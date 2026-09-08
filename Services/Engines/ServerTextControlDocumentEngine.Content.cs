using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services.Operations;
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

        if (!request.Bold.HasValue
            && !request.Italic.HasValue
            && !request.Underline.HasValue
            && string.IsNullOrWhiteSpace(request.ColorHex)
            && string.IsNullOrWhiteSpace(request.FontName)
            && !request.FontSize.HasValue)
        {
            throw new ArgumentException(
                "At least one character formatting property is required: bold, italic, underline, color_hex, font_name, or font_size. Use format_paragraph for alignment and paragraph spacing.",
                nameof(request));
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

        using (var tx = CreateServerTextControl())
        {
            LoadWorkingDocument(tx, workingDocumentPath);

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

                if (start + length > tx.TextChars.Count)
                {
                    throw new ArgumentException(
                        $"The character range is outside the TX text-position length of {tx.TextChars.Count}.",
                        nameof(request));
                }

                // Attach the range before mutating its formatting. A detached Selection can
                // otherwise apply properties at the current input position instead.
                tx.Selection = new Selection(start, length);
                selection = tx.Selection;
            }

            if (request.Bold.HasValue)
            {
                selection.Bold = request.Bold.Value;
            }

            if (request.Italic.HasValue)
            {
                selection.Italic = request.Italic.Value;
            }

            if (request.Underline.HasValue)
            {
                selection.Underline = request.Underline.Value ? FontUnderlineStyle.Single : FontUnderlineStyle.None;
            }

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
            SaveWorkingDocument(tx, workingDocumentPath);
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

        using (var tx = CreateServerTextControl())
        {
            LoadWorkingDocument(tx, workingDocumentPath);

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

        using (var tx = CreateServerTextControl())
        {
            LoadWorkingDocument(tx, workingDocumentPath);

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

        using (var tx = CreateServerTextControl())
        {
            LoadWorkingDocument(tx, workingDocumentPath);

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

        using (var tx = CreateServerTextControl())
        {
            LoadWorkingDocument(tx, workingDocumentPath);
            return tx.Text ?? string.Empty;
        }
    }

    public DocumentEditEngineResult EditDocument(string workingDocumentPath, EditDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        ArgumentNullException.ThrowIfNull(request);
        int selectorCount = 0;
        bool usesMatch = !string.IsNullOrEmpty(request.MatchText);
        bool usesParagraph = request.ParagraphIndex.HasValue
                             || request.StartParagraphIndex.HasValue
                             || request.EndParagraphIndex.HasValue;
        bool usesRange = request.Start.HasValue || request.Length.HasValue;
        selectorCount += usesMatch ? 1 : 0;
        selectorCount += usesParagraph ? 1 : 0;
        selectorCount += usesRange ? 1 : 0;
        if (selectorCount != 1)
        {
            throw new ArgumentException(
                "Specify exactly one target: matchText, paragraphIndex/startParagraphIndex, or start plus length.",
                nameof(request));
        }

        if (request.ReplaceAll && request.OccurrenceIndex.HasValue)
        {
            throw new ArgumentException("Do not combine replaceAll with occurrenceIndex.", nameof(request));
        }

        if (request.ExpectedText is not null && !usesRange)
        {
            throw new ArgumentException(
                "expectedText can only be used with a start/length character range.",
                nameof(request));
        }

        using var tx = CreateServerTextControl();
        LoadWorkingDocument(tx, workingDocumentPath);

        var ranges = new List<SearchTextRange>();
        var paragraphIndexes = new List<int>();
        string targetKind;

        if (usesMatch)
        {
            List<TextOccurrence> occurrences = TextOccurrenceUtilities.FindOccurrences(
                tx,
                request.MatchText!,
                request.MatchCase,
                request.WholeWord,
                null);
            if (occurrences.Count == 0)
            {
                throw new ArgumentException($"No text matched '{request.MatchText}'. Inspect the document and retry with exact text.");
            }

            IEnumerable<TextOccurrence> selected;
            if (request.ReplaceAll)
            {
                selected = occurrences;
            }
            else
            {
                int occurrenceIndex = request.OccurrenceIndex ?? 0;
                if (occurrenceIndex < 0 || occurrenceIndex >= occurrences.Count)
                {
                    throw new ArgumentException(
                        $"occurrenceIndex is out of range. Found {occurrences.Count} occurrence(s), indexed from 0.");
                }

                selected = [occurrences[occurrenceIndex]];
            }

            List<TextOccurrence> selectedRanges = selected.ToList();
            foreach (TextOccurrence occurrence in selectedRanges.OrderByDescending(value => value.Start))
            {
                tx.Selection = new Selection(occurrence.Start, occurrence.Length)
                {
                    Text = NormalizeReplacementText(request.ReplacementText)
                };
            }

            ranges.AddRange(selectedRanges.Select(value => new SearchTextRange
            {
                Start = value.Start,
                Length = value.Length
            }));
            targetKind = "text";
        }
        else if (usesParagraph)
        {
            if (request.ParagraphIndex.HasValue
                && (request.StartParagraphIndex.HasValue || request.EndParagraphIndex.HasValue))
            {
                throw new ArgumentException(
                    "Use paragraphIndex for one paragraph or startParagraphIndex/endParagraphIndex for a paragraph range, not both.");
            }

            int startIndex = request.ParagraphIndex ?? request.StartParagraphIndex
                ?? throw new ArgumentException("startParagraphIndex is required for a paragraph range.");
            int endIndex = request.ParagraphIndex ?? request.EndParagraphIndex ?? startIndex;
            if (startIndex < 0 || endIndex < startIndex || endIndex >= tx.Paragraphs.Count)
            {
                throw new ArgumentException(
                    $"Paragraph range is invalid. The document contains {tx.Paragraphs.Count} paragraph(s), indexed from 0.");
            }

            Paragraph first = tx.Paragraphs[startIndex + 1];
            Paragraph last = tx.Paragraphs[endIndex + 1];
            first.Select();
            int selectionStart = tx.Selection.Start;
            last.Select();
            int selectionEnd = tx.Selection.Start + tx.Selection.Length;
            tx.Selection = new Selection(selectionStart, selectionEnd - selectionStart)
            {
                Text = NormalizeReplacementText(request.ReplacementText)
            };
            ranges.Add(new SearchTextRange
            {
                Start = selectionStart,
                Length = selectionEnd - selectionStart
            });
            paragraphIndexes.AddRange(Enumerable.Range(startIndex, endIndex - startIndex + 1));
            targetKind = startIndex == endIndex ? "paragraph" : "paragraphRange";
        }
        else
        {
            if (!request.Start.HasValue || !request.Length.HasValue)
            {
                throw new ArgumentException("Both start and length are required for a character range.");
            }

            int start = request.Start.Value;
            int length = request.Length.Value;
            int textLength = tx.TextChars.Count;
            if (start < 0 || length < 0 || start + length > textLength)
            {
                throw new ArgumentException($"Character range is outside the TX text-position length of {textLength}.");
            }

            if (request.ExpectedText is not null)
            {
                tx.Selection = new Selection(start, length);
                string currentText = tx.Selection.Text ?? string.Empty;
                if (!string.Equals(currentText, request.ExpectedText, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The character range no longer contains expectedText. Search the current document and retry with fresh TX coordinates.");
                }
            }

            tx.Selection = new Selection(start, length)
            {
                Text = NormalizeReplacementText(request.ReplacementText)
            };
            ranges.Add(new SearchTextRange { Start = start, Length = length });
            targetKind = "range";
        }

        SaveWorkingDocument(tx, workingDocumentPath);
        DocumentContentSnapshot snapshot = CreateContentSnapshot(tx);
        return new DocumentEditEngineResult(
            new DocumentState
            {
                WorkingDocumentPath = workingDocumentPath,
                ContentSnapshot = snapshot
            },
            targetKind,
            ranges.Count,
            ranges,
            paragraphIndexes);
    }

    public DocumentContentSnapshot GetContentSnapshot(string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        using var tx = CreateServerTextControl();
        LoadWorkingDocument(tx, workingDocumentPath);
        return CreateContentSnapshot(tx);
    }

    private static DocumentContentSnapshot CreateContentSnapshot(ServerTextControl tx)
    {
        DocumentParagraphSnapshot[] details = tx.Paragraphs
            .Cast<Paragraph>()
            .Select(paragraph => new DocumentParagraphSnapshot(
                TrimParagraphTerminator(paragraph.Text),
                string.IsNullOrWhiteSpace(paragraph.FormattingStyle) ? null : paragraph.FormattingStyle))
            .ToArray();
        DocumentTableSnapshot[] tables = tx.Tables
            .Cast<Table>()
            .Select(table => new DocumentTableSnapshot(
                table.ID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                table.Rows.Count,
                table.Columns.Count,
                Enumerable.Range(0, table.Rows.Count)
                    .Select(rowIndex => new DocumentTableRowSnapshot(
                        rowIndex,
                        Enumerable.Range(0, table.Columns.Count)
                            .Select(columnIndex =>
                            {
                                TableCell cell = table.Cells.GetItem(rowIndex + 1, columnIndex + 1);
                                return new DocumentTableCellSnapshot(
                                    rowIndex,
                                    columnIndex,
                                    TrimParagraphTerminator(cell.Text),
                                    cell.Start - 1,
                                    cell.Length);
                            })
                            .ToArray()))
                    .ToArray()))
            .ToArray();
        return new DocumentContentSnapshot(
            tx.Text ?? string.Empty,
            details.Select(paragraph => paragraph.Text).ToArray())
        {
            ParagraphDetails = details,
            Tables = tables
        };
    }

    private static string TrimParagraphTerminator(string? value)
        => (value ?? string.Empty).TrimEnd('\r', '\n');

    private static string NormalizeTextForMatch(string value)
        => (value ?? string.Empty).ToLowerInvariant();

    private static string NormalizeReplacementText(string value)
        => (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal);

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
