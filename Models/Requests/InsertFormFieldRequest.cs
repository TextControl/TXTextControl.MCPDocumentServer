using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Inserts a real TX Text Control form field at a deterministic body or table location.</summary>
public sealed class InsertFormFieldRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("fieldName"), JsonRequired]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("formFieldType"), JsonRequired]
    public string FormFieldType { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("checked")]
    public bool? Checked { get; set; }

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];

    [JsonPropertyName("editable")]
    public bool? Editable { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("emptyWidth")]
    public int? EmptyWidth { get; set; }

    [JsonPropertyName("matchText")]
    public string? MatchText { get; set; }

    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }

    [JsonPropertyName("nearTextPosition")]
    public int? NearTextPosition { get; set; }

    [JsonPropertyName("replaceAll")]
    public bool ReplaceAll { get; set; }

    [JsonPropertyName("matchCase")]
    public bool MatchCase { get; set; }

    [JsonPropertyName("wholeWord")]
    public bool WholeWord { get; set; }

    [JsonPropertyName("start")]
    public int? Start { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("expectedText")]
    public string? ExpectedText { get; set; }

    [JsonPropertyName("textPosition")]
    public int? TextPosition { get; set; }

    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    [JsonPropertyName("tableId")]
    public string? TableId { get; set; }

    [JsonPropertyName("rowIndex")]
    public int? RowIndex { get; set; }

    [JsonPropertyName("columnIndex")]
    public int? ColumnIndex { get; set; }

    [JsonPropertyName("headerFooterType")]
    public string? HeaderFooterType { get; set; }

    [JsonPropertyName("sectionIndex")]
    public int? SectionIndex { get; set; }

    [JsonPropertyName("placement")]
    public string? Placement { get; set; }
}
