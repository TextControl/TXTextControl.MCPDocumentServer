using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

string serverUrl = GetArgument("--url") ?? "http://127.0.0.1:5000/mcp";
string label = GetArgument("--label") ?? "worker-pool";
string outputPath = GetArgument("--output") ?? $"benchmark-{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
int? serverProcessId = int.TryParse(GetArgument("--pid"), out int parsedPid) ? parsedPid : null;
using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var scenarios = new List<ScenarioResult>();
int requestId = 0;

JsonObject small = await MeasureSingleAsync("cold-create-small", async () =>
    await CallToolAsync("create_document_from_markdown", new
    {
        request = new { markdown = "# Worker Pool Benchmark\n\nA small representative business document." },
    }));
string smallSession = RequireString(small, "sessionId");

await MeasureAsync("same-session-10-pdf-exports", 10, async _ =>
    await CallToolAsync("create_document_export", new
    {
        request = new { sessionId = smallSession, format = "pdf" },
    }));

await MeasureAsync("same-session-10-append-edits", 10, async index =>
    await CallToolAsync("apply_operations", new
    {
        request = new
        {
            sessionId = smallSession,
            createIfMissing = false,
            operations = new[] { new { type = "append_paragraph", text = $"Sequential edit {index}" } },
        },
    }));

await MeasureAsync("persistent-control-10-new-documents", 10, async _ =>
    await CallToolAsync("create_empty_document", new { }));

var independentSessions = new List<string>();
for (int index = 0; index < 6; index++)
{
    JsonObject created = await CallToolAsync("create_document_from_markdown", new
    {
        request = new { markdown = $"# Independent session {index}\n\nParallel worker-pool benchmark content." },
    });
    independentSessions.Add(RequireString(created, "sessionId"));
}

await MeasureConcurrentAsync("cross-session-concurrent-pdf-exports", independentSessions.Select(sessionId =>
    (Func<Task<JsonObject>>)(() => CallToolAsync("create_document_export", new
    {
        request = new { sessionId, format = "pdf" },
    }))));

await MeasureConversationAsync();
await MeasureDocxToPdfAsync(smallSession);
await MeasureMarkdownToDocxAsync();

string paragraph = string.Join(' ', Enumerable.Repeat(
    "This representative paragraph exercises pagination, layout, serialization, and PDF export.",
    12));
string largeMarkdown = "# Large benchmark document\n\n" + string.Join(
    "\n\n",
    Enumerable.Range(1, 100).Select(index => $"## Section {index}\n\n{paragraph}"));
JsonObject large = await CallToolAsync("create_document_from_markdown", new
{
    request = new { markdown = largeMarkdown },
});
string largeSession = RequireString(large, "sessionId");
await MeasureAsync("large-document-5-pdf-exports", 5, async _ =>
    await CallToolAsync("create_document_export", new
    {
        request = new { sessionId = largeSession, format = "pdf" },
    }));

long? workingSetBytes = null;
if (serverProcessId.HasValue)
{
    try
    {
        using Process process = Process.GetProcessById(serverProcessId.Value);
        workingSetBytes = process.WorkingSet64;
    }
    catch (ArgumentException)
    {
    }
}

long? workerWorkingSetBytes = null;
try
{
    string healthUrl = serverUrl.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase)
        ? serverUrl[..^4] + "/health/document-workers"
        : new Uri(new Uri(serverUrl), "/health/document-workers").ToString();
    using JsonDocument health = JsonDocument.Parse(await client.GetStringAsync(healthUrl));
    if (health.RootElement.TryGetProperty("workerWorkingSetBytes", out JsonElement workerMemory))
    {
        workerWorkingSetBytes = workerMemory.GetInt64();
    }
}
catch (HttpRequestException)
{
}

var report = new
{
    label,
    serverUrl,
    recordedUtc = DateTime.UtcNow,
    serverProcessId,
    workingSetBytes,
    workerWorkingSetBytes,
    totalWorkingSetBytes = workingSetBytes.GetValueOrDefault() + workerWorkingSetBytes.GetValueOrDefault(),
    failureCount = scenarios.Sum(scenario => scenario.Failures),
    scenarios,
};
string reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, reportJson);
Console.WriteLine(reportJson);
Console.WriteLine($"Report: {Path.GetFullPath(outputPath)}");
return;

