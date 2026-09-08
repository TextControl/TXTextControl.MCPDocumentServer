using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TxTextControl.McpServer.Models.Requests;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal readonly record struct ResolvedTableCell(
    Table Table,
    int TableNumber,
    TableCell Cell,
    int RowIndex,
    int ColumnIndex);

internal readonly record struct LocatedTableOccurrence(
    ResolvedTableCell Cell,
    TextOccurrence Occurrence);

internal static class TableTargetUtilities
{
    public static List<ResolvedTableCell> ResolveFormatTargets(
        ServerTextControl tx,
        DocumentOperation operation)
    {
        string scope = string.IsNullOrWhiteSpace(operation.TableScope)
            ? "cell"
            : operation.TableScope.Trim().ToLowerInvariant();
        if (scope is not ("cell" or "selectedcells" or "header" or "row" or "column" or "table"))
        {
            throw new ArgumentException(
                "tableScope must be cell, selectedCells, header, row, column, or table.");
        }

        List<ResolvedTableCell> allCells = EnumerateCells(tx).ToList();
        if (allCells.Count == 0)
        {
            throw new InvalidOperationException("No table was found in the document.");
        }

        (Table Table, int TableNumber)? explicitTable = ResolveExplicitTable(
            tx,
            operation.TableId,
            operation.TableNumber);
        (ResolvedTableCell Cell, TextOccurrence? Match) anchor = ResolveAnchor(
            tx,
            allCells,
            operation,
            explicitTable);
        Table table = explicitTable?.Table ?? anchor.Cell.Table;
        int tableNumber = explicitTable?.TableNumber ?? anchor.Cell.TableNumber;
        List<ResolvedTableCell> tableCells = allCells
            .Where(target => target.TableNumber == tableNumber)
            .ToList();

        return scope switch
        {
            "cell" => ResolveSingleCell(tableCells, operation, anchor.Cell),
            "selectedcells" => ResolveSelectedCells(tableCells, operation, anchor),
            "header" => ResolveRow(tableCells, operation.RowIndex ?? 0),
            "row" => ResolveRow(
                tableCells,
                operation.RowIndex ?? anchor.Cell.RowIndex),
            "column" => ResolveColumn(
                tableCells,
                operation.ColumnIndex ?? anchor.Cell.ColumnIndex),
            "table" => tableCells,
            _ => throw new InvalidOperationException("Unsupported table scope.")
        };
    }

    private static List<ResolvedTableCell> ResolveSingleCell(
        IReadOnlyList<ResolvedTableCell> tableCells,
        DocumentOperation operation,
        ResolvedTableCell anchor)
    {
        if (!operation.RowIndex.HasValue && !operation.ColumnIndex.HasValue)
        {
            return [anchor];
        }

        if (!operation.RowIndex.HasValue || !operation.ColumnIndex.HasValue)
        {
            throw new ArgumentException("Both rowIndex and columnIndex are required for tableScope cell.");
        }

        return
        [
            tableCells.FirstOrDefault(target =>
                target.RowIndex == operation.RowIndex.Value
                && target.ColumnIndex == operation.ColumnIndex.Value) switch
            {
                { Cell: not null } target => target,
                _ => throw new ArgumentException("The requested table cell is out of range.")
            }
        ];
    }

