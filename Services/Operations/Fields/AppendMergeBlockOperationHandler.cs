using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendMergeBlockOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.AppendMergeBlock;
    public string CapabilityPack => FieldsCapabilityPack.PackName;

    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.AppendMergeBlock,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Wraps an existing table row, character range, or paragraph range in a TX Text Control merge-block SubTextPart named txmb_<blockName>.",
        Intent = "Use for repeating MailMerge content such as invoice rows, repeated paragraphs, clauses, or address blocks. This creates a real SubTextPart, not marker text.",
        RequiredProperties = ["type", "blockName", "one target"],
        OptionalProperties = ["blockId", "tableId", "rowIndex", "start", "length", "expectedText", "startParagraphIndex", "endParagraphIndex"],
        Properties = new()
        {
            ["blockName"] = "Merge block name without the txmb_ prefix. The operation stores the SubTextPart as txmb_<blockName>.",
            ["blockId"] = "Optional integer SubTextPart id. If omitted, a positive id is generated.",
            ["tableId"] = "Target table id containing the repeating template row; requires rowIndex.",
            ["rowIndex"] = "Zero-based table row index to wrap.",
            ["start"] = "Zero-based body character start; requires length and expectedText.",
            ["length"] = "Non-zero character count to wrap.",
            ["expectedText"] = "Expected current range text; stale coordinates are rejected.",
            ["startParagraphIndex"] = "First zero-based paragraph to wrap; endParagraphIndex defaults to the same paragraph.",
            ["endParagraphIndex"] = "Last zero-based paragraph to wrap."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.AppendMergeBlock,
            ["blockName"] = "lineItems",
            ["tableId"] = "10",
            ["rowIndex"] = 1
        },
        ModelEffects = ["Marks a table row as a merge block in operation metadata. The physical TX document receives a txmb_ SubTextPart."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var blockName = RequireBlockName(operation.BlockName);
        var subTextPartName = ToSubTextPartName(blockName);
        var blockId = operation.BlockId.GetValueOrDefault(CreateBlockId(blockName, index));
        bool hasTable = !string.IsNullOrWhiteSpace(operation.TableId) || operation.RowIndex.HasValue;
        bool hasRange = operation.Start.HasValue || operation.Length.HasValue;
        bool hasParagraphs = operation.StartParagraphIndex.HasValue || operation.EndParagraphIndex.HasValue;
        if ((hasTable ? 1 : 0) + (hasRange ? 1 : 0) + (hasParagraphs ? 1 : 0) != 1)
        {
            throw new ArgumentException("Specify exactly one merge-block target: tableId/rowIndex, start/length, or startParagraphIndex/endParagraphIndex.");
        }

        int? tableId = null;
        int? rowIndex = null;
        int columnCount = 0;
        if (hasTable)
        {
            tableId = TableOperationUtilities.RequireTableId(operation.TableId);
            rowIndex = TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex));
            var modelTable = TableOperationUtilities.GetModelTable(context.Document, tableId.Value.ToString());
            if (rowIndex.Value >= modelTable.Rows.Count)
            {
                throw new ArgumentException("rowIndex is out of range.");
            }

            columnCount = modelTable.Rows[rowIndex.Value].Cells.Count;
            if (columnCount == 0)
            {
                throw new ArgumentException("The target merge-block row must contain at least one cell.");
            }
        }

        if (context.TryGetTextControl(out var tx))
        {
            if (hasTable)
            {
                var table = TableOperationUtilities.GetTxTable(tx, tableId!.Value);
                SelectTableRow(tx, table, rowIndex!.Value, columnCount);
            }
            else if (hasRange)
            {
                SelectCharacterRange(tx, operation);
            }
            else
            {
                SelectParagraphRange(tx, operation);
            }

            var block = new SubTextPart(subTextPartName, blockId);
            var result = tx.SubTextParts.Add(block);
            if (result != SubTextPartCollection.AddResult.Successful)
            {
                throw new InvalidOperationException($"TX Text Control could not insert merge block '{blockName}': {result}.");
            }
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = hasTable
                ? $"Wrapped table '{tableId}' row {rowIndex} in merge block '{subTextPartName}'."
                : $"Wrapped the selected document range in merge block '{subTextPartName}'.",
            TargetType = "mergeBlock",
            TargetId = subTextPartName,
            Location = hasTable
                ? $"tables['{tableId}'].rows[{rowIndex}]"
                : hasRange
                    ? $"body.range[{operation.Start},{operation.Length}]"
                    : $"paragraphs[{operation.StartParagraphIndex}..{operation.EndParagraphIndex ?? operation.StartParagraphIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["blockName"] = blockName,
                ["subTextPartName"] = subTextPartName,
                ["blockId"] = blockId,
                ["tableId"] = tableId.ToString(),
                ["rowIndex"] = rowIndex,
                ["columnCount"] = hasTable ? columnCount : null,
                ["start"] = operation.Start,
                ["length"] = operation.Length,
                ["startParagraphIndex"] = operation.StartParagraphIndex,
                ["endParagraphIndex"] = operation.EndParagraphIndex
            }
        };
    }

    private static void SelectCharacterRange(ServerTextControl tx, DocumentOperation operation)
    {
        if (!operation.Start.HasValue || !operation.Length.HasValue || operation.Length.Value <= 0)
        {
            throw new ArgumentException("A merge-block character target requires start and a length greater than zero.");
        }

        string text = tx.Text ?? string.Empty;
        int start = operation.Start.Value;
        int length = operation.Length.Value;
        if (start < 0 || start + length > text.Length)
        {
            throw new ArgumentException($"Character range is outside the document text length of {text.Length}.");
        }

        if (operation.ExpectedText is null)
        {
            throw new ArgumentException("expectedText is required for a merge-block character range.");
        }

        if (!string.Equals(text.Substring(start, length), operation.ExpectedText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The character range no longer contains expectedText. Inspect the current document and retry with fresh coordinates.");
        }

        tx.Selection = new Selection(start, length);
    }

    private static void SelectParagraphRange(ServerTextControl tx, DocumentOperation operation)
    {
        int startIndex = operation.StartParagraphIndex
            ?? throw new ArgumentException("startParagraphIndex is required for a paragraph merge block.");
        int endIndex = operation.EndParagraphIndex ?? startIndex;
        if (startIndex < 0 || endIndex < startIndex || endIndex >= tx.Paragraphs.Count)
        {
            throw new ArgumentException($"Paragraph range is invalid. The document contains {tx.Paragraphs.Count} paragraph(s), indexed from 0.");
        }

        tx.Paragraphs[startIndex + 1].Select();
        int start = tx.Selection.Start;
        tx.Paragraphs[endIndex + 1].Select();
        int end = tx.Selection.Start + tx.Selection.Length;
        tx.Selection = new Selection(start, end - start);
    }

    private static void SelectTableRow(ServerTextControl tx, Table table, int rowIndex, int columnCount)
    {
        var firstCell = TableOperationUtilities.GetTxCell(table, rowIndex, 0);
        var lastCell = TableOperationUtilities.GetTxCell(table, rowIndex, columnCount - 1);
        var start = Math.Max(0, firstCell.Start - 1);
        var end = Math.Max(start + 1, lastCell.Start - 1 + (lastCell.Text?.Length ?? 0));
        tx.Selection = new Selection(start, end - start);
    }

    private static string RequireBlockName(string? blockName)
    {
        if (string.IsNullOrWhiteSpace(blockName))
        {
            throw new ArgumentException("blockName is required.");
        }

        var normalized = blockName.Trim();
        if (normalized.StartsWith("txmb_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[5..];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("blockName must not be empty.");
        }

        return normalized;
    }

    private static string ToSubTextPartName(string blockName)
        => "txmb_" + blockName;

    private static int CreateBlockId(string blockName, int index)
        => Math.Abs(HashCode.Combine(blockName, index, Guid.NewGuid())) % int.MaxValue + 1;
}
