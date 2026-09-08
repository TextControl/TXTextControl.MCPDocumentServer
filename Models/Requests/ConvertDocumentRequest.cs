using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class ConvertDocumentRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("sourceFormat")]
    public string? SourceFormat { get; set; }

    [JsonPropertyName("outputFormat"), JsonRequired]
    public string OutputFormat { get; set; } = "pdf";

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }
}
