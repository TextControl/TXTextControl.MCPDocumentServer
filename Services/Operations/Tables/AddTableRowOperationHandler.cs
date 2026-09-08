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
        RequiredProperties = ["type", "rows"],
        OptionalProperties = ["tableId", "tableNumber"],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["tableNumber"] = "One-based table number from get_document_tables. Use when imported table ids are zero or duplicated.",
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
        var values = TableOperationUtilities.NormalizeRowValues(operation.Rows);
        int? requestedTableId = string.IsNullOrWhiteSpace(operation.TableId)
            ? null
            : TableOperationUtilities.RequireTableId(operation.TableId);
        string resultTableId = requestedTableId?.ToString() ?? string.Empty;
        var modelTable = requestedTableId.HasValue
            ? TableOperationUtilities.TryGetModelTable(context.Document, resultTableId)
            : null;
        var columnCount = modelTable is null || modelTable.Rows.Count == 0
            ? values.Count
            : modelTable.Rows[0].Cells.Count;
        var rowIndex = modelTable?.Rows.Count ?? 0;
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
            var resolved = TableTargetUtilities.ResolveExplicitTable(
                               tx,
                               operation.TableId,
                               operation.TableNumber)
                           ?? throw new ArgumentException("tableId or tableNumber is required.");
            var table = resolved.Table;
            resultTableId = table.ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
            modelTable ??= TableOperationUtilities.TryGetModelTable(context.Document, resultTableId);
            var currentRowCount = table.Rows.Count;
            var currentColumnCount = table.Columns.Count;
            columnCount = currentColumnCount;
            rowIndex = currentRowCount;
            if (values.Count > currentColumnCount)
            {
                throw new ArgumentException("The added row cannot contain more cells than the TX table columns.");
            }

            while (values.Count < currentColumnCount)
            {
                values.Add(string.Empty);
            }

            var lastRowFirstCell = table.Cells.GetItem(currentRowCount, 1);
            tx.Selection = new Selection(Math.Max(0, lastRowFirstCell.Start - 1), 0);
            if (!table.Rows.Add(TableAddPosition.After, 1))
            {
                throw new InvalidOperationException("TX Text Control could not add the table row.");
            }

            table = TableTargetUtilities.ResolveExplicitTable(
                        tx,
                        operation.TableId,
                        operation.TableNumber)!.Value.Table;
            for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
            {
                table.Cells.GetItem(currentRowCount + 1, columnIndex + 1).Text = values[columnIndex];
            }
        }
        else if (modelTable is null)
        {
            throw new InvalidOperationException("The requested table was not found in the document model.");
        }

        var row = new DocumentModel.TableRow
        {
            Id = $"{resultTableId}:r{rowIndex + 1}"
        };
        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            var cell = new DocumentModel.TableCell
            {
                Id = $"{resultTableId}:r{rowIndex + 1}c{columnIndex + 1}"
            };
            TableOperationUtilities.SetModelCellText(cell, values[columnIndex]);
            row.Cells.Add(cell);
        }

        modelTable?.Rows.Add(row);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Added row {rowIndex} to table number {operation.TableNumber?.ToString() ?? "(by id)"} (TX id '{resultTableId}').",
            TargetType = "tableRow",
            TargetId = row.Id,
            Location = $"tables['{resultTableId}'].rows[{rowIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = resultTableId,
                ["tableNumber"] = operation.TableNumber,
                ["rowIndex"] = rowIndex,
                ["rowId"] = row.Id,
                ["cellIds"] = row.Cells.Select(cell => cell.Id).ToList(),
                ["columnCount"] = values.Count
            }
        };
    }
}
