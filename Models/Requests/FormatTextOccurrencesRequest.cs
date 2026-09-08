using System.Text.Json.Serialization;
namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Formats text found by TX Text Control without exposing character offsets to the caller.</summary>
public sealed class FormatTextOccurrencesRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("matchText"), JsonRequired]
    public string MatchText { get; set; } = string.Empty;

    [JsonPropertyName("matchCase")]
    public bool MatchCase { get; set; }

    [JsonPropertyName("wholeWord")]
    public bool WholeWord { get; set; }

    [JsonPropertyName("maxOccurrences")]
    public int? MaxOccurrences { get; set; }

    [JsonPropertyName("bold")]
    public bool? Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool? Italic { get; set; }

    [JsonPropertyName("underline")]
    public bool? Underline { get; set; }

    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; set; }

    [JsonPropertyName("fontName")]
    public string? FontName { get; set; }

    [JsonPropertyName("fontSize")]
    public float? FontSize { get; set; }

    [JsonPropertyName("fontSizeUnit")]
    public string FontSizeUnit { get; set; } = "pt";
}
