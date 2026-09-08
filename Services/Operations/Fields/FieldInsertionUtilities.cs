using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal enum FieldInsertionTargetKind { DocumentEnd, TextPosition, Range, Match, Paragraph, TableCell, HeaderFooter }
internal enum FieldInsertionPlacement { End, Start, Replace }

internal sealed record FieldInsertionTarget(
    FieldInsertionTargetKind Kind,
    FieldInsertionPlacement Placement,
    int? Start = null,
    int? Length = null,
    string? ExpectedText = null,
    string? MatchText = null,
    int? OccurrenceIndex = null,
    int? NearTextPosition = null,
    bool ReplaceAll = false,
    bool MatchCase = false,
    bool WholeWord = false,
    int? TextPosition = null,
    int? ParagraphIndex = null,
    int? TableId = null,
    int? RowIndex = null,
    int? ColumnIndex = null,
    HeaderFooterType? HeaderFooterType = null,
    int SectionIndex = 0);

internal readonly record struct FieldInsertionRange(int Start, int Length, string? ExpectedText = null);
internal readonly record struct FieldContainer(IFormattedText Content, string Location);

internal static class FieldInsertionUtilities
{
    private static readonly HeaderFooterType[] HeaderFooterTypes =
    [
        HeaderFooterType.Header,
        HeaderFooterType.Footer,
        HeaderFooterType.FirstPageHeader,
        HeaderFooterType.FirstPageFooter,
        HeaderFooterType.EvenHeader,
        HeaderFooterType.EvenFooter
    ];

    public static IEnumerable<FieldContainer> EnumerateFieldContainers(ServerTextControl tx)
    {
        yield return new FieldContainer(tx, "body");
        foreach (HeaderFooterType type in HeaderFooterTypes)
        {
            HeaderFooter? headerFooter = tx.HeadersAndFooters.GetItem(type);
            if (headerFooter is not null)
            {
                yield return new FieldContainer(headerFooter, $"sections[0].{ToModelHeaderFooterName(type)}");
            }
        }
    }

