using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class DocumentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("paragraph")]
    public Paragraph? Paragraph { get; set; }

    [JsonPropertyName("table")]
    public Table? Table { get; set; }

    [JsonPropertyName("image")]
    public Image? Image { get; set; }

    [JsonPropertyName("field")]
    public Field? Field { get; set; }
}