async Task<JsonObject> CallToolAsync(string name, object arguments)
{
    int id = Interlocked.Increment(ref requestId);
    using HttpRequestMessage request = new(HttpMethod.Post, serverUrl)
    {
        Content = JsonContent.Create(new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new { name, arguments },
        }),
    };
    request.Headers.Accept.ParseAdd("application/json");
    request.Headers.Accept.ParseAdd("text/event-stream");
    using HttpResponseMessage response = await client.SendAsync(request);
    string content = await response.Content.ReadAsStringAsync();
    response.EnsureSuccessStatusCode();
    string data = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .First(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];
    JsonObject envelope = JsonNode.Parse(data)?.AsObject()
        ?? throw new InvalidDataException("MCP returned no JSON-RPC envelope.");
    JsonObject result = envelope["result"]?.AsObject()
        ?? throw new InvalidDataException(envelope["error"]?.ToJsonString() ?? "MCP returned no result.");
    if (result["isError"]?.GetValue<bool>() == true)
    {
        throw new InvalidOperationException(result.ToJsonString());
    }

    string toolJson = result["content"]?[0]?["text"]?.GetValue<string>()
        ?? throw new InvalidDataException("MCP tool returned no JSON text content.");
    return JsonNode.Parse(toolJson)?.AsObject()
        ?? throw new InvalidDataException("MCP tool content was not a JSON object.");
}

async Task<JsonObject> MeasureSingleAsync(string name, Func<Task<JsonObject>> action)
{
    JsonObject? result = null;
    await MeasureAsync(name, 1, async _ => result = await action());
    return result!;
}

async Task MeasureAsync(string name, int count, Func<int, Task<JsonObject>> action)
{
    var timings = new List<double>(count);
    int failures = 0;
    Stopwatch total = Stopwatch.StartNew();
    for (int index = 0; index < count; index++)
    {
        Stopwatch call = Stopwatch.StartNew();
        try
        {
            await action(index);
        }
        catch
        {
            failures++;
        }
        finally
        {
            call.Stop();
            timings.Add(call.Elapsed.TotalMilliseconds);
        }
    }

    total.Stop();
    scenarios.Add(CreateResult(name, timings, total.Elapsed, failures));
}

async Task MeasureConcurrentAsync(string name, IEnumerable<Func<Task<JsonObject>>> actions)
{
    Func<Task<JsonObject>>[] work = actions.ToArray();
    var timings = new double[work.Length];
    int failures = 0;
    Stopwatch total = Stopwatch.StartNew();
    await Task.WhenAll(work.Select(async (action, index) =>
    {
        Stopwatch call = Stopwatch.StartNew();
        try
        {
            await action();
        }
        catch
        {
            Interlocked.Increment(ref failures);
        }
        finally
        {
            call.Stop();
            timings[index] = call.Elapsed.TotalMilliseconds;
        }
    }));
    total.Stop();
    scenarios.Add(CreateResult(name, timings, total.Elapsed, failures));
}

async Task MeasureConversationAsync()
{
    await MeasureAsync("conversation-create-inspect-edit-export", 1, async _ =>
    {
        JsonObject created = await CallToolAsync("create_document_from_markdown", new
        {
            request = new { markdown = "# Agreement\n\nPayment is due in thirty days.\n\nThank you." },
        });
        string sessionId = RequireString(created, "sessionId");
        await CallToolAsync("inspect_document", new
        {
            request = new { sessionId, query = "payment", maxCharacters = 4000 },
        });
        await CallToolAsync("edit_document", new
        {
            request = new
            {
                sessionId,
                matchText = "Payment is due in thirty days.",
                replacementText = "Payment is due in fourteen days.",
            },
        });
        return await CallToolAsync("create_document_export", new
        {
            request = new { sessionId, format = "pdf" },
        });
    });
}

async Task MeasureDocxToPdfAsync(string sessionId)
{
    JsonObject source = await CallToolAsync("get_as_base64", new
    {
        request = new { sessionId, format = "docx" },
    });
    string data = RequireString(source, "base64Document");
    await MeasureAsync("stateless-docx-to-pdf", 3, async _ =>
        await CallToolAsync("convert_document", new
        {
            request = new { data, sourceFormat = "docx", outputFormat = "pdf" },
        }));
}

async Task MeasureMarkdownToDocxAsync()
{
    string data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
        "# Markdown conversion\n\nThis source should convert without rewriting."));
    await MeasureAsync("stateless-markdown-to-docx", 3, async _ =>
        await CallToolAsync("convert_document", new
        {
            request = new { data, sourceFormat = "md", outputFormat = "docx" },
        }));
}

static ScenarioResult CreateResult(string name, IEnumerable<double> values, TimeSpan elapsed, int failures)
{
    double[] sorted = values.Order().ToArray();
    return new ScenarioResult(
        name,
        sorted.Length,
        failures,
        Percentile(sorted, 0.50),
        Percentile(sorted, 0.95),
        Percentile(sorted, 0.99),
        elapsed.TotalSeconds <= 0 ? 0 : sorted.Length / elapsed.TotalSeconds);
}

static double Percentile(double[] sorted, double percentile)
{
    if (sorted.Length == 0)
    {
        return 0;
    }

    int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
    return Math.Round(sorted[Math.Clamp(index, 0, sorted.Length - 1)], 2);
}

static string RequireString(JsonObject value, string propertyName) =>
    value[propertyName]?.GetValue<string>()
    ?? throw new InvalidDataException($"Tool result did not contain '{propertyName}'.");

string? GetArgument(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

internal sealed record ScenarioResult(
    string Name,
    int Calls,
    int Failures,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double ThroughputPerSecond);
