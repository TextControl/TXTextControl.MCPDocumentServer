using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Document
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("styles")]
    public List<Style> Styles { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<Section> Sections { get; set; } = [];

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}
