using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TXTextControl;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Services.Operations;

internal static class ParagraphTargetUtilities
{
    public static IEnumerable<DocumentModel.Paragraph> EnumerateModelParagraphs(
        DocumentModel.Document document)
    {
        foreach (DocumentModel.Section section in document.Sections)
        {
            foreach (DocumentModel.Paragraph paragraph in EnumerateBlocks(section.Blocks))
            {
                yield return paragraph;
            }
        }
    }

    public static List<int> ResolveTargetIndexes(
        ServerTextControl tx,
        DocumentOperation operation,
        bool allowImplicitAll)
    {
        bool hasParagraphRange = operation.StartParagraphIndex.HasValue || operation.EndParagraphIndex.HasValue;
        int selectorCount = (operation.ParagraphIndex.HasValue ? 1 : 0)
                            + (hasParagraphRange ? 1 : 0)
                            + (!string.IsNullOrEmpty(operation.MatchText) ? 1 : 0)
                            + (operation.AllParagraphs ? 1 : 0);
        if (selectorCount == 0)
        {
            if (allowImplicitAll)
            {
                return Enumerable.Range(0, tx.Paragraphs.Count).ToList();
            }

            throw new ArgumentException(
                "Specify exactly one paragraph target: paragraphIndex, startParagraphIndex/endParagraphIndex, matchText, or allParagraphs=true.");
        }

        if (selectorCount != 1)
        {
            throw new ArgumentException(
                "Specify exactly one paragraph target: paragraphIndex, startParagraphIndex/endParagraphIndex, matchText, or allParagraphs=true.");
        }

        if (operation.ParagraphIndex.HasValue)
        {
            ValidateParagraphIndex(tx, operation.ParagraphIndex.Value);
            return [operation.ParagraphIndex.Value];
        }

        if (hasParagraphRange)
        {
            if (!operation.StartParagraphIndex.HasValue)
            {
                throw new ArgumentException("startParagraphIndex is required for a paragraph range.");
            }

            int startIndex = operation.StartParagraphIndex.Value;
            int endIndex = operation.EndParagraphIndex ?? startIndex;
            ValidateParagraphIndex(tx, startIndex);
            ValidateParagraphIndex(tx, endIndex);
            if (endIndex < startIndex)
            {
                throw new ArgumentException("endParagraphIndex must be greater than or equal to startParagraphIndex.");
            }

            return Enumerable.Range(startIndex, endIndex - startIndex + 1).ToList();
        }

        if (operation.AllParagraphs)
        {
            return Enumerable.Range(0, tx.Paragraphs.Count).ToList();
        }

        string searchText = operation.MatchText!;
        List<TextOccurrence> occurrences = TextOccurrenceUtilities.FindOccurrences(
            tx,
            searchText,
            operation.MatchCase,
            operation.WholeWord,
            null);
        if (occurrences.Count == 0)
        {
            string trimmedSearchText = searchText.Trim();
            if (trimmedSearchText.Length > 0
                && !trimmedSearchText.Equals(searchText, StringComparison.Ordinal))
            {
                occurrences = TextOccurrenceUtilities.FindOccurrences(
                    tx,
                    trimmedSearchText,
                    operation.MatchCase,
                    operation.WholeWord,
                    null);
            }
        }

        if (occurrences.Count == 0)
        {
            throw new ArgumentException(
                $"No text matched '{operation.MatchText}'. Inspect the current document and retry with exact text.");
        }

        IEnumerable<TextOccurrence> selectedOccurrences;
        if (operation.ReplaceAll)
        {
            selectedOccurrences = occurrences;
        }
        else if (operation.OccurrenceIndex.HasValue)
        {
            int occurrenceIndex = operation.OccurrenceIndex.Value;
            if (occurrenceIndex < 0 || occurrenceIndex >= occurrences.Count)
            {
                throw new ArgumentException(
                    $"occurrenceIndex is out of range. Found {occurrences.Count} occurrence(s), indexed from 0.");
            }

            selectedOccurrences = [occurrences[occurrenceIndex]];
        }
        else if (operation.NearTextPosition.HasValue)
        {
            int hint = operation.NearTextPosition.Value;
            if (hint < 0)
            {
                throw new ArgumentException("nearTextPosition must be greater than or equal to 0.");
            }

            selectedOccurrences =
            [
                occurrences
                    .OrderBy(occurrence => Math.Abs((long)occurrence.Start - hint))
                    .ThenBy(occurrence => occurrence.Start)
                    .First()
            ];
        }
        else if (occurrences.Count == 1)
        {
            selectedOccurrences = [occurrences[0]];
        }
        else
        {
            throw new ArgumentException(
                $"Text '{operation.MatchText}' occurs {occurrences.Count} times. Set occurrenceIndex, " +
                "nearTextPosition, or allMatches=true to choose the target paragraph(s).");
        }

        return selectedOccurrences
            .Select(occurrence => FindContainingParagraphIndex(tx, occurrence.Start))
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private static int FindContainingParagraphIndex(ServerTextControl tx, int zeroBasedTextPosition)
    {
        for (int collectionIndex = 1; collectionIndex <= tx.Paragraphs.Count; collectionIndex++)
        {
            Paragraph paragraph = tx.Paragraphs[collectionIndex];
            int paragraphStart = paragraph.Start - 1;
            int paragraphEndExclusive = paragraphStart + paragraph.Length;
            if (zeroBasedTextPosition >= paragraphStart && zeroBasedTextPosition < paragraphEndExclusive)
            {
                return collectionIndex - 1;
            }
        }

        throw new InvalidOperationException(
            $"The matched TX text position {zeroBasedTextPosition} is not contained in a paragraph.");
    }

    private static void ValidateParagraphIndex(ServerTextControl tx, int paragraphIndex)
    {
        if (paragraphIndex < 0 || paragraphIndex >= tx.Paragraphs.Count)
        {
            throw new ArgumentException(
                $"paragraphIndex is out of range. The document contains {tx.Paragraphs.Count} paragraph(s), indexed from 0.");
        }
    }

    private static IEnumerable<DocumentModel.Paragraph> EnumerateBlocks(
        IEnumerable<DocumentModel.DocumentBlock> blocks)
    {
        foreach (DocumentModel.DocumentBlock block in blocks)
        {
            if (block.Paragraph is not null)
            {
                yield return block.Paragraph;
            }

            if (block.Table is null)
            {
                continue;
            }

            foreach (DocumentModel.TableRow row in block.Table.Rows)
            {
                foreach (DocumentModel.TableCell cell in row.Cells)
                {
                    foreach (DocumentModel.Paragraph paragraph in EnumerateBlocks(cell.Blocks))
                    {
                        yield return paragraph;
                    }
                }
            }
        }
    }
}
