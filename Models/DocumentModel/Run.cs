using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Run
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

    [JsonPropertyName("style")]
    public TextStyleDefinition? Style { get; set; }
}
