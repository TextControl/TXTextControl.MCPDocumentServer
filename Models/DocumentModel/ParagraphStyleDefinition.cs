using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class ParagraphStyleDefinition
{
    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    [JsonPropertyName("spaceBefore")]
    public float? SpaceBefore { get; set; }

    [JsonPropertyName("spaceAfter")]
    public float? SpaceAfter { get; set; }

    [JsonPropertyName("lineSpacing")]
    public float? LineSpacing { get; set; }

    [JsonPropertyName("absoluteLineSpacing")]
    public float? AbsoluteLineSpacing { get; set; }

    [JsonPropertyName("leftIndent")]
    public float? LeftIndent { get; set; }

    [JsonPropertyName("rightIndent")]
    public float? RightIndent { get; set; }

    [JsonPropertyName("hangingIndent")]
    public float? HangingIndent { get; set; }

    [JsonPropertyName("backgroundColorHex")]
    public string? BackgroundColorHex { get; set; }

    [JsonPropertyName("keepLinesTogether")]
    public bool? KeepLinesTogether { get; set; }

    [JsonPropertyName("keepWithNext")]
    public bool? KeepWithNext { get; set; }

    [JsonPropertyName("pageBreakBefore")]
    public bool? PageBreakBefore { get; set; }

    [JsonPropertyName("widowOrphanLines")]
    public int? WidowOrphanLines { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "pt";
}
