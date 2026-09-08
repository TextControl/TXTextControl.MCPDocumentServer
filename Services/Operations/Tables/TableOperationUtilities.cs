using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal static class TableOperationUtilities
{
    public static int RequireTableId(string? tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            throw new ArgumentException("tableId is required.");
        }

        if (!int.TryParse(tableId.Trim(), out var parsed))
        {
            throw new ArgumentException("tableId must be an integer string.");
        }

        return parsed;
    }

    public static int RequireIndex(int? value, string name)
    {
        if (!value.HasValue)
        {
            throw new ArgumentException($"{name} is required.");
        }

        if (value.Value < 0)
        {
            throw new ArgumentException($"{name} must be >= 0.");
        }

        return value.Value;
    }

    public static Table GetTxTable(ServerTextControl tx, int tableId)
        => tx.Tables.GetItem(tableId)
           ?? throw new InvalidOperationException($"Table '{tableId}' was not found.");

    public static TableCell GetTxCell(Table table, int rowIndex, int columnIndex)
        => table.Cells.GetItem(rowIndex + 1, columnIndex + 1);

    public static DocumentModel.Table GetModelTable(DocumentModel.Document document, string tableId)
        => TryGetModelTable(document, tableId)
           ?? throw new InvalidOperationException($"Table '{tableId}' was not found in the document model.");

    public static DocumentModel.Table? TryGetModelTable(DocumentModel.Document document, string tableId)
        => document.Sections
               .SelectMany(section => section.Blocks)
               .Select(block => block.Table)
               .FirstOrDefault(table => table is not null && string.Equals(table.Id, tableId, StringComparison.OrdinalIgnoreCase));

    public static DocumentModel.Table? TryGetFirstModelTable(DocumentModel.Document document)
        => document.Sections
               .SelectMany(section => section.Blocks)
               .Select(block => block.Table)
               .FirstOrDefault(table => table is not null);

    public static DocumentModel.TableCell GetModelCell(DocumentModel.Table table, int rowIndex, int columnIndex)
    {
        if (rowIndex >= table.Rows.Count)
        {
            throw new ArgumentException("rowIndex is out of range.");
        }

        if (columnIndex >= table.Rows[rowIndex].Cells.Count)
        {
            throw new ArgumentException("columnIndex is out of range.");
        }

        return table.Rows[rowIndex].Cells[columnIndex];
    }

    public static void SetModelCellText(DocumentModel.TableCell cell, string text)
    {
        cell.Blocks =
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
        ];
    }

    public static string GetModelCellText(DocumentModel.TableCell cell)
        => string.Join(
            "\n",
            cell.Blocks
                .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
                .Select(block => block.Paragraph)
                .Where(paragraph => paragraph is not null)
                .Select(paragraph => string.Concat(paragraph!.Runs.Select(run => run.Text ?? string.Empty))));

    public static void ApplyStyleToModelCell(DocumentModel.TableCell cell, DocumentModel.TextStyleDefinition style)
    {
        var text = GetModelCellText(cell);
        cell.Blocks =
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
                            Text = text,
                            Style = style
                        }
                    ]
                }
            }
        ];
    }

    public static void ApplyCellStyleToModelCell(DocumentModel.TableCell cell, DocumentModel.CellStyleDefinition style)
    {
        cell.CellStyle ??= new DocumentModel.CellStyleDefinition();
        if (!string.IsNullOrWhiteSpace(style.BackgroundColorHex))
        {
            cell.CellStyle.BackgroundColorHex = style.BackgroundColorHex;
        }

        if (style.Border is not null)
        {
            cell.CellStyle.Border = MergeBorder(cell.CellStyle.Border, style.Border);
        }


        cell.CellStyle.PaddingLeft = style.PaddingLeft ?? cell.CellStyle.PaddingLeft;
        cell.CellStyle.PaddingRight = style.PaddingRight ?? cell.CellStyle.PaddingRight;
        cell.CellStyle.PaddingTop = style.PaddingTop ?? cell.CellStyle.PaddingTop;
        cell.CellStyle.PaddingBottom = style.PaddingBottom ?? cell.CellStyle.PaddingBottom;
        if (!string.IsNullOrWhiteSpace(style.PaddingUnit))
        {
            cell.CellStyle.PaddingUnit = style.PaddingUnit;
        }

        cell.CellStyle.HorizontalAlignment = string.IsNullOrWhiteSpace(style.HorizontalAlignment)
            ? cell.CellStyle.HorizontalAlignment
            : style.HorizontalAlignment;
        cell.CellStyle.VerticalAlignment = string.IsNullOrWhiteSpace(style.VerticalAlignment)
            ? cell.CellStyle.VerticalAlignment
            : style.VerticalAlignment;
    }

    private static DocumentModel.CellBorderDefinition MergeBorder(
        DocumentModel.CellBorderDefinition? existing,
        DocumentModel.CellBorderDefinition overlay)
        => new()
        {
            Width = overlay.Width ?? existing?.Width,
            ColorHex = overlay.ColorHex ?? existing?.ColorHex,
            Left = MergeBorderSide(existing?.Left, overlay.Left),
            Top = MergeBorderSide(existing?.Top, overlay.Top),
            Right = MergeBorderSide(existing?.Right, overlay.Right),
            Bottom = MergeBorderSide(existing?.Bottom, overlay.Bottom)
        };

    private static DocumentModel.CellBorderSideDefinition? MergeBorderSide(
        DocumentModel.CellBorderSideDefinition? existing,
        DocumentModel.CellBorderSideDefinition? overlay)
    {
        if (existing is null && overlay is null)
        {
            return null;
        }

        return new DocumentModel.CellBorderSideDefinition
        {
            Width = overlay?.Width ?? existing?.Width,
            ColorHex = overlay?.ColorHex ?? existing?.ColorHex
        };
    }

    public static void ApplyDefaultCellTextFormatting(
        ServerTextControl tx,
        TableCell cell,
        DocumentModel.TextStyleDefinition defaultTextStyle,
        string? defaultParagraphStyleName)
    {
        cell.Select();
        var selection = tx.Selection;
        var styleName = ResolveDefaultParagraphStyleName(defaultTextStyle, defaultParagraphStyleName);
        if (!string.IsNullOrWhiteSpace(styleName))
        {
            defaultTextStyle.Name = styleName;
            DocumentOperationFormatter.EnsureParagraphStyle(tx, defaultTextStyle);
            selection.FormattingStyle = styleName;
        }

        if (!string.IsNullOrEmpty(cell.Text))
        {
            DocumentOperationFormatter.ApplyStyle(selection, defaultTextStyle);
        }

        tx.Selection = selection;
    }

    private static string? ResolveDefaultParagraphStyleName(
        DocumentModel.TextStyleDefinition defaultTextStyle,
        string? defaultParagraphStyleName)
    {
        if (!string.IsNullOrWhiteSpace(defaultParagraphStyleName))
        {
            return defaultParagraphStyleName.Trim();
        }

        return string.IsNullOrWhiteSpace(defaultTextStyle.Name)
            ? null
            : defaultTextStyle.Name.Trim();
    }

    public static List<string> NormalizeRowValues(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count != 1)
        {
            throw new ArgumentException("rows must contain exactly one row for add_table_row.");
        }

        if (rows[0].Count == 0)
        {
            throw new ArgumentException("rows[0] must contain at least one cell.");
        }

        return rows[0].Select(value => value ?? string.Empty).ToList();
    }
}
