using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class ApplyTableStylePresetOperationHandler : IDocumentOperationHandler
{
    private readonly DocumentAutomationOptions _options;

    public ApplyTableStylePresetOperationHandler(IOptions<DocumentAutomationOptions> options)
        : this(options.Value)
    {
    }

    public ApplyTableStylePresetOperationHandler(DocumentAutomationOptions options)
    {
        _options = options;
    }

    public string Type => TableCapabilityPack.ApplyTableStylePreset;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.ApplyTableStylePreset,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Applies a configured table style preset to an existing table by formatting each current cell.",
        Intent = "Use for consistent table styling without manually issuing many cell formatting operations.",
        RequiredProperties = ["type", "tableId"],
        OptionalProperties = ["styleName", "tableStyleName"],
        Properties = new()
        {
            ["tableId"] = "Existing table id.",
            ["styleName"] = "Configured table style preset name. Omit unless the user explicitly asks for a table style/preset; when omitted this operation uses the first configured table preset.",
            ["tableStyleName"] = "Alias for styleName when the caller wants to be explicit that this is a table preset. Omit unless explicitly requested."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.ApplyTableStylePreset,
            ["tableId"] = "10",
            ["styleName"] = "Professional Blue"
        },
        ModelEffects = ["Applies configured text styles and cell styles to the matching neutral table cells."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var tableId = TableOperationUtilities.RequireTableId(operation.TableId);
        var preset = ResolvePreset(operation.TableStyleName ?? operation.StyleName);
        if (preset.HeaderRowIndex < 0)
        {
            throw new ArgumentException("table style preset headerRowIndex must be >= 0.");
        }

        var modelTable = TableOperationUtilities.TryGetModelTable(context.Document, tableId.ToString());
        int rowCount;

        if (context.TryGetTextControl(out var tx))
        {
            var table = TableOperationUtilities.GetTxTable(tx, tableId);
            rowCount = table.Rows.Count;
            if (rowCount == 0)
            {
                throw new InvalidOperationException($"Table '{tableId}' has no rows.");
            }

            if (preset.HeaderRowIndex >= rowCount)
            {
                throw new ArgumentException("table style preset headerRowIndex is out of range for the target table.");
            }

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
                {
                    var (textStyle, cellStyle) = ResolveCellStyles(preset, rowIndex);
                    if (textStyle is null && cellStyle is null)
                    {
                        continue;
                    }

                    var cell = TableOperationUtilities.GetTxCell(table, rowIndex, columnIndex);
                    if (textStyle is not null)
                    {
                        cell.Select();
                        var selection = tx.Selection;
                        DocumentOperationFormatter.ApplyStyle(selection, textStyle);
                        tx.Selection = selection;
                    }

                    if (cellStyle is not null)
                    {
                        DocumentOperationFormatter.ApplyCellStyle(tx, cell, cellStyle);
                    }
                }
            }
        }
        else
        {
            if (modelTable is null)
            {
                throw new InvalidOperationException($"Table '{tableId}' was not found in the document model.");
            }

            rowCount = modelTable.Rows.Count;
            if (rowCount == 0)
            {
                throw new InvalidOperationException($"Table '{tableId}' has no rows.");
            }

            if (preset.HeaderRowIndex >= rowCount)
            {
                throw new ArgumentException("table style preset headerRowIndex is out of range for the target table.");
            }
        }

        var affectedCellIds = new List<string>();
        for (var rowIndex = 0; modelTable is not null && rowIndex < modelTable.Rows.Count; rowIndex++)
        {
            var (textStyle, cellStyle) = ResolveCellStyles(preset, rowIndex);
            foreach (var cell in modelTable.Rows[rowIndex].Cells)
            {
                if (textStyle is not null)
                {
                    TableOperationUtilities.ApplyStyleToModelCell(cell, textStyle);
                }

                if (cellStyle is not null)
                {
                    TableOperationUtilities.ApplyCellStyleToModelCell(cell, cellStyle);
                }

                affectedCellIds.Add(cell.Id);
            }
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Applied table style preset '{preset.Name}' to table '{tableId}'.",
            TargetType = "table",
            TargetId = tableId.ToString(),
            Location = $"tables['{tableId}']",
            Metadata = new Dictionary<string, object?>
            {
                ["tableId"] = tableId.ToString(),
                ["styleName"] = preset.Name,
                ["rowCount"] = rowCount,
                ["cellIds"] = affectedCellIds
            }
        };
    }

    private TableStylePresetDefinition ResolvePreset(string? requestedName)
    {
        if (_options.TableStylePresets.Count == 0)
        {
            throw new InvalidOperationException("No table style presets are configured.");
        }

        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return _options.TableStylePresets[0];
        }

        var normalized = requestedName.Trim();
        return _options.TableStylePresets.FirstOrDefault(preset =>
                   string.Equals(preset.Name, normalized, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Table style preset '{normalized}' was not found.");
    }

    private static (TextStyleDefinition? TextStyle, CellStyleDefinition? CellStyle) ResolveCellStyles(
        TableStylePresetDefinition preset,
        int rowIndex)
    {
        if (rowIndex == preset.HeaderRowIndex)
        {
            return (preset.HeaderStyle, preset.HeaderCellStyle);
        }

        var bodyOrdinal = rowIndex > preset.HeaderRowIndex
            ? rowIndex - preset.HeaderRowIndex - 1
            : rowIndex;
        var useAlternating = bodyOrdinal % 2 == 1;
        return useAlternating
            ? (preset.AlternatingRowStyle ?? preset.BodyStyle, preset.AlternatingRowCellStyle ?? preset.BodyCellStyle)
            : (preset.BodyStyle, preset.BodyCellStyle);
    }
}
