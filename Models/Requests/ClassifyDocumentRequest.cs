using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Classifies the active document into a broad business category.</summary>
public sealed class ClassifyDocumentRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;
}
