using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Paragraph
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("runs")]
    public List<Run> Runs { get; set; } = [];

    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    [JsonPropertyName("paragraphStyle")]
    public ParagraphStyleDefinition? ParagraphStyle { get; set; }
}
