using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendTableOperationHandler : IDocumentOperationHandler
{
    private const int MinimumTxTableId = 10;

    public string Type => TableCapabilityPack.AppendTable;
    public string CapabilityPack => TableCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = TableCapabilityPack.AppendTable,
        CapabilityPack = TableCapabilityPack.PackName,
        Description = "Appends or inserts a simple rectangular table from row and cell text.",
        Intent = "Use for structured tabular content where the AI can provide rows directly.",
        RequiredProperties = ["type", "rows"],
        OptionalProperties = ["tableId", "styleName", "paragraphIndex", "placement"],
        Properties = new()
        {
            ["rows"] = "Array of rows, where each row is an array of cell text. Missing cells in shorter rows are padded as empty text.",
            ["tableId"] = "Optional TX table id as an integer string between 10 and 32767.",
            ["styleName"] = "Reserved optional table style name for future formatting support.",
            ["paragraphIndex"] = "Optional zero-based body paragraph index used with placement 'before' or 'after'.",
            ["placement"] = "Optional placement: end, before, or after. Defaults to end."
        },
        Example = new()
        {
            ["type"] = TableCapabilityPack.AppendTable,
            ["tableId"] = "10",
            ["paragraphIndex"] = 0,
            ["placement"] = "after",
            ["rows"] = new object[]
            {
                new[] { "country", "sales", "qty" },
                new[] { "Germany", "$842,000", "1280" }
            }
        },
        ModelEffects = ["Adds or inserts a document.sections[].blocks[] table block with rows, cells, and paragraph text per cell."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var rows = NormalizeRows(operation.Rows);
        var rowCount = rows.Count;
        var columnCount = rows.Max(row => row.Count);
        var txTableId = ResolveTableId(context.Document, operation.TableId);
        var target = ResolveInsertionTarget(context.Document, operation);

        if (context.TryGetTextControl(out var tx))
        {
            SetInsertionPoint(context, tx, target);
            if (!tx.Tables.Add(rowCount, columnCount, txTableId))
            {
                throw new InvalidOperationException("TX Text Control could not insert the table.");
            }

            var table = tx.Tables.GetItem(txTableId)
                ?? throw new InvalidOperationException("TX Text Control inserted the table, but it could not be found by id.");
            var columnWidths = ResolveAutoFitColumnWidths(context, rows, columnCount);

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    var value = columnIndex < rows[rowIndex].Count ? rows[rowIndex][columnIndex] : string.Empty;
                    var cell = table.Cells.GetItem(rowIndex + 1, columnIndex + 1);
                    cell.Text = value;
                    TableOperationUtilities.ApplyDefaultCellTextFormatting(
                        tx,
                        cell,
                        context.GetDefaultTextStyle(),
                        context.GetDefaultParagraphStyleName());
                }
            }

            if (columnWidths is not null)
            {
                ApplyColumnWidths(table, columnWidths);
            }

            context.HasOpenParagraph = false;
        }

        var neutralTable = ToNeutralTable(txTableId, operation, rows, columnCount);
        var modelColumnWidths = ResolveAutoFitColumnWidths(context, rows, columnCount);
        if (modelColumnWidths is not null)
        {
            neutralTable.ColumnWidths = modelColumnWidths.Select(width => (float?)Math.Round(width / 20f, 2)).ToList();
            neutralTable.ColumnWidthUnit = "pt";
        }

        var tableBlock = new DocumentModel.DocumentBlock
        {
            Type = "table",
            Table = neutralTable
        };
        var mainSection = context.GetMainSection();
        var blockIndex = target.ModelBlockIndex ?? mainSection.Blocks.Count;
        if (target.ModelBlockIndex.HasValue)
        {
            mainSection.Blocks.Insert(target.ModelBlockIndex.Value, tableBlock);
        }
        else
        {
            mainSection.Blocks.Add(tableBlock);
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = target.Placement == TableInsertionPlacement.End
                ? $"Appended table '{neutralTable.Id}' with {rowCount} rows and {columnCount} columns."
                : $"Inserted table '{neutralTable.Id}' {target.Placement.ToString().ToLowerInvariant()} paragraph {target.ParagraphIndex} with {rowCount} rows and {columnCount} columns.",
            TargetType = "table",
            TargetId = neutralTable.Id,
            Location = $"sections[0].blocks[{blockIndex}].table",
            Metadata = new Dictionary<string, object?>
            {
                ["sectionIndex"] = 0,
                ["blockIndex"] = blockIndex,
                ["tableId"] = neutralTable.Id,
                ["rowCount"] = rowCount,
                ["columnCount"] = columnCount,
                ["placement"] = target.Placement.ToString().ToLowerInvariant(),
                ["paragraphIndex"] = target.ParagraphIndex
            }
        };
    }

    private static TableInsertionTarget ResolveInsertionTarget(
        DocumentModel.Document document,
        DocumentOperation operation)
    {
        var placement = ResolvePlacement(operation.Placement);
        if (placement == TableInsertionPlacement.End && !operation.ParagraphIndex.HasValue)
        {
            return new TableInsertionTarget(TableInsertionPlacement.End, null, null);
        }

        if (placement == TableInsertionPlacement.End && operation.ParagraphIndex.HasValue)
        {
            throw new ArgumentException("paragraphIndex can only be used with placement 'before' or 'after'.");
        }

        if (placement != TableInsertionPlacement.End && !operation.ParagraphIndex.HasValue)
        {
            throw new ArgumentException("paragraphIndex is required when placement is 'before' or 'after'.");
        }

        var paragraphIndex = operation.ParagraphIndex!.Value;
        if (paragraphIndex < 0)
        {
            throw new ArgumentException("paragraphIndex must be >= 0.");
        }

        var section = document.Sections.Count == 0 ? null : document.Sections[0];
        var currentParagraphIndex = 0;
        if (section is not null)
        {
            for (var blockIndex = 0; blockIndex < section.Blocks.Count; blockIndex++)
            {
                var block = section.Blocks[blockIndex];
                if (block.Paragraph is null)
                {
                    continue;
                }

                if (currentParagraphIndex == paragraphIndex)
                {
                    return new TableInsertionTarget(
                        placement,
                        paragraphIndex,
                        placement == TableInsertionPlacement.Before ? blockIndex : blockIndex + 1);
                }

                currentParagraphIndex++;
            }
        }

        throw new ArgumentException("paragraphIndex is out of range.");
    }

    private static TableInsertionPlacement ResolvePlacement(string? placement)
    {
        if (string.IsNullOrWhiteSpace(placement))
        {
            return TableInsertionPlacement.End;
        }

        return placement.Trim().ToLowerInvariant() switch
        {
            "end" => TableInsertionPlacement.End,
            "before" => TableInsertionPlacement.Before,
            "after" => TableInsertionPlacement.After,
            _ => throw new ArgumentException("placement must be 'end', 'before', or 'after'.")
        };
    }

    private static void SetInsertionPoint(DocumentOperationContext context, ServerTextControl tx, TableInsertionTarget target)
    {
        if (target.Placement == TableInsertionPlacement.End)
        {
            var insertionIndex = (tx.Text ?? string.Empty).Length;
            if (context.HasOpenParagraph)
            {
                tx.Selection = new Selection(insertionIndex, 0) { Text = "\r\n" };
                insertionIndex += 2;
            }

            tx.Selection = new Selection(insertionIndex, 0);
            return;
        }

        var paragraphIndex = target.ParagraphIndex!.Value;
        var collectionIndex = paragraphIndex + 1;
        if (collectionIndex < 1 || collectionIndex > tx.Paragraphs.Count)
        {
            throw new ArgumentException("paragraphIndex is out of range.");
        }

        var paragraph = tx.Paragraphs[collectionIndex];
        paragraph.Select();
        var selection = tx.Selection;
        var start = Math.Max(0, selection.Start);
        var insertionPosition = target.Placement == TableInsertionPlacement.Before
            ? start
            : Math.Max(start, selection.Start + selection.Length);
        tx.Selection = new Selection(insertionPosition, 0);
    }

    private static int[]? ResolveAutoFitColumnWidths(
        DocumentOperationContext context,
        IReadOnlyList<List<string>> rows,
        int columnCount)
    {
        var layout = context.GetCurrentSection().PageLayout;
        if (layout?.PageWidth is null)
        {
            return null;
        }

        var pageWidthTwips = ToTwips(layout.PageWidth.Value, layout.Unit);
        var leftMarginTwips = ToTwips(layout.MarginLeft ?? 72f, layout.Unit);
        var rightMarginTwips = ToTwips(layout.MarginRight ?? 72f, layout.Unit);
        var availableWidth = pageWidthTwips - leftMarginTwips - rightMarginTwips;
        if (availableWidth <= 0)
        {
            return null;
        }

        var weights = new float[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var maxLength = rows
                .Select(row => columnIndex < row.Count ? row[columnIndex]?.Length ?? 0 : 0)
                .DefaultIfEmpty(0)
                .Max();
            weights[columnIndex] = Math.Clamp(maxLength, 4, 24);
        }

        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
        {
            return null;
        }

        var widths = new int[columnCount];
        var assigned = 0;
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            widths[columnIndex] = Math.Max(720, (int)Math.Floor(availableWidth * weights[columnIndex] / totalWeight));
            assigned += widths[columnIndex];
        }

        var overflow = assigned - availableWidth;
        if (overflow > 0)
        {
            widths[^1] = Math.Max(720, widths[^1] - overflow);
        }
        else if (overflow < 0)
        {
            widths[^1] += -overflow;
        }

        return widths;
    }

    private static void ApplyColumnWidths(Table table, IReadOnlyList<int> columnWidths)
    {
        for (var columnIndex = 0; columnIndex < columnWidths.Count && columnIndex < table.Columns.Count; columnIndex++)
        {
            table.Columns.GetItem(columnIndex + 1).Width = columnWidths[columnIndex];
        }
    }

    private static int ToTwips(float value, string? unit)
    {
        var normalized = string.IsNullOrWhiteSpace(unit) ? "pt" : unit.Trim().ToLowerInvariant();
        var points = normalized switch
        {
            "pt" or "point" or "points" => value,
            "in" or "inch" or "inches" => value * 72f,
            "cm" or "centimeter" or "centimeters" => value * 72f / 2.54f,
            "mm" or "millimeter" or "millimeters" => value * 72f / 25.4f,
            "twip" or "twips" => value / 20f,
            _ => value
        };

        return (int)Math.Round(points * 20f);
    }

    private static List<List<string>> NormalizeRows(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException("rows must contain at least one row.");
        }

        var normalized = new List<List<string>>();
        foreach (var row in rows)
        {
            if (row is null)
            {
                throw new ArgumentException("rows cannot contain null row values.");
            }

            normalized.Add(row.Select(value => value ?? string.Empty).ToList());
        }

        if (normalized.Any(row => row.Count == 0))
        {
            throw new ArgumentException("each table row must contain at least one cell.");
        }

        return normalized;
    }

    private static int ResolveTableId(DocumentModel.Document document, string? requestedTableId)
    {
        if (!string.IsNullOrWhiteSpace(requestedTableId))
        {
            var normalized = requestedTableId.Trim();
            if (!int.TryParse(normalized, out var parsed))
            {
                throw new ArgumentException("tableId must be an integer string when provided.");
            }

            if (parsed < MinimumTxTableId || parsed > short.MaxValue)
            {
                throw new ArgumentException($"tableId must be between {MinimumTxTableId} and {short.MaxValue}.");
            }

            if (GetExistingTableIds(document).Contains(parsed))
            {
                throw new InvalidOperationException($"Table id '{parsed}' already exists.");
            }

            return parsed;
        }

        var existing = GetExistingTableIds(document);
        for (var candidate = MinimumTxTableId; candidate <= short.MaxValue; candidate++)
        {
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No TX Text Control table ids are available.");
    }

    private static HashSet<int> GetExistingTableIds(DocumentModel.Document document)
        => document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => block.Table?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => int.TryParse(id, out var parsed) ? parsed : 0)
            .Where(id => id >= MinimumTxTableId)
            .ToHashSet();

    private static DocumentModel.Table ToNeutralTable(
        int txTableId,
        DocumentOperation operation,
        IReadOnlyList<List<string>> rows,
        int columnCount)
    {
        var table = new DocumentModel.Table
        {
            Id = txTableId.ToString(),
            StyleName = string.IsNullOrWhiteSpace(operation.StyleName) ? null : operation.StyleName.Trim()
        };

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new DocumentModel.TableRow
            {
                Id = $"{txTableId}:r{rowIndex + 1}"
            };

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var text = columnIndex < rows[rowIndex].Count ? rows[rowIndex][columnIndex] : string.Empty;
                row.Cells.Add(new DocumentModel.TableCell
                {
                    Id = $"{txTableId}:r{rowIndex + 1}c{columnIndex + 1}",
                    Blocks =
                    [
                        new DocumentModel.DocumentBlock
                        {
                            Type = "paragraph",
                            Paragraph = new DocumentModel.Paragraph
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                Runs =
                                [
                                    new DocumentModel.Run
                                    {
                                        Id = Guid.NewGuid().ToString("N"),
                                        Text = text
                                    }
                                ]
                            }
                        }
                    ]
                });
            }

            table.Rows.Add(row);
        }

        return table;
    }

    private enum TableInsertionPlacement
    {
        End,
        Before,
        After
    }

    private sealed record TableInsertionTarget(
        TableInsertionPlacement Placement,
        int? ParagraphIndex,
        int? ModelBlockIndex);
}
