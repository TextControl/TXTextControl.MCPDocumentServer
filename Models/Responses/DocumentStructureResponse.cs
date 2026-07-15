using System.Collections.Generic;
using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentStructureResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int SectionCount { get; set; }
    public int ParagraphCount { get; set; }
    public int TableCount { get; set; }
    public int ImageCount { get; set; }
    public int FieldCount { get; set; }
    public int HeaderFooterCount { get; set; }
    public List<DocumentSectionInspection> Sections { get; set; } = [];
}

public sealed class DocumentSectionInspection
{
    public int SectionIndex { get; set; }
    public string Id { get; set; } = string.Empty;
    public string? StyleName { get; set; }
    public PageLayoutDefinition? PageLayout { get; set; }
    public HeaderFooterInspection? Header { get; set; }
    public HeaderFooterInspection? Footer { get; set; }
    public List<DocumentBlockInspection> Blocks { get; set; } = [];
}

public sealed class DocumentBlockInspection
{
    public int BlockIndex { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? StyleName { get; set; }
    public string? TextPreview { get; set; }
    public string? TableId { get; set; }
    public int? RowCount { get; set; }
    public int? ColumnCount { get; set; }
    public string? FieldName { get; set; }
    public string? ImageAltText { get; set; }
}

public sealed class HeaderFooterInspection
{
    public string Type { get; set; } = string.Empty;
    public string? TextPreview { get; set; }
    public List<DocumentBlockInspection> Blocks { get; set; } = [];
}

public sealed class DocumentStylesResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<StyleInspection> Styles { get; set; } = [];
}

public sealed class StyleInspection
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public TextStyleDefinition? Text { get; set; }
    public ParagraphStyleDefinition? Paragraph { get; set; }
    public CellStyleDefinition? Cell { get; set; }
}

public sealed class DocumentTablesResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<TableInspection> Tables { get; set; } = [];
}

public sealed class TableInspection
{
    public string Id { get; set; } = string.Empty;
    public int SectionIndex { get; set; }
    public int BlockIndex { get; set; }
    public string? StyleName { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<TableRowInspection> Rows { get; set; } = [];
}

public sealed class TableRowInspection
{
    public int RowIndex { get; set; }
    public string Id { get; set; } = string.Empty;
    public List<TableCellInspection> Cells { get; set; } = [];
}

public sealed class TableCellInspection
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public string Id { get; set; } = string.Empty;
    public string? TextPreview { get; set; }
    public CellStyleDefinition? CellStyle { get; set; }
    public int ColumnSpan { get; set; }
    public int RowSpan { get; set; }
    public List<string> FieldNames { get; set; } = [];
}

public sealed class DocumentFieldsResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<FieldInspection> Fields { get; set; } = [];
}

public sealed class FieldInspection
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Location { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new();
}

public sealed class DocumentHeadersFootersResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<HeaderFooterLocationInspection> HeadersFooters { get; set; } = [];
}

public sealed class HeaderFooterLocationInspection
{
    public int SectionIndex { get; set; }
    public string SectionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TextPreview { get; set; }
    public List<DocumentBlockInspection> Blocks { get; set; } = [];
}
