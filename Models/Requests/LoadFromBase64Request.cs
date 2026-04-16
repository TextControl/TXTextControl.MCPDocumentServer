using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class LoadFromBase64Request
{
    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}
