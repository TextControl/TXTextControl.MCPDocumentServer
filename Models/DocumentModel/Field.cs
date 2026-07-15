using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Field
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new();
}
