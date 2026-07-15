using System.Text.Json.Serialization;
using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class RenderDocumentModelRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("createIfMissing")]
    public bool CreateIfMissing { get; set; } = true;

    [JsonPropertyName("document")]
    public Document? Document { get; set; }
}
