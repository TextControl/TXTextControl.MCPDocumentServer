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
        Description = "Wraps a table row in a TX Text Control merge-block SubTextPart named txmb_<blockName>.",
        Intent = "Use when creating MailMerge templates with repeating table rows for array data such as invoice line items.",
        RequiredProperties = ["type", "blockName", "tableId", "rowIndex"],
        OptionalProperties = ["blockId"],
        Properties = new()
        {
            ["blockName"] = "Merge block name without the txmb_ prefix. The operation stores the SubTextPart as txmb_<blockName>.",
            ["blockId"] = "Optional integer SubTextPart id. If omitted, a positive id is generated.",
            ["tableId"] = "Target table id containing the repeating template row.",
            ["rowIndex"] = "Zero-based table row index to wrap as the repeating merge block."
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
        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var rowIndex = TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex));
        var modelTable = TableOperationUtilities.GetModelTable(context.Document, tableId.ToString());

        if (rowIndex >= modelTable.Rows.Count)
        {
            throw new ArgumentException("rowIndex is out of range.");
        }

        var columnCount = modelTable.Rows[rowIndex].Cells.Count;
        if (columnCount == 0)
        {
            throw new ArgumentException("The target merge-block row must contain at least one cell.");
        }

        if (context.TryGetTextControl(out var tx))
        {
            var table = TableOperationUtilities.GetTxTable(tx, tableId);
            SelectTableRow(tx, table, rowIndex, columnCount);

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
            Detail = $"Wrapped table '{tableId}' row {rowIndex} in merge block '{subTextPartName}'.",
            TargetType = "mergeBlock",
            TargetId = subTextPartName,
            Location = $"tables['{tableId}'].rows[{rowIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["blockName"] = blockName,
                ["subTextPartName"] = subTextPartName,
                ["blockId"] = blockId,
                ["tableId"] = tableId.ToString(),
                ["rowIndex"] = rowIndex,
                ["columnCount"] = columnCount
            }
        };
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
