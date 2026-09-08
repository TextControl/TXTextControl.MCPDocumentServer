using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services.Workers;

internal static class DocumentWorkerProcess
{
    public const string WorkerSwitch = "--document-worker";
    public const string CommitFileName = "document.worker.commit.json";

    public static bool IsWorker(IReadOnlyList<string> arguments) =>
        arguments.Contains(WorkerSwitch, StringComparer.Ordinal);

    public static async Task<int> RunAsync(string[] arguments)
    {
        string contentRoot = GetRequiredArgument(arguments, "--content-root");
        string allowedRoot = Path.GetFullPath(GetRequiredArgument(arguments, "--allowed-root"));
        int parentProcessId = int.Parse(
            GetRequiredArgument(arguments, "--parent-pid"),
            System.Globalization.CultureInfo.InvariantCulture);
        int maximumInputBytes = int.Parse(
            GetRequiredArgument(arguments, "--max-input-bytes"),
            System.Globalization.CultureInfo.InvariantCulture);
        int maximumOutputBytes = int.Parse(
            GetRequiredArgument(arguments, "--max-output-bytes"),
            System.Globalization.CultureInfo.InvariantCulture);

        using CancellationTokenSource lifetime = new();
        _ = WatchParentAsync(parentProcessId, lifetime.Token);
        Stopwatch initialization = Stopwatch.StartNew();
        using ServerTextControlDocumentEngine engine = CreateEngine(contentRoot, out var settings);
        initialization.Stop();
        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        await DocumentWorkerProtocol.WriteAsync(
            output,
            new DocumentWorkerReady(DocumentWorkerProtocol.Version, Environment.ProcessId, initialization.ElapsedMilliseconds),
            maximumOutputBytes,
            CancellationToken.None).ConfigureAwait(false);

        while (true)
        {
            DocumentWorkerRequest? request;
            try
            {
                request = await DocumentWorkerProtocol.ReadAsync<DocumentWorkerRequest>(
                    input,
                    maximumInputBytes,
                    lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (request is null)
            {
                break;
            }

            if (request.AutomationSettings is not null)
            {
                settings.ApplyWorkerSnapshot(request.AutomationSettings);
            }

            DocumentWorkerResponse response = Execute(
                engine,
                request,
                allowedRoot,
                maximumInputBytes,
                maximumOutputBytes);
            if (response.Success && request.Mutation && request.SessionKey is not null)
            {
                PersistMutationCommit(request, response, allowedRoot, maximumOutputBytes);
            }
            await DocumentWorkerProtocol.WriteAsync(
                output,
                response,
                maximumOutputBytes,
                CancellationToken.None).ConfigureAwait(false);
        }

        lifetime.Cancel();
        return 0;
    }

    private static DocumentWorkerResponse Execute(
        ServerTextControlDocumentEngine engine,
        DocumentWorkerRequest request,
        string allowedRoot,
        int maximumInputBytes,
        int maximumOutputBytes)
    {
        Stopwatch execution = Stopwatch.StartNew();
        engine.BeginCommandTiming();
        try
        {
            if (request.ProtocolVersion != DocumentWorkerProtocol.Version)
            {
                throw new InvalidOperationException($"Unsupported document worker protocol version {request.ProtocolVersion}.");
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > request.DeadlineUnixMilliseconds)
            {
                throw new TimeoutException("The document command deadline elapsed before execution.");
            }

            // The host serializes each session and owns its authoritative revision.
            // A session may legitimately move after a worker restart or affinity expiry,
            // so a worker must not reject a host revision based on local process memory.
            long currentRevision = request.ExpectedRevision ?? 0;

            object? result = Dispatch(engine, request.Command, request.Payload, allowedRoot, maximumInputBytes);
            execution.Stop();
            ServerTextControlDocumentEngine.EngineCommandTiming engineTiming = engine.CompleteCommandTiming();
            long? newRevision = null;
            if (request.Mutation && request.SessionKey is not null)
            {
                newRevision = currentRevision + 1;
            }

            Stopwatch serialization = Stopwatch.StartNew();
            JsonElement resultJson = result is null
                ? JsonSerializer.SerializeToElement<object?>(null, DocumentWorkerProtocol.JsonOptions)
                : JsonSerializer.SerializeToElement(result, result.GetType(), DocumentWorkerProtocol.JsonOptions);
            if (JsonSerializer.SerializeToUtf8Bytes(resultJson, DocumentWorkerProtocol.JsonOptions).Length > maximumOutputBytes)
            {
                throw new InvalidOperationException("The document worker result exceeds the configured output limit.");
            }

            serialization.Stop();
            return new DocumentWorkerResponse(
                DocumentWorkerProtocol.Version,
                request.RequestId,
                true,
                resultJson,
                newRevision,
                request.MutationId,
                null,
                new DocumentWorkerTiming(
                    engineTiming.LoadMilliseconds,
                    execution.ElapsedMilliseconds,
                    engineTiming.SaveMilliseconds,
                    serialization.ElapsedMilliseconds,
                    engineTiming.CacheHit));
        }
        catch (Exception exception)
        {
            execution.Stop();
            Exception error = exception.GetBaseException();
            return new DocumentWorkerResponse(
                DocumentWorkerProtocol.Version,
                request.RequestId,
                false,
                null,
                null,
                request.MutationId,
                new DocumentWorkerError(
                    error is TimeoutException ? "timeout" : error is ArgumentException ? "invalid_request" : "engine_error",
                    error.Message,
                    error.ToString()),
                new DocumentWorkerTiming(0, execution.ElapsedMilliseconds, 0, 0, null));
        }
    }

    private static void PersistMutationCommit(
        DocumentWorkerRequest request,
        DocumentWorkerResponse response,
        string allowedRoot,
        int maximumBytes)
    {
        if (!request.Payload.TryGetProperty("workingDocumentPath", out JsonElement pathProperty)
            || string.IsNullOrWhiteSpace(pathProperty.GetString()))
        {
            throw new InvalidDataException("A mutating document command must include workingDocumentPath.");
        }

        string workingPath = ValidatePath(pathProperty.GetString()!, allowedRoot);
        string directory = Path.GetDirectoryName(workingPath)
            ?? throw new InvalidDataException("The working document has no parent directory.");
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(response, DocumentWorkerProtocol.JsonOptions);
        if (data.Length > maximumBytes)
        {
            throw new InvalidOperationException("The mutation commit record exceeds the configured output limit.");
        }

        string commitPath = Path.Combine(directory, CommitFileName);
        string temporaryPath = Path.Combine(directory, $".{CommitFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, commitPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static object? Dispatch(
        ServerTextControlDocumentEngine engine,
        string command,
        JsonElement payload,
        string allowedRoot,
        int maximumInputBytes)
    {
        return command switch
        {
            "unload" => Execute<EmptyPayload>(payload, _ =>
            {
                engine.UnloadActiveDocument();
                return true;
            }),
            "create_empty" => Execute<CreateEmptyPayload>(payload, value =>
                engine.CreateEmpty(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "load_base64" => Execute<LoadBase64Payload>(payload, value =>
                engine.LoadFromBase64(value.Base64Document, ValidatePath(value.WorkingDocumentPath, allowedRoot), value.State, value.SourceFormat)),
            "load_file" => Execute<LoadFilePayload>(payload, value =>
                engine.LoadFromBytes(
                    ReadInputFile(value.InputPath, allowedRoot, maximumInputBytes),
                    ValidatePath(value.WorkingDocumentPath, allowedRoot),
                    value.State,
                    value.SourceFormat)),
            "load_markdown_presets" => Execute<MarkdownPayload>(payload, value =>
                engine.LoadMarkdownWithPresetStyles(value.Markdown, ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "apply_presets" => Execute<StatePayload>(payload, value =>
                engine.ApplyPresetStyles(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.State)),
            "convert_base64" => Execute<ConvertPayload>(payload, value =>
                engine.ConvertFromBase64ToFile(
                    value.Base64Document,
                    value.SourceFormat,
                    ValidatePath(value.WorkingDocumentPath, allowedRoot),
                    ValidatePath(value.OutputPath, allowedRoot),
                    value.OutputFormat)),
            "convert_file" => Execute<ConvertFilePayload>(payload, value =>
                engine.ConvertFromBytesToFile(
                    ReadInputFile(value.InputPath, allowedRoot, maximumInputBytes),
                    value.SourceFormat,
                    ValidatePath(value.WorkingDocumentPath, allowedRoot),
                    ValidatePath(value.OutputPath, allowedRoot),
                    value.OutputFormat)),
            "get_base64" => Execute<FormatPayload>(payload, value =>
                engine.GetAsBase64(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.Format)),
            "export_file" => Execute<ExportPayload>(payload, value =>
            {
                engine.ExportToFile(
                    ValidatePath(value.WorkingDocumentPath, allowedRoot),
                    ValidatePath(value.OutputPath, allowedRoot),
                    value.Format);
                return true;
            }),
            "apply_operations" => Execute<OperationsPayload>(payload, value =>
                engine.ApplyOperations(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.State, value.Request)),
            "format_text" => Execute<FormatTextPayload>(payload, value =>
                engine.FormatText(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.Request)),
            "get_paragraphs" => Execute<ParagraphsPayload>(payload, value =>
                engine.GetParagraphs(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.Start, value.End)),
            "search_text" => Execute<SearchPayload>(payload, value =>
                engine.SearchText(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.Text, value.MatchCase, value.WholeWord)),
            "search_ranges" => Execute<SearchPayload>(payload, value =>
                engine.SearchTextRanges(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.Text, value.MatchCase, value.WholeWord)),
            "get_text" => Execute<PathPayload>(payload, value =>
                engine.GetText(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "edit_document" => Execute<EditPayload>(payload, value =>
                engine.EditDocument(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.Request)),
            "content_snapshot" => Execute<PathPayload>(payload, value =>
                engine.GetContentSnapshot(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "document_styles" => Execute<PathPayload>(payload, value =>
                engine.GetDocumentStyleSnapshots(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "template_fields" => Execute<PathPayload>(payload, value =>
                engine.GetTemplateMergeFields(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "template_blocks" => Execute<PathPayload>(payload, value =>
                engine.GetTemplateMergeBlocks(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "template_form_fields" => Execute<PathPayload>(payload, value =>
                engine.GetTemplateFormFields(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "template_snapshot" => Execute<PathPayload>(payload, value =>
                engine.GetTemplateContentSnapshot(ValidatePath(value.WorkingDocumentPath, allowedRoot))),
            "merge_template" => Execute<MergePayload>(payload, value =>
                engine.MergeTemplate(ValidatePath(value.WorkingDocumentPath, allowedRoot), value.State, value.Request)),
            _ => throw new InvalidOperationException($"Unknown document worker command '{command}'."),
        };
    }

    private static object? Execute<TPayload>(JsonElement payload, Func<TPayload, object?> action)
    {
        TPayload value = payload.Deserialize<TPayload>(DocumentWorkerProtocol.JsonOptions)
            ?? throw new InvalidDataException("Document worker command payload is empty.");
        return action(value);
    }

    private static string ValidatePath(string value, string allowedRoot)
    {
        string fullPath = Path.GetFullPath(value);
        string rootPrefix = allowedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw new UnauthorizedAccessException("Document worker paths must remain inside MCP-managed storage.");
        }

        return fullPath;
    }

    private static byte[] ReadInputFile(string path, string allowedRoot, int maximumBytes)
    {
        string fullPath = ValidatePath(path, allowedRoot);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The staged document input was not found.", fullPath);
        }

        if (file.Length > maximumBytes)
        {
            throw new InvalidOperationException("The staged document exceeds the configured input limit.");
        }

        return File.ReadAllBytes(fullPath);
    }

    private static ServerTextControlDocumentEngine CreateEngine(string contentRoot, out AutomationSettingsService settings)
    {
        string settingsPath = Path.Combine(contentRoot, "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        DocumentAutomationOptions options = configuration
            .GetSection(DocumentAutomationOptions.SectionName)
            .Get<DocumentAutomationOptions>() ?? new DocumentAutomationOptions();
        IOptions<DocumentAutomationOptions> wrappedOptions = Microsoft.Extensions.Options.Options.Create(options);
        ICapabilityPack[] packs =
        [
            new BasicTextCapabilityPack(), new MediaCapabilityPack(), new TableCapabilityPack(),
            new FieldsCapabilityPack(), new SectionCapabilityPack(), new HeaderFooterCapabilityPack(),
        ];
        IDocumentOperationHandler[] handlers =
        [
            new DefineStyleOperationHandler(), new RenameStyleOperationHandler(),
            new DeleteStyleOperationHandler(), new CreateStylesFromParagraphsOperationHandler(),
            new AppendParagraphOperationHandler(),
            new ApplyStyleToParagraphOperationHandler(), new FormatParagraphsOperationHandler(),
            new FormatTextOccurrencesOperationHandler(), new ReplaceTextOperationHandler(),
            new AppendImageOperationHandler(), new AppendTableOperationHandler(options),
            new SetTableCellTextOperationHandler(), new FormatTableCellOperationHandler(),
            new FormatTableHeaderRowOperationHandler(), new FormatTableColumnOperationHandler(),
            new ApplyTableStylePresetOperationHandler(options), new AddTableRowOperationHandler(),
            new AppendMergeFieldOperationHandler(), new UpdateMergeFieldOperationHandler(),
            new ClearApplicationFieldsOperationHandler(), new AppendMergeBlockOperationHandler(),
            new AppendFormFieldOperationHandler(), new UpdateFormFieldOperationHandler(),
            new ClearFormFieldsOperationHandler(), new InsertSectionBreakOperationHandler(),
            new SetSectionLayoutOperationHandler(), new SetHeaderFooterOperationHandler(),
        ];
        settings = new AutomationSettingsService(options, settingsPath, packs, handlers);
        var registry = new DocumentOperationRegistry(handlers, packs, settings);
        return new ServerTextControlDocumentEngine(registry, wrappedOptions, usePersistentControl: true);
    }

    private static async Task WatchParentAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using Process parent = Process.GetProcessById(parentProcessId);
                if (parent.HasExited)
                {
                    Environment.Exit(3);
                }
            }
            catch (ArgumentException)
            {
                Environment.Exit(3);
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetRequiredArgument(IReadOnlyList<string> arguments, string name)
    {
        int index = -1;
        for (int candidate = 0; candidate < arguments.Count; candidate++)
        {
            if (arguments[candidate].Equals(name, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }
        }
        if (index < 0 || index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            throw new ArgumentException($"Worker argument '{name}' is required.");
        }

        return arguments[index + 1];
    }

    private sealed record PathPayload(string WorkingDocumentPath);
    private sealed record EmptyPayload;
    private sealed record StatePayload(string WorkingDocumentPath, DocumentState State);
    private sealed record CreateEmptyPayload(string WorkingDocumentPath);
    private sealed record LoadBase64Payload(string Base64Document, string WorkingDocumentPath, DocumentState? State, string? SourceFormat);
    private sealed record LoadFilePayload(string InputPath, string WorkingDocumentPath, DocumentState? State, string? SourceFormat);
    private sealed record MarkdownPayload(string Markdown, string WorkingDocumentPath);
    private sealed record ConvertPayload(string Base64Document, string? SourceFormat, string WorkingDocumentPath, string OutputPath, string OutputFormat);
    private sealed record ConvertFilePayload(string InputPath, string? SourceFormat, string WorkingDocumentPath, string OutputPath, string OutputFormat);
    private sealed record FormatPayload(string WorkingDocumentPath, string Format);
    private sealed record ExportPayload(string WorkingDocumentPath, string OutputPath, string Format);
    private sealed record OperationsPayload(string WorkingDocumentPath, DocumentState State, ApplyOperationsRequest Request);
    private sealed record FormatTextPayload(string WorkingDocumentPath, FormatTextRequest Request);
    private sealed record ParagraphsPayload(string WorkingDocumentPath, int? Start, int? End);
    private sealed record SearchPayload(string WorkingDocumentPath, string Text, bool MatchCase, bool WholeWord);
    private sealed record EditPayload(string WorkingDocumentPath, EditDocumentRequest Request);
    private sealed record MergePayload(string WorkingDocumentPath, DocumentState State, MergeTemplateRequest Request);
}
