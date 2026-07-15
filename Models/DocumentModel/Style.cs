using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Style
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "paragraph";

    [JsonPropertyName("basedOn")]
    public string? BasedOn { get; set; }

    [JsonPropertyName("text")]
    public TextStyleDefinition? Text { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphStyleDefinition? Paragraph { get; set; }
}
