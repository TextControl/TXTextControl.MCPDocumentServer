using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TxTextControl.McpServer.Models;
using TXTextControl;
using TXTextControl.Markdown;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Simplified TX Text Control document engine.
/// Document-oriented operations.
/// </summary>
public sealed partial class ServerTextControlDocumentEngine : ITxDocumentEngine
{
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

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Save(workingDocumentPath, StreamType.InternalUnicodeFormat);
        }

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath
        };
    }

    public DocumentState LoadFromBase64(string base64Document, string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(base64Document))
        {
            throw new ArgumentException("Base64 document content is required.", nameof(base64Document));
        }

        EnsureDirectory(workingDocumentPath);

        var payload = base64Document.Trim();
        var markerIndex = payload.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            payload = payload[(markerIndex + "base64,".Length)..];
        }

        byte[] documentBytes;
        try
        {
            documentBytes = Convert.FromBase64String(payload);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The provided content is not valid base64.", ex);
        }

        using (var tx = new ServerTextControl())
        {
            tx.Create();

            // Edge case: markdown payload (base64 plain text markdown)
            var utf8Text = TryGetUtf8Text(documentBytes);
            if (!string.IsNullOrWhiteSpace(utf8Text) && IsLikelyMarkdown(utf8Text))
            {
                try
                {
                    tx.LoadMarkdown(utf8Text);
                    tx.Save(workingDocumentPath, StreamType.InternalUnicodeFormat);

                    return new DocumentState
                    {
                        WorkingDocumentPath = workingDocumentPath
                    };
                }
                catch
                {
                    // Fall back to generic format detection below.
                }
            }

            var loadSettings = new LoadSettings();
            var guessed = GuessFormat(documentBytes, out var excludeHtml);

            if (!TryLoad(tx, loadSettings, documentBytes, guessed, excludeHtml))
            {
                throw new InvalidOperationException("The document format could not be loaded.");
            }

            tx.Save(workingDocumentPath, StreamType.InternalUnicodeFormat);
        }

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath
        };
    }

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

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat);

            return normalizedFormat switch
            {
                "tx" => SaveBinaryAsBase64(tx, BinaryStreamType.InternalUnicodeFormat),
                "docx" => SaveBinaryAsBase64(tx, BinaryStreamType.WordprocessingML),
                "pdf" => SaveBinaryAsBase64(tx, BinaryStreamType.AdobePDF),
                "html" => SaveStringAsBase64(tx, StringStreamType.HTMLFormat),
                "md" => SaveMarkdownAsBase64(tx),
                _ => throw new ArgumentException("Unsupported format. Use one of: tx, docx, pdf, html, md.", nameof(format))
            };
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

    private static DocType GuessFormat(byte[] data, out bool excludeHtml)
    {
        excludeHtml = false;

        foreach (var hint in HintFormats)
        {
            var signature = hint.Value;
            if (data.Length < signature.Length)
            {
                continue;
            }

            var test = data.Take(signature.Length).ToArray();
            if (test.SequenceEqual(signature))
            {
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
}
