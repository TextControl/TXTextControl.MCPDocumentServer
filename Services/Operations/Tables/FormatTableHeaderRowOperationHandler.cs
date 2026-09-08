using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class FormatTableHeaderRowOperationHandler : IDocumentOperationHandler
{
    public string Type => TableCapabilityPack.FormatTableHeaderRow;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.FormatTableHeaderRow,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Applies text and/or cell formatting to every cell in one table header row.",
        Intent = "Use for making table headers bold, colored, shaded, or otherwise emphasized.",
        RequiredProperties = ["type", "tableId"],
        OptionalProperties = ["rowIndex", "style", "cellStyle"],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["rowIndex"] = "Optional zero-based header row index. Defaults to 0.",
            ["style"] = "Optional TextStyleDefinition applied to each cell text in the row. Use only when the user explicitly asks for header row text styling.",
            ["cellStyle"] = "Optional CellStyleDefinition applied to each cell in the row, such as { backgroundColorHex: '#1F4E79', border: { bottom: { width: 20, colorHex: '#000000' } } }. Use only when the user explicitly asks for header row/cell formatting."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.FormatTableHeaderRow,
            ["tableId"] = "10",
            ["style"] = new Dictionary<string, object?> { ["bold"] = true, ["colorHex"] = "#FFFFFF" },
            ["cellStyle"] = new Dictionary<string, object?>
            {
                ["backgroundColorHex"] = "#1F4E79",
                ["border"] = new Dictionary<string, object?>
                {
                    ["bottom"] = new Dictionary<string, object?> { ["width"] = 20, ["colorHex"] = "#000000" }
                }
            }
        },
        ModelEffects = ["Applies inline run styles and/or cell styles to each neutral cell in the selected row."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (operation.Style is null && operation.CellStyle is null)
        {
            throw new ArgumentException("style or cellStyle is required.");
        }

        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var rowIndex = operation.RowIndex ?? 0;
        if (rowIndex < 0)
        {
            throw new ArgumentException("rowIndex must be >= 0.");
        }

        var modelTable = TableOperationUtilities.TryGetModelTable(context.Document, tableId.ToString());
        int columnCount;

        if (context.TryGetTextControl(out var tx))
        {
            var table = TableOperationUtilities.GetTxTable(tx, tableId);
            if (rowIndex >= table.Rows.Count)
            {
                throw new ArgumentException("rowIndex is out of range.");
            }

            columnCount = table.Columns.Count;
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
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
                    DocumentOperationFormatter.ApplyCellStyle(tx, cell, operation.CellStyle);
                }
            }
        }
        else
        {
            if (modelTable is null)
            {
                throw new InvalidOperationException($"Table '{tableId}' was not found in the document model.");
            }

            if (rowIndex >= modelTable.Rows.Count)
            {
                throw new ArgumentException("rowIndex is out of range.");
            }

            columnCount = modelTable.Rows[rowIndex].Cells.Count;
        }

        var modelRow = modelTable is not null && rowIndex < modelTable.Rows.Count
            ? modelTable.Rows[rowIndex]
            : null;
        foreach (var cell in modelRow?.Cells ?? [])
        {
            if (operation.Style is not null)
            {
                TableOperationUtilities.ApplyStyleToModelCell(cell, operation.Style);
            }

            if (operation.CellStyle is not null)
            {
                TableOperationUtilities.ApplyCellStyleToModelCell(cell, operation.CellStyle);
            }
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Formatted table '{tableId}' header row {rowIndex}.",
            TargetType = "tableRow",
            TargetId = modelRow?.Id,
            Location = $"tables['{tableId}'].rows[{rowIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = tableId.ToString(),
                ["rowIndex"] = rowIndex,
                ["rowId"] = modelRow?.Id,
                ["cellCount"] = columnCount,
                ["cellIds"] = modelRow?.Cells.Select(cell => cell.Id).ToList() ?? [],
                ["hasTextStyle"] = operation.Style is not null,
                ["hasCellStyle"] = operation.CellStyle is not null
            }
        };
    }
}
