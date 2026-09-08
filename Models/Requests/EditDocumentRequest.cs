using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class EditDocumentRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("replacementText"), JsonRequired]
    public string ReplacementText { get; set; } = string.Empty;

    [JsonPropertyName("matchText")]
    public string? MatchText { get; set; }

    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    [JsonPropertyName("startParagraphIndex")]
    public int? StartParagraphIndex { get; set; }

    [JsonPropertyName("endParagraphIndex")]
    public int? EndParagraphIndex { get; set; }

    [JsonPropertyName("start")]
    public int? Start { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    /// <summary>
    /// Optional stale-range guard for start/length edits. The operation is rejected unless the
    /// current text in the selected range is exactly this value.
    /// </summary>
    [JsonPropertyName("expectedText")]
    public string? ExpectedText { get; set; }

    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }

    [JsonPropertyName("replaceAll")]
    public bool ReplaceAll { get; set; }

    [JsonPropertyName("matchCase")]
    public bool MatchCase { get; set; }

    [JsonPropertyName("wholeWord")]
    public bool WholeWord { get; set; }
}
