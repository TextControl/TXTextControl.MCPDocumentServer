using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Resolves a document section from its visible heading.</summary>
public sealed class InspectDocumentSectionRequest
{
    /// <summary>Existing document session.</summary>
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Visible section heading, with numbering optional.</summary>
    [JsonPropertyName("heading"), JsonRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>Zero-based occurrence when the same heading appears more than once.</summary>
    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }
}
