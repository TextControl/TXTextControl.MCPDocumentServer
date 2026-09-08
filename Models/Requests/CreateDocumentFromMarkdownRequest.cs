using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Contains semantic Markdown for creating a preset-styled document.</summary>
public sealed class CreateDocumentFromMarkdownRequest
{
    /// <summary>Gets or sets the Markdown document content.</summary>
    [JsonPropertyName("markdown"), JsonRequired]
    public string Markdown { get; set; } = string.Empty;
}
