using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class LoadFromBase64Request
{
    [JsonPropertyName("data"), JsonRequired]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("sourceFormat")]
    public string? SourceFormat { get; set; }
}
