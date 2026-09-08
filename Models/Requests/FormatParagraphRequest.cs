using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Focused request for applying paragraph-level formatting or a named style.</summary>
public sealed class FormatParagraphRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    [JsonPropertyName("startParagraphIndex")]
    public int? StartParagraphIndex { get; set; }

    [JsonPropertyName("endParagraphIndex")]
    public int? EndParagraphIndex { get; set; }

    [JsonPropertyName("matchText")]
    public string? MatchText { get; set; }

    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }

    [JsonPropertyName("nearTextPosition")]
    public int? NearTextPosition { get; set; }

    [JsonPropertyName("allMatches")]
    public bool AllMatches { get; set; }

    [JsonPropertyName("allParagraphs")]
    public bool AllParagraphs { get; set; }

    [JsonPropertyName("matchCase")]
    public bool MatchCase { get; set; }

    [JsonPropertyName("wholeWord")]
    public bool WholeWord { get; set; }

    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

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
