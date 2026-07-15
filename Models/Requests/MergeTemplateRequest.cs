using System.Text.Json;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class MergeTemplateRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("jsonData")]
    public string? JsonData { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("append")]
    public bool Append { get; set; }

    [JsonPropertyName("removeEmptyFields")]
    public bool RemoveEmptyFields { get; set; } = true;

    [JsonPropertyName("removeEmptyBlocks")]
    public bool RemoveEmptyBlocks { get; set; } = true;

    [JsonPropertyName("removeEmptyImages")]
    public bool RemoveEmptyImages { get; set; } = true;

    [JsonPropertyName("removeEmptyLines")]
    public bool RemoveEmptyLines { get; set; } = true;

    [JsonPropertyName("removeTrailingWhitespace")]
    public bool RemoveTrailingWhitespace { get; set; } = true;

    [JsonPropertyName("formFieldMergeType")]
    public string? FormFieldMergeType { get; set; }
}
