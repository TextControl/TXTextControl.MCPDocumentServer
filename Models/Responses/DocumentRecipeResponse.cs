using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentRecipeSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;
}

public sealed class DocumentRecipeResponse
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("recipeName")]
    public string RecipeName { get; set; } = string.Empty;

    [JsonPropertyName("operationCount")]
    public int OperationCount { get; set; }

    [JsonPropertyName("mergeFields")]
    public IReadOnlyList<string> MergeFields { get; set; } = [];

    [JsonPropertyName("repeatingBlocks")]
    public IReadOnlyList<string> RepeatingBlocks { get; set; } = [];

    [JsonPropertyName("formFields")]
    public IReadOnlyList<string> FormFields { get; set; } = [];

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