    public static FieldInsertionTarget Resolve(DocumentOperation operation, bool allowMatch, bool allowHeaderFooter)
    {
        bool hasTable = !string.IsNullOrWhiteSpace(operation.TableId) || operation.RowIndex.HasValue || operation.ColumnIndex.HasValue;
        bool hasHeaderFooter = !string.IsNullOrWhiteSpace(operation.HeaderFooterType);
        bool hasMatch = !string.IsNullOrWhiteSpace(operation.MatchText);
        bool hasRange = operation.Start.HasValue || operation.Length.HasValue;
        bool hasTextPosition = operation.TextPosition.HasValue;
        bool hasParagraph = operation.ParagraphIndex.HasValue;
        int targetCount = (hasTable ? 1 : 0) + (hasHeaderFooter ? 1 : 0) + (hasMatch ? 1 : 0)
            + (hasRange ? 1 : 0) + (hasParagraph ? 1 : 0) + (hasTextPosition && !hasTable && !hasHeaderFooter ? 1 : 0);
        if (targetCount > 1)
        {
            throw new ArgumentException("Specify only one field target: matchText, start/length, textPosition, paragraphIndex, table cell, or headerFooterType.");
        }

        FieldInsertionPlacement placement = ResolvePlacement(operation.Placement);
        if (hasRange)
        {
            if (!operation.Start.HasValue || !operation.Length.HasValue)
            {
                throw new ArgumentException("Both start and length are required for a character range.");
            }

            if (operation.Length.Value > 0 && operation.ExpectedText is null)
            {
                throw new ArgumentException("expectedText is required when a character range replaces existing text.");
            }

            if (placement is not FieldInsertionPlacement.End and not FieldInsertionPlacement.Replace)
            {
                throw new ArgumentException("A character range is replaced; placement may be omitted or set to replace.");
            }

            return new FieldInsertionTarget(FieldInsertionTargetKind.Range, FieldInsertionPlacement.Replace,
                Start: operation.Start, Length: operation.Length, ExpectedText: operation.ExpectedText);
        }

        if (operation.ExpectedText is not null)
        {
            throw new ArgumentException("expectedText can only be used with a start/length character range.");
        }

        if (hasMatch)
        {
            if (!allowMatch)
            {
                throw new ArgumentException("matchText is not supported for this field type.");
            }

            if (operation.ReplaceAll && (operation.OccurrenceIndex.HasValue || operation.NearTextPosition.HasValue))
            {
                throw new ArgumentException("Do not combine replaceAll with occurrenceIndex or nearTextPosition.");
            }

            if (operation.OccurrenceIndex.HasValue && operation.NearTextPosition.HasValue)
            {
                throw new ArgumentException("Use either occurrenceIndex or nearTextPosition, not both.");
            }

            if (operation.NearTextPosition < 0)
            {
                throw new ArgumentException("nearTextPosition must be non-negative.");
            }

            if (placement is not FieldInsertionPlacement.End and not FieldInsertionPlacement.Replace)
            {
                throw new ArgumentException("A text match is replaced; placement may be omitted or set to replace.");
            }

            return new FieldInsertionTarget(FieldInsertionTargetKind.Match, FieldInsertionPlacement.Replace,
                MatchText: operation.MatchText!, OccurrenceIndex: operation.OccurrenceIndex,
                NearTextPosition: operation.NearTextPosition,
                ReplaceAll: operation.ReplaceAll, MatchCase: operation.MatchCase, WholeWord: operation.WholeWord);
        }

        if (operation.ReplaceAll || operation.OccurrenceIndex.HasValue || operation.NearTextPosition.HasValue)
        {
            throw new ArgumentException("replaceAll, occurrenceIndex, and nearTextPosition require matchText.");
        }

        if (hasTable)
        {
            if (operation.TextPosition.HasValue && placement != FieldInsertionPlacement.End)
            {
                throw new ArgumentException("Do not combine a table-cell textPosition with placement; textPosition is already exact.");
            }
            return new FieldInsertionTarget(FieldInsertionTargetKind.TableCell, placement,
                TextPosition: operation.TextPosition,
                TableId: TableOperationUtilities.RequireTableId(operation.TableId),
                RowIndex: TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex)),
                ColumnIndex: TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex)));
        }

        if (hasHeaderFooter)
        {
            if (!allowHeaderFooter)
            {
                throw new ArgumentException("headerFooterType is not supported for this field type.");
            }

            int sectionIndex = operation.SectionIndex ?? 0;
            if (sectionIndex != 0)
            {
                throw new NotSupportedException("Only sectionIndex 0 is currently supported for field insertion.");
            }

            if (operation.TextPosition.HasValue && placement != FieldInsertionPlacement.End)
            {
                throw new ArgumentException("Do not combine a header/footer textPosition with placement; textPosition is already exact.");
            }

            return new FieldInsertionTarget(FieldInsertionTargetKind.HeaderFooter, placement,
                TextPosition: operation.TextPosition, HeaderFooterType: ResolveHeaderFooterType(operation.HeaderFooterType),
                SectionIndex: sectionIndex);
        }

        if (operation.SectionIndex.HasValue)
        {
            throw new ArgumentException("sectionIndex requires headerFooterType.");
        }

        if (hasParagraph)
        {
            return new FieldInsertionTarget(FieldInsertionTargetKind.Paragraph, placement, ParagraphIndex: operation.ParagraphIndex);
        }

        if (hasTextPosition)
        {
            if (placement is not FieldInsertionPlacement.End)
            {
                throw new ArgumentException("placement is not used with an absolute textPosition.");
            }

            return new FieldInsertionTarget(FieldInsertionTargetKind.TextPosition, FieldInsertionPlacement.End,
                TextPosition: operation.TextPosition);
        }

        if (placement != FieldInsertionPlacement.End)
        {
            throw new ArgumentException("placement requires a paragraph, table-cell, or header/footer target.");
        }

        return new FieldInsertionTarget(FieldInsertionTargetKind.DocumentEnd, placement);
    }

    public static IReadOnlyList<FieldInsertionRange> ResolveBodyRanges(ServerTextControl tx, FieldInsertionTarget target)
    {
        int textLength = tx.TextChars.Count;
        switch (target.Kind)
        {
            case FieldInsertionTargetKind.DocumentEnd:
                return [new FieldInsertionRange(textLength, 0)];
            case FieldInsertionTargetKind.TextPosition:
                ValidatePosition(target.TextPosition!.Value, textLength, nameof(target.TextPosition));
                return [new FieldInsertionRange(target.TextPosition.Value, 0)];
            case FieldInsertionTargetKind.Range:
                ValidateRange(tx, target.Start!.Value, target.Length!.Value, target.ExpectedText);
                return [new FieldInsertionRange(target.Start.Value, target.Length.Value, target.ExpectedText)];
            case FieldInsertionTargetKind.Match:
                List<TextOccurrence> matches = TextOccurrenceUtilities.FindOccurrences(tx, target.MatchText!, target.MatchCase, target.WholeWord, null);
                if (matches.Count == 0)
                {
                    throw new ArgumentException($"No text matched '{target.MatchText}'. Inspect the document and retry with exact text.");
                }

                if (target.ReplaceAll)
                {
                    return matches.Select(match => new FieldInsertionRange(match.Start, match.Length, target.MatchText)).ToArray();
                }

                if (target.NearTextPosition.HasValue)
                {
                    TextOccurrence nearest = matches
                        .OrderBy(match => Math.Abs((long)match.Start - target.NearTextPosition.Value))
                        .ThenBy(match => match.Start)
                        .First();
                    return [new FieldInsertionRange(nearest.Start, nearest.Length, target.MatchText)];
                }

                int occurrenceIndex = target.OccurrenceIndex ?? 0;
                if (occurrenceIndex < 0 || occurrenceIndex >= matches.Count)
                {
                    throw new ArgumentException($"occurrenceIndex is out of range. Found {matches.Count} occurrence(s), indexed from 0.");
                }

                TextOccurrence selected = matches[occurrenceIndex];
                return [new FieldInsertionRange(selected.Start, selected.Length, target.MatchText)];
            case FieldInsertionTargetKind.Paragraph:
                return [ResolveParagraphRange(tx, target.ParagraphIndex!.Value, target.Placement)];
            case FieldInsertionTargetKind.TableCell:
                return [ResolveTableCellRange(tx, target)];
            default:
                throw new InvalidOperationException("The target is not in the main document text.");
        }
    }

    public static void PrepareSelection(ServerTextControl tx, FieldInsertionRange range)
    {
        ValidateRange(tx, range.Start, range.Length, range.ExpectedText);
        if (range.Length > 0)
        {
            tx.Selection = new Selection(range.Start, range.Length) { Text = string.Empty };
        }

        tx.Selection = new Selection(range.Start, 0);
    }

    public static HeaderFooter GetOrCreateHeaderFooter(ServerTextControl tx, HeaderFooterType type)
    {
        HeaderFooter? headerFooter = tx.HeadersAndFooters.GetItem(type);
        if (headerFooter is not null)
        {
            return headerFooter;
        }

        if (!tx.HeadersAndFooters.Add(type))
        {
            throw new InvalidOperationException($"TX Text Control could not add {type}.");
        }

        return tx.HeadersAndFooters.GetItem(type)
            ?? throw new InvalidOperationException($"TX Text Control added {type}, but it could not be found.");
    }

    public static void PrepareHeaderFooterSelection(HeaderFooter headerFooter, FieldInsertionTarget target)
    {
        int textLength = headerFooter.TextChars.Count;
        int start = target.TextPosition ?? (target.Placement == FieldInsertionPlacement.End ? textLength : 0);
        ValidatePosition(start, textLength, nameof(target.TextPosition));
        int length = target.TextPosition.HasValue || target.Placement != FieldInsertionPlacement.Replace ? 0 : textLength;
        headerFooter.Selection = new Selection(start, length);
        if (length > 0)
        {
            headerFooter.Selection.Text = string.Empty;
            headerFooter.Selection = new Selection(start, 0);
        }
    }

    public static string DescribeLocation(FieldInsertionTarget target)
        => target.Kind switch
        {
            FieldInsertionTargetKind.DocumentEnd => "body.end",
            FieldInsertionTargetKind.TextPosition => $"body.text[{target.TextPosition}]",
            FieldInsertionTargetKind.Range => $"body.range[{target.Start},{target.Length}]",
            FieldInsertionTargetKind.Match => target.ReplaceAll ? $"body.matches[{target.MatchText}]" : $"body.matches[{target.MatchText}][{target.OccurrenceIndex ?? 0}]",
            FieldInsertionTargetKind.Paragraph => $"paragraphs[{target.ParagraphIndex}].{target.Placement.ToString().ToLowerInvariant()}",
            FieldInsertionTargetKind.TableCell => $"tables['{target.TableId}'].rows[{target.RowIndex}].cells[{target.ColumnIndex}]",
            FieldInsertionTargetKind.HeaderFooter => $"sections[{target.SectionIndex}].{ToModelHeaderFooterName(target.HeaderFooterType!.Value)}",
            _ => "document"
        };

    public static string ToModelHeaderFooterName(HeaderFooterType type)
        => type switch
        {
            HeaderFooterType.Header => "header", HeaderFooterType.Footer => "footer",
            HeaderFooterType.FirstPageHeader => "firstPageHeader", HeaderFooterType.FirstPageFooter => "firstPageFooter",
            HeaderFooterType.EvenHeader => "evenHeader", HeaderFooterType.EvenFooter => "evenFooter", _ => type.ToString()
        };

    private static FieldInsertionPlacement ResolvePlacement(string? placement)
        => string.IsNullOrWhiteSpace(placement) ? FieldInsertionPlacement.End : placement.Trim().ToLowerInvariant() switch
        {
            "end" => FieldInsertionPlacement.End, "start" => FieldInsertionPlacement.Start,
            "replace" => FieldInsertionPlacement.Replace,
            _ => throw new ArgumentException("placement must be 'end', 'start', or 'replace'.")
        };

    private static FieldInsertionRange ResolveParagraphRange(ServerTextControl tx, int paragraphIndex, FieldInsertionPlacement placement)
    {
        if (paragraphIndex < 0 || paragraphIndex >= tx.Paragraphs.Count)
        {
            throw new ArgumentException($"paragraphIndex is out of range. The document contains {tx.Paragraphs.Count} paragraph(s), indexed from 0.");
        }

        tx.Paragraphs[paragraphIndex + 1].Select();
        int start = tx.Selection.Start;
        int contentLength = TrimTrailingParagraphMarks(tx.Selection.Text ?? string.Empty);
        return placement switch
        {
            FieldInsertionPlacement.Start => new FieldInsertionRange(start, 0),
            FieldInsertionPlacement.End => new FieldInsertionRange(start + contentLength, 0),
            FieldInsertionPlacement.Replace => new FieldInsertionRange(start, contentLength),
            _ => throw new InvalidOperationException("Unsupported paragraph placement.")
        };
    }

    private static FieldInsertionRange ResolveTableCellRange(ServerTextControl tx, FieldInsertionTarget target)
    {
        TableCell cell = TableOperationUtilities.GetTxCell(
            TableOperationUtilities.GetTxTable(tx, target.TableId!.Value), target.RowIndex!.Value, target.ColumnIndex!.Value);
        string text = cell.Text ?? string.Empty;
        int offset = target.TextPosition ?? (target.Placement == FieldInsertionPlacement.End ? text.Length : 0);
        ValidatePosition(offset, text.Length, nameof(target.TextPosition));
        int length = target.TextPosition.HasValue || target.Placement != FieldInsertionPlacement.Replace ? 0 : text.Length;
        return new FieldInsertionRange(Math.Max(0, cell.Start - 1 + offset), length);
    }

    private static void ValidateRange(ServerTextControl tx, int start, int length, string? expectedText)
    {
        int textLength = tx.TextChars.Count;
        if (start < 0 || length < 0 || start + length > textLength)
        {
            throw new ArgumentException($"Character range is outside the TX text-position length of {textLength}.");
        }

        if (expectedText is not null)
        {
            tx.Selection = new Selection(start, length);
            if (!string.Equals(tx.Selection.Text ?? string.Empty, expectedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The character range no longer contains expectedText. Inspect the current document with MCP and retry with fresh server coordinates.");
            }
        }
    }

    private static void ValidatePosition(int position, int textLength, string name)
    {
        if (position < 0 || position > textLength)
        {
            throw new ArgumentException($"{name} must be between 0 and the text length of {textLength}.");
        }
    }

    private static int TrimTrailingParagraphMarks(string text)
    {
        int length = text.Length;
        while (length > 0 && text[length - 1] is '\r' or '\n') length--;
        return length;
    }

    private static HeaderFooterType ResolveHeaderFooterType(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "header" => HeaderFooterType.Header, "footer" => HeaderFooterType.Footer,
            "firstpageheader" or "first_page_header" or "first-page-header" => HeaderFooterType.FirstPageHeader,
            "firstpagefooter" or "first_page_footer" or "first-page-footer" => HeaderFooterType.FirstPageFooter,
            "evenheader" or "even_header" or "even-header" => HeaderFooterType.EvenHeader,
            "evenfooter" or "even_footer" or "even-footer" => HeaderFooterType.EvenFooter,
            _ => throw new ArgumentException("headerFooterType must be one of: header, footer, firstPageHeader, firstPageFooter, evenHeader, evenFooter.")
        };
}
