using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Focused request for formatting table cells resolved by semantic scope or editor selection.</summary>
public sealed class FormatTableRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("scope"), JsonRequired]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("tableId")]
    public string? TableId { get; set; }

    /// <summary>One-based table number in document order.</summary>
    [JsonPropertyName("tableNumber")]
    public int? TableNumber { get; set; }

    [JsonPropertyName("rowIndex")]
    public int? RowIndex { get; set; }

    [JsonPropertyName("columnIndex")]
    public int? ColumnIndex { get; set; }

    [JsonPropertyName("matchText")]
    public string? MatchText { get; set; }

    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }

    [JsonPropertyName("nearTextPosition")]
    public int? NearTextPosition { get; set; }

    [JsonPropertyName("selectionLength")]
    public int? SelectionLength { get; set; }

    [JsonPropertyName("matchCase")]
    public bool MatchCase { get; set; }

    [JsonPropertyName("bold")]
    public bool? Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool? Italic { get; set; }

    [JsonPropertyName("underline")]
    public bool? Underline { get; set; }

    [JsonPropertyName("textColorHex")]
    public string? TextColorHex { get; set; }

    [JsonPropertyName("fontName")]
    public string? FontName { get; set; }

    [JsonPropertyName("fontSize")]
    public float? FontSize { get; set; }

    [JsonPropertyName("fontSizeUnit")]
    public string FontSizeUnit { get; set; } = "pt";

    [JsonPropertyName("backgroundColorHex")]
    public string? BackgroundColorHex { get; set; }

    [JsonPropertyName("borderWidth")]
    public int? BorderWidth { get; set; }

    [JsonPropertyName("borderColorHex")]
    public string? BorderColorHex { get; set; }

    [JsonPropertyName("paddingLeft")]
    public float? PaddingLeft { get; set; }

    [JsonPropertyName("paddingRight")]
    public float? PaddingRight { get; set; }

    [JsonPropertyName("paddingTop")]
    public float? PaddingTop { get; set; }

    [JsonPropertyName("paddingBottom")]
    public float? PaddingBottom { get; set; }

    [JsonPropertyName("paddingUnit")]
    public string PaddingUnit { get; set; } = "pt";

    [JsonPropertyName("horizontalAlignment")]
    public string? HorizontalAlignment { get; set; }

    [JsonPropertyName("verticalAlignment")]
    public string? VerticalAlignment { get; set; }
}
