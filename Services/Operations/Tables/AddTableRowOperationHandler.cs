using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AddTableRowOperationHandler : IDocumentOperationHandler
{
    public string Type => TableCapabilityPack.AddTableRow;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.AddTableRow,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Adds one row to the end of an existing table.",
        Intent = "Use when appending data rows to an existing table.",
        RequiredProperties = ["type", "tableId", "rows"],
        OptionalProperties = [],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["rows"] = "Exactly one row of cell text. The row must not contain more cells than the existing table columns."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.AddTableRow,
            ["tableId"] = "10",
            ["rows"] = new object[]
            {
                new[] { "Brazil", "$421,750", "705" }
            }
        },
        ModelEffects = ["Appends one neutral table row with text cells."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var values = TableOperationUtilities.NormalizeRowValues(operation.Rows);
        var modelTable = TableOperationUtilities.GetModelTable(context.Document, tableId.ToString());
        var columnCount = modelTable.Rows.Count == 0 ? values.Count : modelTable.Rows[0].Cells.Count;
        if (values.Count > columnCount)
        {
            throw new ArgumentException("The added row cannot contain more cells than the existing table.");
        }

        while (values.Count < columnCount)
        {
            values.Add(string.Empty);
        }

        if (context.TryGetTextControl(out var tx))
        {
            var table = TableOperationUtilities.GetTxTable(tx, tableId);
            var currentRowCount = table.Rows.Count;
            var currentColumnCount = table.Columns.Count;
            if (values.Count > currentColumnCount)
            {
                throw new ArgumentException("The added row cannot contain more cells than the TX table columns.");
            }

            var lastRowFirstCell = table.Cells.GetItem(currentRowCount, 1);
            tx.Selection = new Selection(Math.Max(0, lastRowFirstCell.Start - 1), 0);
            if (!table.Rows.Add(TableAddPosition.After, 1))
            {
                throw new InvalidOperationException("TX Text Control could not add the table row.");
            }

            table = TableOperationUtilities.GetTxTable(tx, tableId);
            for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
            {
                table.Cells.GetItem(currentRowCount + 1, columnIndex + 1).Text = values[columnIndex];
            }
        }

        var rowIndex = modelTable.Rows.Count;
        var row = new DocumentModel.TableRow
        {
            Id = $"{tableId}:r{rowIndex + 1}"
        };
        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            var cell = new DocumentModel.TableCell
            {
                Id = $"{tableId}:r{rowIndex + 1}c{columnIndex + 1}"
            };
            TableOperationUtilities.SetModelCellText(cell, values[columnIndex]);
            row.Cells.Add(cell);
        }

        modelTable.Rows.Add(row);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Added row {rowIndex} to table '{tableId}'.",
            TargetType = "tableRow",
            TargetId = row.Id,
            Location = $"tables['{tableId}'].rows[{rowIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = tableId.ToString(),
                ["rowIndex"] = rowIndex,
                ["rowId"] = row.Id,
                ["cellIds"] = row.Cells.Select(cell => cell.Id).ToList(),
                ["columnCount"] = values.Count
            }
        };
    }
}
