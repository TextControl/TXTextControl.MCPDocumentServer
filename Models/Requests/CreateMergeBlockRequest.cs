using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Wraps an existing table row, paragraph range, or character range as a repeating merge block.</summary>
public sealed class CreateMergeBlockRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("blockName"), JsonRequired]
    public string BlockName { get; set; } = string.Empty;

    [JsonPropertyName("blockId")]
    public int? BlockId { get; set; }

    [JsonPropertyName("tableId")]
    public string? TableId { get; set; }

    [JsonPropertyName("rowIndex")]
    public int? RowIndex { get; set; }

    [JsonPropertyName("start")]
    public int? Start { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("expectedText")]
    public string? ExpectedText { get; set; }

    [JsonPropertyName("startParagraphIndex")]
    public int? StartParagraphIndex { get; set; }

    [JsonPropertyName("endParagraphIndex")]
    public int? EndParagraphIndex { get; set; }
}
