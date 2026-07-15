using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class TextStyleDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fontName")]
    public string? FontName { get; set; }

    [JsonPropertyName("fontSize")]
    public float? FontSize { get; set; }

    [JsonPropertyName("fontSizeUnit")]
    public string FontSizeUnit { get; set; } = "pt";

    [JsonPropertyName("bold")]
    public bool? Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool? Italic { get; set; }

    [JsonPropertyName("underline")]
    public bool? Underline { get; set; }

    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphStyleDefinition? Paragraph { get; set; }
}
