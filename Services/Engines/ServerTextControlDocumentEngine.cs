using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Operations;
using TXTextControl;
using TXTextControl.Markdown;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Simplified TX Text Control document engine.
/// Document-oriented operations.
/// </summary>
public sealed partial class ServerTextControlDocumentEngine : ITxDocumentEngine, IDisposable
{
    private readonly DocumentOperationRegistry _operationRegistry;
    private readonly DocumentAutomationOptions _automationOptions;
    private readonly PersistentServerTextControl? _persistentControl;
    private string? _loadedDocumentPath;
    private long _loadedDocumentLength;
    private DateTime _loadedDocumentWriteUtc;
    private bool _loadedWithTemplateSettings;
    private long _commandLoadMilliseconds;
    private long _commandSaveMilliseconds;
    private bool? _commandCacheHit;

    public ServerTextControlDocumentEngine(
        DocumentOperationRegistry operationRegistry,
        IOptions<DocumentAutomationOptions> automationOptions)
        : this(operationRegistry, automationOptions, usePersistentControl: false)
    {
    }

    internal ServerTextControlDocumentEngine(
        DocumentOperationRegistry operationRegistry,
        IOptions<DocumentAutomationOptions> automationOptions,
        bool usePersistentControl)
    {
        TxTextControlLicensing.Configure();
        _operationRegistry = operationRegistry;
        _automationOptions = automationOptions.Value;
        _persistentControl = usePersistentControl ? new PersistentServerTextControl() : null;
    }

    private enum DocType
    {
        WordprocessingML = 0,
        SpreadsheetML = 1,
        AdobePDF = 3,
        RichTextFormat = 4,
        HTMLFormat = 5,
        InternalUnicodeFormat = 6,
        PlainText = 7,
        Markdown = 8
    }

    private static readonly Dictionary<DocType, byte[]> HintFormats = new()
    {
        [DocType.WordprocessingML] = new byte[] { 80, 75, 3, 4 },
        [DocType.SpreadsheetML] = new byte[] { 80, 75, 3, 4 },
        [DocType.AdobePDF] = new byte[] { 37, 80, 68, 70 },
        [DocType.RichTextFormat] = new byte[] { 123, 92, 114, 116 },
        [DocType.InternalUnicodeFormat] = new byte[] { 8, 7 }
    };

    private static readonly HashSet<DocType> ExcludeHtml =
    [
        DocType.WordprocessingML,
        DocType.SpreadsheetML,
        DocType.AdobePDF,
        DocType.InternalUnicodeFormat
    ];

