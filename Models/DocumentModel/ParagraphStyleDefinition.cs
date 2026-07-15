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

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "pt";
}
