using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Removes field markup from a document session.</summary>
public sealed class ClearFieldsRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("keepText")]
    public bool KeepText { get; set; } = true;
}
