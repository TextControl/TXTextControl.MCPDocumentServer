using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services.Workers;

/// <summary>
/// Routes the existing document engine contract to bounded, long-lived worker processes.
/// Tool and workflow code therefore remains transport-agnostic.
/// </summary>
internal sealed class WorkerPooledDocumentEngine(
    DocumentWorkerPool pool,
    IOptions<DocumentWorkerPoolOptions> configuredOptions) : ITxDocumentEngine
{
    private readonly DocumentWorkerPoolOptions options = configuredOptions.Value;

    public DocumentState CreateEmpty(string workingDocumentPath) =>
        Execute<DocumentState>("create_empty", new { workingDocumentPath }, workingDocumentPath, mutation: true);

    public DocumentState LoadFromBase64(
        string base64Document,
        string workingDocumentPath,
        DocumentState? existingState = null,
        string? sourceFormat = null) =>
        ExecuteWithStagedInput<DocumentState>(
            "load_file",
            base64Document,
            workingDocumentPath,
            inputPath => new { inputPath, workingDocumentPath, state = existingState, sourceFormat },
            mutation: true);

    public DocumentPresetStyleEngineResult LoadMarkdownWithPresetStyles(
        string markdown,
        string workingDocumentPath) =>
        Execute<DocumentPresetStyleEngineResult>(
            "load_markdown_presets",
            new { markdown, workingDocumentPath },
            workingDocumentPath,
            mutation: true);

    public DocumentPresetStyleEngineResult ApplyPresetStyles(
        string workingDocumentPath,
        DocumentState state) =>
        Execute<DocumentPresetStyleEngineResult>(
            "apply_presets",
            new { workingDocumentPath, state },
            workingDocumentPath,
            mutation: true);

    public DocumentState ConvertFromBase64ToFile(
        string base64Document,
        string? sourceFormat,
        string workingDocumentPath,
        string outputPath,
        string outputFormat) =>
        ExecuteWithStagedInput<DocumentState>(
            "convert_file",
            base64Document,
            workingDocumentPath,
            inputPath => new { inputPath, sourceFormat, workingDocumentPath, outputPath, outputFormat },
            mutation: true);

    public string GetAsBase64(string workingDocumentPath, string format) =>
        Execute<string>("get_base64", new { workingDocumentPath, format }, workingDocumentPath, mutation: false);

    public void ExportToFile(string workingDocumentPath, string outputPath, string format) =>
        _ = Execute<bool>(
            "export_file",
            new { workingDocumentPath, outputPath, format },
            workingDocumentPath,
            mutation: false);

    public DocumentState ApplyOperations(
        string workingDocumentPath,
        DocumentState state,
        ApplyOperationsRequest request) =>
        Execute<DocumentState>(
            "apply_operations",
            new { workingDocumentPath, state, request },
            workingDocumentPath,
            mutation: true);

    public DocumentState FormatText(string workingDocumentPath, FormatTextRequest request) =>
        Execute<DocumentState>(
            "format_text",
            new { workingDocumentPath, request },
            workingDocumentPath,
            mutation: true);

    public IReadOnlyList<string> GetParagraphs(string workingDocumentPath, int? start = null, int? end = null) =>
        Execute<List<string>>(
            "get_paragraphs",
            new { workingDocumentPath, start, end },
            workingDocumentPath,
            mutation: false);

    public IReadOnlyList<int> SearchText(
        string workingDocumentPath,
        string text = "",
        bool matchCase = false,
        bool wholeWord = false) =>
        Execute<List<int>>(
            "search_text",
            new { workingDocumentPath, text, matchCase, wholeWord },
            workingDocumentPath,
            mutation: false);

    public IReadOnlyList<SearchTextRange> SearchTextRanges(
        string workingDocumentPath,
        string text = "",
        bool matchCase = false,
        bool wholeWord = false) =>
        Execute<List<SearchTextRange>>(
            "search_ranges",
            new { workingDocumentPath, text, matchCase, wholeWord },
            workingDocumentPath,
            mutation: false);

    public string GetText(string workingDocumentPath) =>
        Execute<string>("get_text", new { workingDocumentPath }, workingDocumentPath, mutation: false);

    public DocumentEditEngineResult EditDocument(string workingDocumentPath, EditDocumentRequest request) =>
        Execute<DocumentEditEngineResult>(
            "edit_document",
            new { workingDocumentPath, request },
            workingDocumentPath,
            mutation: true);

    public DocumentContentSnapshot GetContentSnapshot(string workingDocumentPath) =>
        Execute<DocumentContentSnapshot>(
            "content_snapshot",
            new { workingDocumentPath },
            workingDocumentPath,
            mutation: false);

    public IReadOnlyList<StyleInspection> GetDocumentStyleSnapshots(string workingDocumentPath) =>
        Execute<List<StyleInspection>>(
            "document_styles",
            new { workingDocumentPath },
            workingDocumentPath,
            mutation: false);

    public IReadOnlyList<TemplateMergeFieldInfo> GetTemplateMergeFields(string workingDocumentPath) =>
        Execute<List<TemplateMergeFieldInfo>>(
            "template_fields",
            new { workingDocumentPath },
            workingDocumentPath,
            mutation: false);

    public IReadOnlyList<TemplateMergeBlockInfo> GetTemplateMergeBlocks(string workingDocumentPath) =>
        Execute<List<TemplateMergeBlockInfo>>(
            "template_blocks",
            new { workingDocumentPath },
            workingDocumentPath,
            mutation: false);

    public IReadOnlyList<TemplateFormFieldInfo> GetTemplateFormFields(string workingDocumentPath) =>
        Execute<List<TemplateFormFieldInfo>>(
            "template_form_fields",
            new { workingDocumentPath },
            workingDocumentPath,
            mutation: false);

    public TemplateContentSnapshot GetTemplateContentSnapshot(string workingDocumentPath) =>
        Execute<TemplateContentSnapshot>(
            "template_snapshot",
            new { workingDocumentPath },
            workingDocumentPath,
            mutation: false);

    public MergeTemplateEngineResult MergeTemplate(
        string workingDocumentPath,
        DocumentState state,
        MergeTemplateRequest request) =>
        Execute<MergeTemplateEngineResult>(
            "merge_template",
            new { workingDocumentPath, state, request },
            workingDocumentPath,
            mutation: true);

    private T Execute<T>(string command, object payload, string sessionPath, bool mutation) =>
        pool.Execute<T>(command, payload, NormalizeSessionKey(sessionPath), mutation);

    private T ExecuteWithStagedInput<T>(
        string command,
        string base64Document,
        string sessionPath,
        Func<string, object> createPayload,
        bool mutation)
    {
        byte[] input = DecodeBase64(base64Document);
        int maximumBytes = checked(options.MaximumInputMegabytes * 1024 * 1024);
        if (input.Length > maximumBytes)
        {
            throw new InvalidOperationException(
                $"The uploaded document exceeds the configured {options.MaximumInputMegabytes} MB input limit.");
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(sessionPath))
            ?? throw new ArgumentException("The session document path has no parent directory.", nameof(sessionPath));
        Directory.CreateDirectory(directory);
        string inputPath = Path.Combine(directory, $".worker-input-{Guid.NewGuid():N}.bin");
        try
        {
            using (var stream = new FileStream(
                inputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.WriteThrough))
            {
                stream.Write(input);
                stream.Flush(flushToDisk: true);
            }

            return Execute<T>(command, createPayload(inputPath), sessionPath, mutation);
        }
        finally
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private static byte[] DecodeBase64(string base64Document)
    {
        if (string.IsNullOrWhiteSpace(base64Document))
        {
            throw new ArgumentException("Base64 document content is required.", nameof(base64Document));
        }

        string payload = base64Document.Trim();
        int markerIndex = payload.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            payload = payload[(markerIndex + "base64,".Length)..];
        }

        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("The provided content is not valid base64.", exception);
        }
    }

    private static string NormalizeSessionKey(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return directory is null
            ? throw new ArgumentException("The session document path has no parent directory.", nameof(path))
            : Path.GetFileName(directory);
    }
}
