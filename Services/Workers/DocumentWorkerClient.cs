using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services.Workers;

internal sealed class DocumentWorkerClient : IAsyncDisposable
{
    private const int DiagnosticTailCapacity = 80;
    private readonly int workerIndex;
    private readonly string contentRoot;
    private readonly string allowedRoot;
    private readonly DocumentWorkerPoolOptions options;
    private readonly ConcurrentQueue<string> diagnosticTail = new();
    private Process? process;
    private Stream? input;
    private Stream? output;
    private int restartCount;

    public DocumentWorkerClient(
        int workerIndex,
        string contentRoot,
        string allowedRoot,
        DocumentWorkerPoolOptions options)
    {
        this.workerIndex = workerIndex;
        this.contentRoot = contentRoot;
        this.allowedRoot = allowedRoot;
        this.options = options;
    }

    public SemaphoreSlim ExecutionGate { get; } = new(1, 1);
    public int WorkerIndex => workerIndex;
    public bool IsHealthy => process is { HasExited: false };
    public int ProcessId => process?.Id ?? 0;
    public long WorkingSetBytes
    {
        get
        {
            try
            {
                return process is { HasExited: false } child ? child.WorkingSet64 : 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }
    public int RestartCount => restartCount;
    public string? HotSessionKey { get; set; }
    public DateTime LastUsedUtc { get; set; }

    public async Task<DocumentWorkerReady> StartAsync(CancellationToken cancellationToken)
    {
        await StopAsync().ConfigureAwait(false);
        ProcessStartInfo startInfo = CreateStartInfo();
        var child = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!child.Start())
        {
            child.Dispose();
            throw new InvalidOperationException($"Document worker {workerIndex} could not start.");
        }

        process = child;
        input = child.StandardInput.BaseStream;
        output = child.StandardOutput.BaseStream;
        _ = PumpDiagnosticsAsync(child.StandardError);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
        try
        {
            DocumentWorkerReady ready = await DocumentWorkerProtocol.ReadAsync<DocumentWorkerReady>(
                output,
                checked(options.MaximumOutputMegabytes * 1024 * 1024),
                timeout.Token).ConfigureAwait(false)
                ?? throw new EndOfStreamException($"Document worker {workerIndex} exited before its ready message.");
            if (ready.ProtocolVersion != DocumentWorkerProtocol.Version)
            {
                throw new InvalidOperationException(
                    $"Document worker {workerIndex} uses protocol {ready.ProtocolVersion}; expected {DocumentWorkerProtocol.Version}.");
            }

            return ready;
        }
        catch (Exception exception)
        {
            await StopAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Document worker {workerIndex} failed to initialize. {FormatDiagnosticTail()}",
                exception);
        }
    }

    public async Task<DocumentWorkerResponse> ExecuteAsync(
        DocumentWorkerRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsHealthy || input is null || output is null)
        {
            throw new InvalidOperationException($"Document worker {workerIndex} is not running. {FormatDiagnosticTail()}");
        }

        await DocumentWorkerProtocol.WriteAsync(
            input,
            request,
            checked(options.MaximumInputMegabytes * 1024 * 1024),
            cancellationToken).ConfigureAwait(false);
        DocumentWorkerResponse response = await DocumentWorkerProtocol.ReadAsync<DocumentWorkerResponse>(
            output,
            checked(options.MaximumOutputMegabytes * 1024 * 1024),
            cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException($"Document worker {workerIndex} closed its response stream. {FormatDiagnosticTail()}");
        if (!response.RequestId.Equals(request.RequestId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Document worker {workerIndex} returned correlation id '{response.RequestId}' for '{request.RequestId}'.");
        }

        return response;
    }

    public async Task<DocumentWorkerReady> RestartAsync(CancellationToken cancellationToken)
    {
        if (restartCount >= options.WorkerRestartLimit)
        {
            throw new InvalidOperationException(
                $"Document worker {workerIndex} exceeded its restart limit. {FormatDiagnosticTail()}");
        }

        restartCount++;
        DocumentWorkerMetrics.WorkerRestarts.Add(1);
        if (options.WorkerRestartBackoffMilliseconds > 0)
        {
            await Task.Delay(options.WorkerRestartBackoffMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        HotSessionKey = null;
        return await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        Stream? standardInput = input;
        input = null;
        output = null;
        if (standardInput is not null)
        {
            try
            {
                await standardInput.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        Process? child = process;
        process = null;
        if (child is null)
        {
            return;
        }

        try
        {
            if (!child.HasExited && !child.WaitForExit(2_000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(5_000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            child.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        ExecutionGate.Dispose();
    }

    private ProcessStartInfo CreateStartInfo()
    {
        string configuredPath = string.IsNullOrWhiteSpace(options.WorkerExecutablePath)
            ? string.Empty
            : Path.GetFullPath(options.WorkerExecutablePath);
        bool configuredDll = configuredPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        string processPath = configuredDll
            ? "dotnet"
            : configuredPath.Length > 0
                ? configuredPath
                : Environment.ProcessPath
                    ?? throw new InvalidOperationException("The MCP host executable path is unavailable.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = contentRoot,
        };
        if (configuredDll)
        {
            startInfo.ArgumentList.Add(configuredPath);
        }
        else if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string entryAssembly = Assembly.GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException("The MCP entry assembly path is unavailable.");
            startInfo.ArgumentList.Add(entryAssembly);
        }

        AddArgument(startInfo, DocumentWorkerProcess.WorkerSwitch);
        AddArgument(startInfo, "--content-root", contentRoot);
        AddArgument(startInfo, "--allowed-root", allowedRoot);
        AddArgument(startInfo, "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--max-input-bytes", checked(options.MaximumInputMegabytes * 1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--max-output-bytes", checked(options.MaximumOutputMegabytes * 1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string? value = null)
    {
        startInfo.ArgumentList.Add(name);
        if (value is not null)
        {
            startInfo.ArgumentList.Add(value);
        }
    }

    private async Task PumpDiagnosticsAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                diagnosticTail.Enqueue(line);
                while (diagnosticTail.Count > DiagnosticTailCapacity && diagnosticTail.TryDequeue(out _))
                {
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private string FormatDiagnosticTail() => diagnosticTail.IsEmpty
        ? "No worker diagnostics were captured."
        : string.Join(Environment.NewLine, diagnosticTail);
}
