using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Replaces the body of a previously inspected document section.</summary>
public sealed class ReplaceDocumentSectionRequest
{
    /// <summary>Existing document session.</summary>
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Visible section heading returned by inspect_document_section.</summary>
    [JsonPropertyName("heading"), JsonRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>Content hash returned by inspect_document_section.</summary>
    [JsonPropertyName("expectedContentHash"), JsonRequired]
    public string ExpectedContentHash { get; set; } = string.Empty;

    /// <summary>New section body. The heading itself is preserved.</summary>
    [JsonPropertyName("replacementText"), JsonRequired]
    public string ReplacementText { get; set; } = string.Empty;

    /// <summary>Zero-based occurrence when the same heading appears more than once.</summary>
    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }
}
