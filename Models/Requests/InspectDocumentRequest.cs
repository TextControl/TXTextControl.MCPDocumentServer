using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class InspectDocumentRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("startParagraphIndex")]
    public int? StartParagraphIndex { get; set; }

    [JsonPropertyName("paragraphCount")]
    public int? ParagraphCount { get; set; }

    [JsonPropertyName("contextParagraphs")]
    public int ContextParagraphs { get; set; } = 1;

    [JsonPropertyName("maxCharacters")]
    public int MaxCharacters { get; set; } = 8000;
}
