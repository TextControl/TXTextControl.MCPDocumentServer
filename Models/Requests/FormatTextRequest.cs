using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class FormatTextRequest
{
    [JsonPropertyName("start")]
    public int? Start { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    [JsonPropertyName("bold")]
    public bool Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool Italic { get; set; }

    [JsonPropertyName("underline")]
    public bool Underline { get; set; }

    [JsonPropertyName("color_hex")]
    public string? ColorHex { get; set; }

    [JsonPropertyName("font_name")]
    public string? FontName { get; set; }

    [JsonPropertyName("font_size")]
    public float? FontSize { get; set; }
}
