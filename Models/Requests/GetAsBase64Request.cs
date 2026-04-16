using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class GetAsBase64Request
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = "docx";
}
