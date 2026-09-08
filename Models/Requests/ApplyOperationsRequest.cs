using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class ApplyOperationsRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("createIfMissing")]
    public bool CreateIfMissing { get; set; } = true;

    [JsonPropertyName("operations"), JsonRequired]
    public List<DocumentOperation> Operations { get; set; } = [];
}
