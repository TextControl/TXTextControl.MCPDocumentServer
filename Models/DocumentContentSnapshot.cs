using System.Collections.Generic;

namespace TxTextControl.McpServer.Models;

/// <summary>
/// Immutable text content extracted from one document revision.
/// </summary>
public sealed record DocumentContentSnapshot(
    string Text,
    IReadOnlyList<string> Paragraphs)
{
    /// <summary>Paragraph text plus structural formatting metadata in document order.</summary>
    public IReadOnlyList<DocumentParagraphSnapshot> ParagraphDetails { get; init; } = [];

    /// <summary>Tables read directly from the authoritative TX document in document order.</summary>
    public IReadOnlyList<DocumentTableSnapshot> Tables { get; init; } = [];
}

/// <summary>Immutable structural metadata for one document paragraph.</summary>
public sealed record DocumentParagraphSnapshot(
    string Text,
    string? StyleName);

/// <summary>Immutable table metadata extracted from the live TX document.</summary>
public sealed record DocumentTableSnapshot(
    string Id,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<DocumentTableRowSnapshot> Rows);

/// <summary>One zero-based TX table row.</summary>
public sealed record DocumentTableRowSnapshot(
    int RowIndex,
    IReadOnlyList<DocumentTableCellSnapshot> Cells);

/// <summary>One zero-based TX table cell.</summary>
public sealed record DocumentTableCellSnapshot(
    int RowIndex,
    int ColumnIndex,
    string Text,
    int Start,
    int Length);
