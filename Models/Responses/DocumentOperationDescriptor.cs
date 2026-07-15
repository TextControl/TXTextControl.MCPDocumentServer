using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentOperationDescriptor
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("capabilityPack")]
    public string CapabilityPack { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    [JsonPropertyName("requiredProperties")]
    public List<string> RequiredProperties { get; set; } = [];

    [JsonPropertyName("optionalProperties")]
    public List<string> OptionalProperties { get; set; } = [];

    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();

    [JsonPropertyName("example")]
    public Dictionary<string, object?> Example { get; set; } = new();

    [JsonPropertyName("modelEffects")]
    public List<string> ModelEffects { get; set; } = [];

    [JsonPropertyName("requiresTxExecution")]
    public bool RequiresTxExecution { get; set; } = true;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}
