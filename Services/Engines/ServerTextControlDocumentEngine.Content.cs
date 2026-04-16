using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Simplified TX Text Control document engine.
/// Content-oriented operations.
/// </summary>
public sealed partial class ServerTextControlDocumentEngine
{
    /// <summary>
    /// Format either a character range (start/length) or an entire paragraph (paragraphIndex).
    /// </summary>
    public DocumentState FormatText(string workingDocumentPath, FormatTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var hasRange = request.Start.HasValue || request.Length.HasValue;
        var hasParagraph = request.ParagraphIndex.HasValue;

        if (hasRange && hasParagraph)
        {
            throw new ArgumentException("Specify either start/length or paragraphIndex, not both.", nameof(request));
        }

        if (!hasRange && !hasParagraph)
        {
            throw new ArgumentException("Specify either start/length or paragraphIndex.", nameof(request));
        }

        if (hasRange && (!request.Start.HasValue || !request.Length.HasValue))
        {
            throw new ArgumentException("Both start and length are required when formatting a range.", nameof(request));
        }

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat);

            Selection selection;

            if (hasParagraph)
            {
                var paragraphIndex = request.ParagraphIndex!.Value;
                if (paragraphIndex < 0)
                {
                    throw new ArgumentException("paragraphIndex must be >= 0.", nameof(request));
                }

                // TX Text Control paragraphs are index-based (1-based collection index).
                var collectionIndex = paragraphIndex + 1;
                if (collectionIndex < 1 || collectionIndex > tx.Paragraphs.Count)
                {
                    throw new ArgumentException("paragraphIndex is out of range.", nameof(request));
                }

                var targetParagraph = tx.Paragraphs[collectionIndex];
                targetParagraph.Select();
                selection = tx.Selection;
            }
            else
            {
                var start = request.Start!.Value;
                var length = request.Length!.Value;

                if (start < 0 || length < 0)
                {
                    throw new ArgumentException("start and length must be >= 0.", nameof(request));
                }

                selection = new Selection(start, length);
            }

            selection.Bold = request.Bold;
            selection.Italic = request.Italic;
            selection.Underline = request.Underline ? FontUnderlineStyle.Single : FontUnderlineStyle.None;

            if (!string.IsNullOrWhiteSpace(request.ColorHex))
            {
                selection.ForeColor = ParseHexColor(request.ColorHex);
            }

            if (!string.IsNullOrWhiteSpace(request.FontName))
            {
                selection.FontName = request.FontName;
            }

            if (request.FontSize.HasValue)
            {
                if (request.FontSize.Value <= 0)
                {
                    throw new ArgumentException("font_size must be > 0.", nameof(request));
                }

                // TX Text Control uses twips (1 pt = 20 twips).
                selection.FontSize = (int)Math.Round(request.FontSize.Value * 20f);
            }

            tx.Selection = selection;
            tx.Save(workingDocumentPath, StreamType.InternalUnicodeFormat);
        }

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath
        };
    }

    /// <summary>
    /// Extract paragraphs from the current document as InternalUnicodeFormat, with optional slicing.
    /// </summary>
    public IReadOnlyList<string> GetParagraphs(string workingDocumentPath, int? start = null, int? end = null)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        var texts = new List<string>();

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat);

            foreach (Paragraph paragraph in tx.Paragraphs)
            {
                texts.Add(paragraph.Text ?? string.Empty);
            }
        }

        var s = start ?? 0;
        var e = end ?? texts.Count;

        s = Math.Max(0, Math.Min(s, texts.Count));
        e = Math.Max(s, Math.Min(e, texts.Count));

        return texts.GetRange(s, e - s);
    }

    /// <summary>
    /// Search for the specified text in the document's paragraphs and return matching paragraph indices.
    /// </summary>
    public IReadOnlyList<int> SearchText(string workingDocumentPath, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        var matches = new List<int>();

        var query = text ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            return matches;
        }

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat);

            var index = 0;
            foreach (Paragraph paragraph in tx.Paragraphs)
            {
                var paragraphText = paragraph.Text ?? string.Empty;

                var a = NormalizeTextForMatch(paragraphText);
                var b = NormalizeTextForMatch(query);

                if (matchCase)
                {
                    a = paragraphText;
                    b = query;
                }

                if (wholeWord)
                {
                    var words = (a ?? string.Empty)
                        .Replace("\r", " ")
                        .Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

                    if (words.Contains(b))
                    {
                        matches.Add(index);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(a) && a.Contains(b, StringComparison.Ordinal))
                    {
                        matches.Add(index);
                    }
                }

                index++;
            }
        }

        return matches;
    }

    /// <summary>
    /// Search text and return exact (start, length) ranges using ServerTextControl.Find.
    /// </summary>
    public IReadOnlyList<SearchTextRange> SearchTextRanges(string workingDocumentPath, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        var ranges = new List<SearchTextRange>();
        var query = text ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            return ranges;
        }

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat);

            var options = (FindOptions)0;
            if (matchCase)
            {
                options |= FindOptions.MatchCase;
            }

            if (wholeWord)
            {
                options |= FindOptions.MatchWholeWord;
            }

            var start = 0;
            while (true)
            {
                var found = tx.Find(query, start, options);
                if (found < 0)
                {
                    break;
                }

                ranges.Add(new SearchTextRange
                {
                    Start = found,
                    Length = query.Length
                });

                start = found + Math.Max(1, query.Length);
            }
        }

        return ranges;
    }

    /// <summary>
    /// Extract the full text of the document as InternalUnicodeFormat.
    /// </summary>
    public string GetText(string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat);
            return tx.Text ?? string.Empty;
        }
    }

    private static string NormalizeTextForMatch(string value)
        => (value ?? string.Empty).ToLowerInvariant();

    private static Color ParseHexColor(string value)
    {
        var hex = value.Trim();
        if (!hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = "#" + hex;
        }

        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("color_hex must be a valid hex color (for example: #FF0000).", nameof(value), ex);
        }
    }
}
