using System;
using System.Collections.Generic;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class FormatTableCellOperationHandler : IDocumentOperationHandler
{
    public string Type => TableCapabilityPack.FormatTableCell;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.FormatTableCell,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Applies text and/or cell formatting to one table cell.",
        Intent = "Use for emphasizing a specific table cell without calculating text offsets.",
        RequiredProperties = ["type", "tableId", "rowIndex", "columnIndex"],
        OptionalProperties = ["style", "cellStyle"],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["rowIndex"] = "Zero-based row index.",
            ["columnIndex"] = "Zero-based column index.",
            ["style"] = "Optional TextStyleDefinition to apply to the complete cell text. Use only when the user explicitly asks for cell text styling.",
            ["cellStyle"] = "Optional CellStyleDefinition for cell-level formatting, such as { backgroundColorHex: '#1F4E79', border: { width: 10, colorHex: '#000000' } }. Use only when the user explicitly asks for cell formatting."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.FormatTableCell,
            ["tableId"] = "10",
            ["rowIndex"] = 0,
            ["columnIndex"] = 1,
            ["style"] = new Dictionary<string, object?> { ["bold"] = true },
            ["cellStyle"] = new Dictionary<string, object?>
            {
                ["backgroundColorHex"] = "#D9EAF7",
                ["border"] = new Dictionary<string, object?> { ["width"] = 10, ["colorHex"] = "#000000" }
            }
        },
        ModelEffects = ["Applies inline run style and/or cell style to the matching neutral table cell."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (operation.Style is null && operation.CellStyle is null)
        {
            throw new ArgumentException("style or cellStyle is required.");
        }

        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var rowIndex = TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex));
        var columnIndex = TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex));

        if (context.TryGetTextControl(out var tx))
        {
            var table = TableOperationUtilities.GetTxTable(tx, tableId);
            var cell = TableOperationUtilities.GetTxCell(table, rowIndex, columnIndex);
            if (operation.Style is not null)
            {
                cell.Select();
                var selection = tx.Selection;
                DocumentOperationFormatter.ApplyStyle(selection, operation.Style);
                tx.Selection = selection;
            }

            if (operation.CellStyle is not null)
            {
                DocumentOperationFormatter.ApplyCellStyle(cell, operation.CellStyle);
            }
        }

        var modelTable = TableOperationUtilities.GetModelTable(context.Document, tableId.ToString());
        var modelCell = TableOperationUtilities.GetModelCell(modelTable, rowIndex, columnIndex);
        if (operation.Style is not null)
        {
            TableOperationUtilities.ApplyStyleToModelCell(modelCell, operation.Style);
        }

        if (operation.CellStyle is not null)
        {
            TableOperationUtilities.ApplyCellStyleToModelCell(modelCell, operation.CellStyle);
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Formatted table '{tableId}' cell ({rowIndex}, {columnIndex}).",
            TargetType = "tableCell",
            TargetId = modelCell.Id,
            Location = $"tables['{tableId}'].rows[{rowIndex}].cells[{columnIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = tableId.ToString(),
                ["rowIndex"] = rowIndex,
                ["columnIndex"] = columnIndex,
                ["cellId"] = modelCell.Id,
                ["hasTextStyle"] = operation.Style is not null,
                ["hasCellStyle"] = operation.CellStyle is not null
            }
        };
    }
}