    public DocumentState CreateEmpty(string workingDocumentPath)
    {
        EnsureDirectory(workingDocumentPath);

        using (var tx = CreateServerTextControl())
        {
            ResetDocument(tx);
            SaveWorkingDocument(tx, workingDocumentPath);
        }

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = CreateNeutralDocument(),
            ContentSnapshot = new DocumentContentSnapshot(string.Empty, Array.Empty<string>())
        };
    }

    public DocumentState LoadFromBase64(
        string base64Document,
        string workingDocumentPath,
        DocumentState? existingState = null,
        string? sourceFormat = null)
    {
        if (string.IsNullOrWhiteSpace(base64Document))
        {
            throw new ArgumentException("Base64 document content is required.", nameof(base64Document));
        }

        return LoadFromBytes(
            DecodeBase64Document(base64Document),
            workingDocumentPath,
            existingState,
            sourceFormat);
    }

    internal DocumentState LoadFromBytes(
        byte[] documentBytes,
        string workingDocumentPath,
        DocumentState? existingState = null,
        string? sourceFormat = null)
    {
        ArgumentNullException.ThrowIfNull(documentBytes);
        EnsureDirectory(workingDocumentPath);
        string sourceContentHash = ComputeSourceContentHash(documentBytes, sourceFormat);
        if (File.Exists(workingDocumentPath)
            && string.Equals(existingState?.SourceContentHash, sourceContentHash, StringComparison.Ordinal))
        {
            return existingState!;
        }

        DocumentContentSnapshot contentSnapshot;
        using (var tx = CreateServerTextControl())
        {
            ResetDocument(tx);
            LoadDocumentBytes(tx, documentBytes, sourceFormat);
            contentSnapshot = CreateContentSnapshot(tx);
            SaveWorkingDocument(tx, workingDocumentPath);
        }

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = CreateNeutralDocument(),
            SourceContentHash = sourceContentHash,
            ContentSnapshot = contentSnapshot
        };
    }

    public DocumentState ConvertFromBase64ToFile(
        string base64Document,
        string? sourceFormat,
        string workingDocumentPath,
        string outputPath,
        string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(base64Document))
        {
            throw new ArgumentException("Base64 document content is required.", nameof(base64Document));
        }

        return ConvertFromBytesToFile(
            DecodeBase64Document(base64Document),
            sourceFormat,
            workingDocumentPath,
            outputPath,
            outputFormat);
    }

    internal DocumentState ConvertFromBytesToFile(
        byte[] documentBytes,
        string? sourceFormat,
        string workingDocumentPath,
        string outputPath,
        string outputFormat)
    {
        ArgumentNullException.ThrowIfNull(documentBytes);
        EnsureDirectory(workingDocumentPath);
        EnsureDirectory(outputPath);

        using var tx = CreateServerTextControl();
        ResetDocument(tx);
        LoadDocumentBytes(tx, documentBytes, sourceFormat);
        DocumentContentSnapshot contentSnapshot = CreateContentSnapshot(tx);
        SaveWorkingDocument(tx, workingDocumentPath);
        SaveDocument(tx, outputPath, outputFormat);

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = CreateNeutralDocument(),
            SourceContentHash = ComputeSourceContentHash(documentBytes, sourceFormat),
            ContentSnapshot = contentSnapshot
        };
    }

    private static Models.DocumentModel.Document CreateNeutralDocument()
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Sections =
            [
                new Models.DocumentModel.Section
                {
                    Id = Guid.NewGuid().ToString("N")
                }
            ]
        };

    public string GetAsBase64(string workingDocumentPath, string format)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            throw new ArgumentException("A format is required.", nameof(format));
        }

        var normalizedFormat = format.Trim().ToLowerInvariant();

        using (var tx = CreateServerTextControl())
        {
            LoadWorkingDocument(tx, workingDocumentPath);

            return normalizedFormat switch
            {
                "tx" => SaveBinaryAsBase64(tx, BinaryStreamType.InternalUnicodeFormat),
                "rtf" => SaveStringAsBase64(tx, StringStreamType.RichTextFormat),
                "docx" => SaveBinaryAsBase64(tx, BinaryStreamType.WordprocessingML),
                "pdf" => SaveBinaryAsBase64(tx, BinaryStreamType.AdobePDF),
                "html" => SaveStringAsBase64(tx, StringStreamType.HTMLFormat),
                "md" => SaveMarkdownAsBase64(tx),
                "txt" => SaveStringAsBase64(tx, StringStreamType.PlainText),
                _ => throw new ArgumentException("Unsupported format. Use one of: tx, rtf, docx, pdf, html, md, txt.", nameof(format))
            };
        }
    }

    public void ExportToFile(string workingDocumentPath, string outputPath, string format)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An output path is required.", nameof(outputPath));
        }

        EnsureDirectory(outputPath);
        string normalizedFormat = format.Trim().ToLowerInvariant();
        using var tx = CreateServerTextControl();
        LoadWorkingDocument(tx, workingDocumentPath);

        SaveDocument(tx, outputPath, normalizedFormat);
    }

    private static void SaveDocument(ServerTextControl tx, string outputPath, string normalizedFormat)
    {
        switch (normalizedFormat.Trim().ToLowerInvariant())
        {
            case "tx":
                tx.Save(outputPath, StreamType.InternalUnicodeFormat);
                break;
            case "rtf":
                tx.Save(outputPath, StreamType.RichTextFormat);
                break;
            case "docx":
                tx.Save(outputPath, StreamType.WordprocessingML);
                break;
            case "pdf":
                tx.Save(outputPath, StreamType.AdobePDF);
                break;
            case "html":
                tx.Save(outputPath, StreamType.HTMLFormat);
                break;
            case "md":
                tx.SaveMarkdown(out string markdown);
                File.WriteAllText(outputPath, markdown ?? string.Empty, new UTF8Encoding(false));
                break;
            case "txt":
                tx.Save(outputPath, StreamType.PlainText);
                break;
            default:
                throw new ArgumentException("Unsupported format. Use one of: tx, rtf, docx, pdf, html, md, txt.", nameof(normalizedFormat));
        }
    }

    private static byte[] DecodeBase64Document(string base64Document)
    {
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
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The provided content is not valid base64.", ex);
        }
    }

    private static string ComputeSourceContentHash(byte[] documentBytes, string? sourceFormat)
    {
        string normalizedSourceFormat = string.IsNullOrWhiteSpace(sourceFormat)
            ? "auto"
            : sourceFormat.Trim().TrimStart('.').ToLowerInvariant();
        byte[] hashInput = [.. Encoding.UTF8.GetBytes(normalizedSourceFormat), 0, .. documentBytes];
        return Convert.ToHexString(SHA256.HashData(hashInput));
    }

    private static void LoadDocumentBytes(
        ServerTextControl tx,
        byte[] documentBytes,
        string? sourceFormat)
    {
        var loadSettings = new LoadSettings();
        if (!string.IsNullOrWhiteSpace(sourceFormat)
            && !sourceFormat.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            LoadExactFormat(tx, loadSettings, documentBytes, sourceFormat);
            return;
        }

        DocType guessed = GuessFormat(documentBytes, out bool excludeHtml, out bool matchedSignature);
        string? utf8Text = matchedSignature ? null : TryGetUtf8Text(documentBytes);
        if (!string.IsNullOrWhiteSpace(utf8Text) && IsLikelyMarkdown(utf8Text))
        {
            try
            {
                tx.LoadMarkdown(utf8Text);
                return;
            }
            catch
            {
                // Fall back to TX Text Control's other supported formats.
            }
        }

        if (!TryLoad(tx, loadSettings, documentBytes, guessed, excludeHtml))
        {
            throw new InvalidOperationException("The document format could not be loaded.");
        }
    }

    private static string SaveMarkdownAsBase64(ServerTextControl tx)
    {
        tx.SaveMarkdown(out string value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static string SaveBinaryAsBase64(ServerTextControl tx, BinaryStreamType streamType)
    {
        tx.Save(out byte[] bytes, streamType);
        return Convert.ToBase64String(bytes);
    }

    private static string SaveStringAsBase64(ServerTextControl tx, StringStreamType streamType)
    {
        tx.Save(out string value, streamType);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static DocType GuessFormat(
        byte[] data,
        out bool excludeHtml,
        out bool matchedSignature)
    {
        excludeHtml = false;
        matchedSignature = false;

        foreach (var hint in HintFormats)
        {
            var signature = hint.Value;
            if (data.AsSpan().StartsWith(signature))
            {
                matchedSignature = true;
                excludeHtml = ExcludeHtml.Contains(hint.Key);
                return hint.Key;
            }
        }

        return DocType.WordprocessingML;
    }

    private static bool TryLoad(
        ServerTextControl serverTextControl,
        LoadSettings loadSettings,
        byte[] document,
        DocType startType,
        bool excludeHtml)
    {
        for (var i = (int)startType; i <= (int)DocType.Markdown; i++)
        {
            var current = (DocType)i;
            if (excludeHtml && current == DocType.HTMLFormat)
            {
                continue;
            }

            try
            {
                switch (current)
                {
                    case DocType.WordprocessingML:
                        serverTextControl.Load(document, BinaryStreamType.WordprocessingML, loadSettings);
                        return true;

                    case DocType.AdobePDF:
                        serverTextControl.Load(document, BinaryStreamType.AdobePDF, loadSettings);
                        return true;

                    case DocType.RichTextFormat:
                        serverTextControl.Load(Encoding.UTF8.GetString(document), StringStreamType.RichTextFormat, loadSettings);
                        return true;

                    case DocType.HTMLFormat:
                        serverTextControl.Load(Encoding.UTF8.GetString(document), StringStreamType.HTMLFormat, loadSettings);
                        return true;

                    case DocType.PlainText:
                        serverTextControl.Load(Encoding.UTF8.GetString(document), StringStreamType.PlainText, loadSettings);
                        return true;

                    case DocType.InternalUnicodeFormat:
                        serverTextControl.Load(document, BinaryStreamType.InternalUnicodeFormat, loadSettings);
                        return true;

                    case DocType.Markdown:
                        serverTextControl.LoadMarkdown(Encoding.UTF8.GetString(document));
                        return true;
                }
            }
            catch
            {
                // Try next format
            }
        }

        return false;
    }

    private static void LoadExactFormat(
        ServerTextControl serverTextControl,
        LoadSettings loadSettings,
        byte[] document,
        string sourceFormat)
    {
        string normalized = sourceFormat.Trim().TrimStart('.').ToLowerInvariant();
        switch (normalized)
        {
            case "tx":
                serverTextControl.Load(document, BinaryStreamType.InternalUnicodeFormat, loadSettings);
                break;
            case "docx":
                serverTextControl.Load(document, BinaryStreamType.WordprocessingML, loadSettings);
                break;
            case "pdf":
                serverTextControl.Load(document, BinaryStreamType.AdobePDF, loadSettings);
                break;
            case "rtf":
                serverTextControl.Load(Encoding.UTF8.GetString(document), StringStreamType.RichTextFormat, loadSettings);
                break;
            case "html":
            case "htm":
                serverTextControl.Load(Encoding.UTF8.GetString(document), StringStreamType.HTMLFormat, loadSettings);
                break;
            case "txt":
            case "text":
                serverTextControl.Load(Encoding.UTF8.GetString(document), StringStreamType.PlainText, loadSettings);
                break;
            case "md":
            case "markdown":
                serverTextControl.LoadMarkdown(Encoding.UTF8.GetString(document));
                break;
            default:
                throw new ArgumentException(
                    "sourceFormat must be one of: auto, tx, rtf, docx, html, pdf, md, txt.",
                    nameof(sourceFormat));
        }
    }

    private static string? TryGetUtf8Text(byte[] data)
    {
        try
        {
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLikelyMarkdown(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.TrimStart();
        return trimmed.StartsWith("#", StringComparison.Ordinal)
               || trimmed.StartsWith("- ", StringComparison.Ordinal)
               || trimmed.StartsWith("* ", StringComparison.Ordinal)
               || trimmed.StartsWith(">", StringComparison.Ordinal)
               || value.Contains("```", StringComparison.Ordinal)
               || value.Contains("[", StringComparison.Ordinal) && value.Contains("](", StringComparison.Ordinal);
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private ServerTextControl CreateServerTextControl()
    {
        TxTextControlLicensing.Configure();
        return _persistentControl ?? new ServerTextControl();
    }

    private void ResetDocument(ServerTextControl tx)
    {
        tx.Create();
        ClearLoadedDocument();
    }

    private void LoadWorkingDocument(ServerTextControl tx, string workingDocumentPath, bool templateSettings = false)
    {
        string fullPath = Path.GetFullPath(workingDocumentPath);
        var file = new FileInfo(fullPath);
        if (_persistentControl is not null
            && string.Equals(_loadedDocumentPath, fullPath, StringComparison.OrdinalIgnoreCase)
            && _loadedDocumentLength == file.Length
            && _loadedDocumentWriteUtc == file.LastWriteTimeUtc
            && _loadedWithTemplateSettings == templateSettings)
        {
            _commandCacheHit = true;
            return;
        }

        Stopwatch load = Stopwatch.StartNew();
        tx.Create();
        if (templateSettings)
        {
            tx.Load(fullPath, StreamType.InternalUnicodeFormat, CreateTemplateLoadSettings());
        }
        else
        {
            tx.Load(fullPath, StreamType.InternalUnicodeFormat);
        }

        load.Stop();
        _commandLoadMilliseconds += load.ElapsedMilliseconds;
        _commandCacheHit = false;
        CaptureLoadedDocument(fullPath, templateSettings);
    }

    private void SaveWorkingDocument(ServerTextControl tx, string workingDocumentPath)
    {
        string fullPath = Path.GetFullPath(workingDocumentPath);
        EnsureDirectory(fullPath);
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Stopwatch save = Stopwatch.StartNew();
            tx.Save(temporaryPath, StreamType.InternalUnicodeFormat);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            save.Stop();
            _commandSaveMilliseconds += save.ElapsedMilliseconds;
            CaptureLoadedDocument(fullPath, _loadedWithTemplateSettings);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void CaptureLoadedDocument(string workingDocumentPath, bool templateSettings)
    {
        if (_persistentControl is null)
        {
            return;
        }

        var file = new FileInfo(workingDocumentPath);
        _loadedDocumentPath = file.FullName;
        _loadedDocumentLength = file.Length;
        _loadedDocumentWriteUtc = file.LastWriteTimeUtc;
        _loadedWithTemplateSettings = templateSettings;
    }

    private void ClearLoadedDocument()
    {
        _loadedDocumentPath = null;
        _loadedDocumentLength = 0;
        _loadedDocumentWriteUtc = default;
        _loadedWithTemplateSettings = false;
    }

    internal void BeginCommandTiming()
    {
        _commandLoadMilliseconds = 0;
        _commandSaveMilliseconds = 0;
        _commandCacheHit = null;
    }

    internal EngineCommandTiming CompleteCommandTiming() => new(
        _commandLoadMilliseconds,
        _commandSaveMilliseconds,
        _commandCacheHit);

    internal void UnloadActiveDocument()
    {
        if (_persistentControl is not null)
        {
            _persistentControl.Create();
            ClearLoadedDocument();
        }
    }

    internal sealed record EngineCommandTiming(long LoadMilliseconds, long SaveMilliseconds, bool? CacheHit);

    public void Dispose() => _persistentControl?.DisposePermanently();

    private sealed class PersistentServerTextControl : ServerTextControl
    {
        private bool disposePermanently;

        public void DisposePermanently()
        {
            disposePermanently = true;
            Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposePermanently)
            {
                base.Dispose(disposing);
            }
        }
    }
}