    private static List<ResolvedTableCell> ResolveSelectedCells(
        IReadOnlyList<ResolvedTableCell> tableCells,
        DocumentOperation operation,
        (ResolvedTableCell Cell, TextOccurrence? Match) anchor)
    {
        if (anchor.Match.HasValue)
        {
            int rangeStart = anchor.Match.Value.Start;
            int rangeLength = Math.Max(
                anchor.Match.Value.Length,
                Math.Max(0, operation.SelectionLength ?? 0));
            int rangeEnd = rangeStart + rangeLength;
            List<ResolvedTableCell> overlaps = tableCells
                .Where(target => Overlaps(target.Cell, rangeStart, rangeEnd))
                .ToList();
            if (overlaps.Count > 0)
            {
                return overlaps;
            }
        }

        string selectedText = NormalizeWhitespace(operation.MatchText);
        if (selectedText.Length > 0)
        {
            List<ResolvedTableCell> contentMatches = tableCells
                .Where(target =>
                {
                    string cellText = NormalizeWhitespace(target.Cell.Text);
                    return cellText.Length > 0
                           && selectedText.Contains(cellText, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            if (contentMatches.Count > 0)
            {
                return contentMatches;
            }
        }

        return [anchor.Cell];
    }

    private static List<ResolvedTableCell> ResolveRow(
        IReadOnlyList<ResolvedTableCell> tableCells,
        int rowIndex)
    {
        if (rowIndex < 0)
        {
            throw new ArgumentException("rowIndex must be >= 0.");
        }

        List<ResolvedTableCell> targets = tableCells
            .Where(target => target.RowIndex == rowIndex)
            .ToList();
        return targets.Count > 0
            ? targets
            : throw new ArgumentException("rowIndex is out of range.");
    }

    private static List<ResolvedTableCell> ResolveColumn(
        IReadOnlyList<ResolvedTableCell> tableCells,
        int columnIndex)
    {
        if (columnIndex < 0)
        {
            throw new ArgumentException("columnIndex must be >= 0.");
        }

        List<ResolvedTableCell> targets = tableCells
            .Where(target => target.ColumnIndex == columnIndex)
            .ToList();
        return targets.Count > 0
            ? targets
            : throw new ArgumentException("columnIndex is out of range.");
    }

    private static (ResolvedTableCell Cell, TextOccurrence? Match) ResolveAnchor(
        ServerTextControl tx,
        IReadOnlyList<ResolvedTableCell> allCells,
        DocumentOperation operation,
        (Table Table, int TableNumber)? explicitTable)
    {
        IReadOnlyList<ResolvedTableCell> candidateCells = explicitTable is null
            ? allCells
            : allCells.Where(target => target.TableNumber == explicitTable.Value.TableNumber).ToList();

        if (!string.IsNullOrWhiteSpace(operation.MatchText))
        {
            string searchText = operation.MatchText!;
            List<TextOccurrence> occurrences = FindOccurrences(tx, operation, searchText);
            if (occurrences.Count == 0)
            {
                string trimmed = searchText.Trim();
                if (trimmed.Length > 0 && !trimmed.Equals(searchText, StringComparison.Ordinal))
                {
                    searchText = trimmed;
                    occurrences = FindOccurrences(tx, operation, searchText);
                }
            }

            List<LocatedTableOccurrence> located = occurrences
                .Select(occurrence => new LocatedTableOccurrence(
                    candidateCells.FirstOrDefault(target => Contains(target.Cell, occurrence.Start)),
                    occurrence))
                .Where(value => value.Cell.Cell is not null)
                .ToList();
            if (located.Count > 0)
            {
                var chosen = ChooseLocatedOccurrence(located, operation);
                return (chosen.Cell, chosen.Occurrence);
            }

            ResolvedTableCell? scored = ResolveByCellContent(candidateCells, operation.MatchText, operation.NearTextPosition);
            if (scored.HasValue)
            {
                return (scored.Value, null);
            }

            throw new ArgumentException(
                $"Selected text was not found inside a table. Inspect the current document and retry with exact cell text.");
        }

        if (operation.RowIndex.HasValue && operation.ColumnIndex.HasValue && explicitTable is not null)
        {
            ResolvedTableCell target = candidateCells.FirstOrDefault(value =>
                value.RowIndex == operation.RowIndex.Value
                && value.ColumnIndex == operation.ColumnIndex.Value);
            if (target.Cell is not null)
            {
                return (target, null);
            }
        }

        if (explicitTable is not null)
        {
            return (candidateCells.First(), null);
        }

        throw new ArgumentException(
            "Specify tableId or matchText from the selected table so the target table can be resolved.");
    }

    private static LocatedTableOccurrence ChooseLocatedOccurrence(
        IReadOnlyList<LocatedTableOccurrence> located,
        DocumentOperation operation)
    {
        if (operation.OccurrenceIndex.HasValue)
        {
            int index = operation.OccurrenceIndex.Value;
            if (index < 0 || index >= located.Count)
            {
                throw new ArgumentException(
                    $"occurrenceIndex is out of range. Found {located.Count} table occurrence(s), indexed from 0.");
            }

            return located[index];
        }

        if (operation.NearTextPosition.HasValue)
        {
            int hint = operation.NearTextPosition.Value;
            if (hint < 0)
            {
                throw new ArgumentException("nearTextPosition must be >= 0.");
            }

            return located
                .OrderBy(value => Math.Abs((long)value.Occurrence.Start - hint))
                .ThenBy(value => value.Occurrence.Start)
                .First();
        }

        if (located.Count == 1)
        {
            return located[0];
        }

        throw new ArgumentException(
            $"Selected text occurs in {located.Count} table locations. Set nearTextPosition or occurrenceIndex.");
    }

    private static List<TextOccurrence> FindOccurrences(
        ServerTextControl tx,
        DocumentOperation operation,
        string text) => TextOccurrenceUtilities.FindOccurrences(
            tx,
            text,
            operation.MatchCase,
            wholeWord: false,
            maxOccurrences: null);

    private static ResolvedTableCell? ResolveByCellContent(
        IReadOnlyList<ResolvedTableCell> cells,
        string? selectedText,
        int? nearTextPosition)
    {
        string normalizedSelection = NormalizeWhitespace(selectedText);
        var matches = cells
            .Where(target =>
            {
                string cellText = NormalizeWhitespace(target.Cell.Text);
                return cellText.Length > 0
                       && (normalizedSelection.Contains(cellText, StringComparison.OrdinalIgnoreCase)
                           || cellText.Contains(normalizedSelection, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        return nearTextPosition.HasValue
            ? matches.OrderBy(target => Math.Abs((long)(target.Cell.Start - 1) - nearTextPosition.Value)).First()
            : matches.Count == 1
                ? matches[0]
                : null;
    }

    internal static (Table Table, int TableNumber)? ResolveExplicitTable(
        ServerTextControl tx,
        string? tableId,
        int? tableNumber)
    {
        if (tableNumber.HasValue)
        {
            if (tableNumber.Value < 1 || tableNumber.Value > tx.Tables.Count)
            {
                throw new ArgumentException($"tableNumber must be between 1 and {tx.Tables.Count}.");
            }

            Table table = tx.Tables.Cast<Table>().ElementAt(tableNumber.Value - 1);
            return (table, tableNumber.Value);
        }

        if (string.IsNullOrWhiteSpace(tableId))
        {
            return null;
        }

        if (tableId.Trim().Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            Table table = tx.Tables.Cast<Table>().FirstOrDefault()
                          ?? throw new InvalidOperationException("No table was found.");
            return (table, 1);
        }

        int parsedId = TableOperationUtilities.RequireTableId(tableId);
        var matches = tx.Tables.Cast<Table>()
            .Select((table, index) => (Table: table, TableNumber: index + 1))
            .Where(value => value.Table.ID == parsedId)
            .ToList();
        if (matches.Count > 1)
        {
            throw new ArgumentException(
                $"Table id '{parsedId}' is not unique in this imported document. Use tableNumber from get_document_tables.");
        }

        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Table '{parsedId}' was not found.");
    }

    private static IEnumerable<ResolvedTableCell> EnumerateCells(ServerTextControl tx)
    {
        int tableNumber = 0;
        foreach (Table table in tx.Tables)
        {
            tableNumber++;
            foreach (TableCell cell in table.Cells)
            {
                yield return new ResolvedTableCell(table, tableNumber, cell, cell.Row - 1, cell.Column - 1);
            }
        }
    }

    private static bool Contains(TableCell cell, int zeroBasedPosition)
    {
        int start = cell.Start - 1;
        return zeroBasedPosition >= start && zeroBasedPosition < start + cell.Length;
    }

    private static bool Overlaps(TableCell cell, int rangeStart, int rangeEnd)
    {
        int cellStart = cell.Start - 1;
        int cellEnd = cellStart + cell.Length;
        return rangeStart < cellEnd && rangeEnd > cellStart;
    }

    private static string NormalizeWhitespace(string? value)
        => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
