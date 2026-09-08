using System;
using System.Collections.Generic;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class SetTableCellTextOperationHandler : IDocumentOperationHandler
{
    public string Type => TableCapabilityPack.SetTableCellText;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.SetTableCellText,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Sets text in one table cell by table id and zero-based row/column indices.",
        Intent = "Use for correcting or filling a specific table cell.",
        RequiredProperties = ["type", "tableId", "rowIndex", "columnIndex", "text"],
        OptionalProperties = [],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["rowIndex"] = "Zero-based row index.",
            ["columnIndex"] = "Zero-based column index.",
            ["text"] = "Replacement cell text."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.SetTableCellText,
            ["tableId"] = "10",
            ["rowIndex"] = 1,
            ["columnIndex"] = 2,
            ["text"] = "1280"
        },
        ModelEffects = ["Updates the matching table cell text in the neutral document model."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var rowIndex = TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex));
        var columnIndex = TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex));
        var text = operation.Text ?? string.Empty;

        if (context.TryGetTextControl(out var tx))
        {
            var table = TableOperationUtilities.GetTxTable(tx, tableId);
            var cell = TableOperationUtilities.GetTxCell(table, rowIndex, columnIndex);
            cell.Text = text;
            TableOperationUtilities.ApplyDefaultCellTextFormatting(
                tx,
                cell,
                context.GetDefaultTextStyle(),
                context.GetDefaultParagraphStyleName());
        }

        var modelTable = TableOperationUtilities.TryGetModelTable(context.Document, tableId.ToString());
        var modelCell = modelTable is null
            ? null
            : TableOperationUtilities.GetModelCell(modelTable, rowIndex, columnIndex);
        if (modelCell is not null)
        {
            TableOperationUtilities.SetModelCellText(modelCell, text);
        }
        else if (!context.TryGetTextControl(out _))
        {
            throw new InvalidOperationException($"Table '{tableId}' was not found in the document model.");
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Set table '{tableId}' cell ({rowIndex}, {columnIndex}).",
            TargetType = "tableCell",
            TargetId = modelCell?.Id,
            Location = $"tables['{tableId}'].rows[{rowIndex}].cells[{columnIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = tableId.ToString(),
                ["rowIndex"] = rowIndex,
                ["columnIndex"] = columnIndex,
                ["cellId"] = modelCell?.Id,
                ["textLength"] = text.Length
            }
        };
    }

}
