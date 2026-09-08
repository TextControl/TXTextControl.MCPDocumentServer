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

    [JsonPropertyName("strikeout")]
    public bool? Strikeout { get; set; }

    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; set; }

    [JsonPropertyName("backgroundColorHex")]
    public string? BackgroundColorHex { get; set; }

    [JsonPropertyName("characterSpacing")]
    public float? CharacterSpacing { get; set; }

    [JsonPropertyName("characterScaling")]
    public int? CharacterScaling { get; set; }

    [JsonPropertyName("baseline")]
    public float? Baseline { get; set; }

    [JsonPropertyName("capitals")]
    public string? Capitals { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphStyleDefinition? Paragraph { get; set; }
}
