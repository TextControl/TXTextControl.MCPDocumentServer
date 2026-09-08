using System;
using System.Collections.Generic;
using System.Linq;
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
        Description = "Applies text and/or cell formatting to one or more table cells resolved by coordinates, selected text, row, column, header, or table scope.",
        Intent = "Use for table and cell formatting without calculating text offsets.",
        RequiredProperties = ["type"],
        OptionalProperties = ["tableId", "tableNumber", "tableScope", "rowIndex", "columnIndex", "matchText", "occurrenceIndex", "nearTextPosition", "selectionLength", "style", "cellStyle"],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["tableNumber"] = "One-based table number from get_document_tables; prefer this when imported tables have duplicate id values.",
            ["rowIndex"] = "Zero-based row index.",
            ["columnIndex"] = "Zero-based column index.",
            ["tableScope"] = "cell (default), selectedCells, header, row, column, or table.",
            ["matchText"] = "Selected text used to locate the authoritative TX table/cells.",
            ["nearTextPosition"] = "Non-authoritative browser-position hint used to choose the closest table occurrence.",
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

        if (!context.TryGetTextControl(out var tx))
        {
            return ApplyToModelOnly(context, operation, index);
        }

        List<ResolvedTableCell> targets = TableTargetUtilities.ResolveFormatTargets(tx, operation);
        foreach (ResolvedTableCell target in targets)
        {
            if (operation.Style is not null)
            {
                target.Cell.Select();
                var selection = tx.Selection;
                DocumentOperationFormatter.ApplyStyle(selection, operation.Style);
                tx.Selection = selection;
            }

            if (operation.CellStyle is not null)
            {
                DocumentOperationFormatter.ApplyCellStyle(tx, target.Cell, operation.CellStyle);
            }
        }

        var cellIds = new List<string>();
        foreach (ResolvedTableCell target in targets)
        {
            var modelTable = TableOperationUtilities.TryGetModelTable(
                context.Document,
                target.Table.ID.ToString());
            if (modelTable is null
                || target.RowIndex >= modelTable.Rows.Count
                || target.ColumnIndex >= modelTable.Rows[target.RowIndex].Cells.Count)
            {
                continue;
            }

            var modelCell = modelTable.Rows[target.RowIndex].Cells[target.ColumnIndex];
            if (operation.Style is not null)
            {
                TableOperationUtilities.ApplyStyleToModelCell(modelCell, operation.Style);
            }

            if (operation.CellStyle is not null)
            {
                TableOperationUtilities.ApplyCellStyleToModelCell(modelCell, operation.CellStyle);
            }

            cellIds.Add(modelCell.Id);
        }

        string tableId = targets[0].Table.ID.ToString();
        List<Dictionary<string, object?>> cells = targets
            .Select(target => new Dictionary<string, object?>
            {
                ["rowIndex"] = target.RowIndex,
                ["columnIndex"] = target.ColumnIndex,
                ["start"] = target.Cell.Start - 1,
                ["length"] = target.Cell.Length
            })
            .ToList();

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = targets.Count == 1
                ? $"Formatted table '{tableId}' cell ({targets[0].RowIndex}, {targets[0].ColumnIndex})."
                : $"Formatted {targets.Count} cells in table '{tableId}'.",
            TargetType = targets.Count == 1 ? "tableCell" : "tableCells",
            TargetId = cellIds.Count == 1 ? cellIds[0] : null,
            Location = $"tables['{tableId}']",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = tableId,
                ["tableNumber"] = targets[0].TableNumber,
                ["tableScope"] = operation.TableScope ?? "cell",
                ["cellCount"] = targets.Count,
                ["cells"] = cells,
                ["cellIds"] = cellIds,
                ["hasTextStyle"] = operation.Style is not null,
                ["hasCellStyle"] = operation.CellStyle is not null
            }
        };
    }

    private static OperationResult ApplyToModelOnly(
        DocumentOperationContext context,
        DocumentOperation operation,
        int index)
    {
        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var rowIndex = TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex));
        var columnIndex = TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex));
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
            Type = TableCapabilityPack.FormatTableCell,
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
                ["cellCount"] = 1,
                ["hasTextStyle"] = operation.Style is not null,
                ["hasCellStyle"] = operation.CellStyle is not null
            }
        };
    }
}
