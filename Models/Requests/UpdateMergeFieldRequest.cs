using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Updates existing real MERGEFIELD ApplicationFields by name.</summary>
public sealed class UpdateMergeFieldRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("fieldName"), JsonRequired]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("fieldText")]
    public string? FieldText { get; set; }

    [JsonPropertyName("parameters")]
    public List<string> Parameters { get; set; } = [];
}
