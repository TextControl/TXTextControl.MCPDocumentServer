using System.Collections.Generic;
using System.Text.Json.Serialization;
using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class DocumentOperation
{
    [JsonPropertyName("type"), JsonRequired]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("style")]
    public TextStyleDefinition? Style { get; set; }

    [JsonPropertyName("cellStyle")]
    public CellStyleDefinition? CellStyle { get; set; }

    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

    [JsonPropertyName("newStyleName")]
    public string? NewStyleName { get; set; }

    [JsonPropertyName("replacementStyleName")]
    public string? ReplacementStyleName { get; set; }

    [JsonPropertyName("basedOn")]
    public string? BasedOn { get; set; }

    [JsonPropertyName("followingStyle")]
    public string? FollowingStyle { get; set; }

    [JsonPropertyName("styleNamePrefix")]
    public string? StyleNamePrefix { get; set; }

    [JsonPropertyName("minimumOccurrences")]
    public int? MinimumOccurrences { get; set; }

    [JsonPropertyName("includeStyledParagraphs")]
    public bool IncludeStyledParagraphs { get; set; }

    [JsonPropertyName("tableStyleName")]
    public string? TableStyleName { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphStyleDefinition? Paragraph { get; set; }

    [JsonPropertyName("pageLayout")]
    public PageLayoutDefinition? PageLayout { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("fieldId")]
    public string? FieldId { get; set; }

    [JsonPropertyName("fieldName")]
    public string? FieldName { get; set; }

    [JsonPropertyName("blockName")]
    public string? BlockName { get; set; }

    [JsonPropertyName("blockId")]
    public int? BlockId { get; set; }

    [JsonPropertyName("fieldText")]
    public string? FieldText { get; set; }

    [JsonPropertyName("formFieldType")]
    public string? FormFieldType { get; set; }

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];

    [JsonPropertyName("checked")]
    public bool? Checked { get; set; }

    [JsonPropertyName("editable")]
    public bool? Editable { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("emptyWidth")]
    public int? EmptyWidth { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("dateFormat")]
    public string? DateFormat { get; set; }

    [JsonPropertyName("typeName")]
    public string? TypeName { get; set; }

    [JsonPropertyName("parameters")]
    public List<string> Parameters { get; set; } = [];

    [JsonPropertyName("keepText")]
    public bool KeepText { get; set; } = true;

    [JsonPropertyName("placement")]
    public string? Placement { get; set; }

    [JsonPropertyName("headerFooterType")]
    public string? HeaderFooterType { get; set; }

    [JsonPropertyName("includePageNumber")]
    public bool IncludePageNumber { get; set; }

    [JsonPropertyName("sectionIndex")]
    public int? SectionIndex { get; set; }

    [JsonPropertyName("breakKind")]
    public string? BreakKind { get; set; }

    [JsonPropertyName("pageSize")]
    public string? PageSize { get; set; }

    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; }

    [JsonPropertyName("pageWidth")]
    public float? PageWidth { get; set; }

    [JsonPropertyName("pageHeight")]
    public float? PageHeight { get; set; }

    [JsonPropertyName("marginLeft")]
    public float? MarginLeft { get; set; }

    [JsonPropertyName("marginRight")]
    public float? MarginRight { get; set; }

    [JsonPropertyName("marginTop")]
    public float? MarginTop { get; set; }

    [JsonPropertyName("marginBottom")]
    public float? MarginBottom { get; set; }

    [JsonPropertyName("matchText")]
    public string? MatchText { get; set; }

    [JsonPropertyName("replacementText")]
    public string? ReplacementText { get; set; }

    [JsonPropertyName("matchCase")]
    public bool MatchCase { get; set; }

    [JsonPropertyName("wholeWord")]
    public bool WholeWord { get; set; }

    [JsonPropertyName("maxOccurrences")]
    public int? MaxOccurrences { get; set; }

    [JsonPropertyName("runs")]
    public List<Run> Runs { get; set; } = [];

    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    [JsonPropertyName("startParagraphIndex")]
    public int? StartParagraphIndex { get; set; }

    [JsonPropertyName("endParagraphIndex")]
    public int? EndParagraphIndex { get; set; }

    [JsonPropertyName("start")]
    public int? Start { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("expectedText")]
    public string? ExpectedText { get; set; }

    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }

    [JsonPropertyName("nearTextPosition")]
    public int? NearTextPosition { get; set; }

    [JsonPropertyName("replaceAll")]
    public bool ReplaceAll { get; set; }

    [JsonPropertyName("allParagraphs")]
    public bool AllParagraphs { get; set; }

    [JsonPropertyName("rowIndex")]
    public int? RowIndex { get; set; }

    [JsonPropertyName("columnIndex")]
    public int? ColumnIndex { get; set; }

    [JsonPropertyName("tableId")]
    public string? TableId { get; set; }

    [JsonPropertyName("tableNumber")]
    public int? TableNumber { get; set; }

    [JsonPropertyName("tableScope")]
    public string? TableScope { get; set; }

    [JsonPropertyName("selectionLength")]
    public int? SelectionLength { get; set; }

    [JsonPropertyName("rows")]
    public List<List<string>> Rows { get; set; } = [];

    [JsonPropertyName("columnWidths")]
    public List<float?> ColumnWidths { get; set; } = [];

    [JsonPropertyName("columnWidthUnit")]
    public string? ColumnWidthUnit { get; set; }

    [JsonPropertyName("imagePath")]
    public string? ImagePath { get; set; }

    [JsonPropertyName("imageBase64")]
    public string? ImageBase64 { get; set; }

    [JsonPropertyName("imageFormat")]
    public string? ImageFormat { get; set; }

    [JsonPropertyName("filterIndex")]
    public int? FilterIndex { get; set; }

    [JsonPropertyName("imageName")]
    public string? ImageName { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("altText")]
    public string? AltText { get; set; }

    [JsonPropertyName("width")]
    public float? Width { get; set; }

    [JsonPropertyName("height")]
    public float? Height { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("horizontalScaling")]
    public int? HorizontalScaling { get; set; }

    [JsonPropertyName("verticalScaling")]
    public int? VerticalScaling { get; set; }

    [JsonPropertyName("insertionMode")]
    public string? InsertionMode { get; set; }

    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    [JsonPropertyName("textPosition")]
    public int? TextPosition { get; set; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; set; }

    [JsonPropertyName("locationX")]
    public float? LocationX { get; set; }

    [JsonPropertyName("locationY")]
    public float? LocationY { get; set; }

    [JsonPropertyName("locationUnit")]
    public string? LocationUnit { get; set; }

    [JsonPropertyName("sizeable")]
    public bool? Sizeable { get; set; }

    [JsonPropertyName("moveable")]
    public bool? Moveable { get; set; }

    [JsonPropertyName("saveMode")]
    public string? SaveMode { get; set; }
}
