using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class CreateDocumentExportRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("format"), JsonRequired]
    public string Format { get; set; } = "pdf";

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }
}
