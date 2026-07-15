using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class FormatTableColumnOperationHandler : IDocumentOperationHandler
{
    public string Type => TableCapabilityPack.FormatTableColumn;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.FormatTableColumn,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Sets the width of one table column.",
        Intent = "Use for layout changes such as making a description column wider without editing cell text.",
        RequiredProperties = ["type", "tableId", "columnIndex", "width"],
        OptionalProperties = ["unit"],
        Properties = new()
        {
            ["tableId"] = "Existing table id, or 'first' for the first table in an imported document.",
            ["columnIndex"] = "Zero-based column index.",
            ["width"] = "Requested column width.",
            ["unit"] = "Optional width unit: pt, px, in, cm, mm, or twips. Defaults to pt."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.FormatTableColumn,
            ["tableId"] = "10",
            ["columnIndex"] = 1,
            ["width"] = 220,
            ["unit"] = "pt"
        },
        ModelEffects = ["Updates document.sections[].blocks[].table.columnWidths for the selected column."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var tableSelector = RequireTableSelector(operation.TableId);
        var columnIndex = TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex));
        if (!operation.Width.HasValue)
        {
            throw new ArgumentException("width is required for format_table_column.");
        }

        var unit = string.IsNullOrWhiteSpace(operation.Unit) ? "pt" : operation.Unit.Trim();
        var widthTwips = ToTwips(operation.Width.Value, unit);

        var resolvedTableId = tableSelector;
        var modelTable = tableSelector.Equals("first", StringComparison.OrdinalIgnoreCase)
            ? TableOperationUtilities.TryGetFirstModelTable(context.Document)
            : TableOperationUtilities.TryGetModelTable(context.Document, tableSelector);

        if (context.TryGetTextControl(out var tx))
        {
            var table = tableSelector.Equals("first", StringComparison.OrdinalIgnoreCase)
                ? GetFirstTxTable(tx)
                : TableOperationUtilities.GetTxTable(tx, TableOperationUtilities.RequireTableId(tableSelector));
            if (columnIndex >= table.Columns.Count)
            {
                throw new ArgumentException("columnIndex is out of range.");
            }

            var column = table.Columns.GetItem(columnIndex + 1);
            column.Width = widthTwips;
            FitTableToAvailableWidth(context, table, columnIndex);
            resolvedTableId = table.ID.ToString();
            widthTwips = table.Columns.GetItem(columnIndex + 1).Width;
        }

        if (modelTable is not null)
        {
            var modelColumnWidths = ResolveCurrentModelColumnWidths(context, modelTable, columnIndex, widthTwips);
            while (modelTable.ColumnWidths.Count < modelColumnWidths.Length)
            {
                modelTable.ColumnWidths.Add(null);
            }

            for (var i = 0; i < modelColumnWidths.Length; i++)
            {
                modelTable.ColumnWidths[i] = (float)Math.Round(modelColumnWidths[i] / 20f, 2);
            }

            modelTable.ColumnWidthUnit = "pt";
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Set table '{resolvedTableId}' column {columnIndex} width to {operation.Width.Value:0.##} {unit}.",
            TargetType = "tableColumn",
            TargetId = $"{resolvedTableId}:{columnIndex}",
            Location = $"tables['{resolvedTableId}'].columns[{columnIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = resolvedTableId,
                ["columnIndex"] = columnIndex,
                ["width"] = operation.Width.Value,
                ["unit"] = unit,
                ["widthTwips"] = widthTwips
            }
        };
    }

    private static string RequireTableSelector(string? tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            throw new ArgumentException("tableId is required.");
        }

        var selector = tableId.Trim();
        if (selector.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            return selector;
        }

        TableOperationUtilities.RequireTableId(selector);
        return selector;
    }

    private static Table GetFirstTxTable(ServerTextControl tx)
        => tx.Tables.Cast<Table>().FirstOrDefault()
           ?? throw new InvalidOperationException("No table was found.");

    private static void FitTableToAvailableWidth(
        DocumentOperationContext context,
        Table table,
        int preferredColumnIndex)
    {
        var availableWidth = GetAvailableWidthTwips(context);
        if (!availableWidth.HasValue || table.Columns.Count == 0)
        {
            return;
        }

        var widths = Enumerable
            .Range(1, table.Columns.Count)
            .Select(column => table.Columns.GetItem(column).Width)
            .ToArray();
        if (widths.Sum() <= availableWidth.Value)
        {
            return;
        }

        var fitted = FitWidths(widths, preferredColumnIndex, availableWidth.Value);
        for (var i = 0; i < fitted.Length; i++)
        {
            table.Columns.GetItem(i + 1).Width = fitted[i];
        }
    }

    private static int[] ResolveCurrentModelColumnWidths(
        DocumentOperationContext context,
        TxTextControl.McpServer.Models.DocumentModel.Table modelTable,
        int preferredColumnIndex,
        int preferredWidthTwips)
    {
        var columnCount = modelTable.Rows.Count == 0
            ? preferredColumnIndex + 1
            : modelTable.Rows.Max(row => row.Cells.Count);
        columnCount = Math.Max(columnCount, preferredColumnIndex + 1);

        var widths = new int[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            widths[i] = i < modelTable.ColumnWidths.Count && modelTable.ColumnWidths[i].HasValue
                ? ToTwips(modelTable.ColumnWidths[i]!.Value, modelTable.ColumnWidthUnit ?? "pt")
                : 1440;
        }

        widths[preferredColumnIndex] = preferredWidthTwips;
        var availableWidth = GetAvailableWidthTwips(context);
        return availableWidth.HasValue && widths.Sum() > availableWidth.Value
            ? FitWidths(widths, preferredColumnIndex, availableWidth.Value)
            : widths;
    }

    private static int[] FitWidths(
        IReadOnlyList<int> widths,
        int preferredColumnIndex,
        int availableWidth)
    {
        const int minimumColumnWidth = 360;
        var fitted = widths.ToArray();
        var otherIndices = Enumerable.Range(0, fitted.Length)
            .Where(index => index != preferredColumnIndex)
            .ToList();

        if (otherIndices.Count == 0)
        {
            fitted[preferredColumnIndex] = availableWidth;
            return fitted;
        }

        var maximumPreferredWidth = Math.Max(minimumColumnWidth, availableWidth - (minimumColumnWidth * otherIndices.Count));
        var preferredWidth = Math.Min(fitted[preferredColumnIndex], maximumPreferredWidth);
        var remainingWidth = Math.Max(0, availableWidth - preferredWidth);
        var otherTotal = otherIndices.Sum(index => fitted[index]);
        if (otherTotal <= 0)
        {
            var equalWidth = remainingWidth / otherIndices.Count;
            foreach (var index in otherIndices)
            {
                fitted[index] = equalWidth;
            }
        }
        else
        {
            foreach (var index in otherIndices)
            {
                fitted[index] = Math.Max(minimumColumnWidth, (int)Math.Floor(remainingWidth * fitted[index] / (float)otherTotal));
            }
        }

        fitted[preferredColumnIndex] = preferredWidth;
        var difference = availableWidth - fitted.Sum();
        fitted[^1] += difference;
        return fitted;
    }

    private static int? GetAvailableWidthTwips(DocumentOperationContext context)
    {
        var layout = context.GetCurrentSection().PageLayout;
        if (layout?.PageWidth is null)
        {
            return null;
        }

        var pageWidth = ToTwips(layout.PageWidth.Value, layout.Unit ?? "pt");
        var left = ToTwips(layout.MarginLeft ?? 72f, layout.Unit ?? "pt");
        var right = ToTwips(layout.MarginRight ?? 72f, layout.Unit ?? "pt");
        var available = pageWidth - left - right;
        return available > 0 ? available : null;
    }

    private static int ToTwips(float value, string unit)
    {
        if (value <= 0)
        {
            throw new ArgumentException("Column width must be greater than 0.");
        }

        var twips = unit.Trim().ToLowerInvariant() switch
        {
            "twip" or "twips" => value,
            "pt" or "point" or "points" => value * 20f,
            "px" or "pixel" or "pixels" => value * 15f,
            "in" or "inch" or "inches" => value * 1440f,
            "cm" => value * 1440f / 2.54f,
            "mm" => value * 1440f / 25.4f,
            _ => throw new ArgumentException("Column width unit must be one of: pt, px, in, cm, mm, twips.")
        };

        return (int)Math.Round(twips);
    }
}
