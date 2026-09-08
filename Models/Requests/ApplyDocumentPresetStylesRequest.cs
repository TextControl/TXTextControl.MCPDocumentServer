using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Identifies an existing document session whose configured presets should be applied.</summary>
public sealed class ApplyDocumentPresetStylesRequest
{
    /// <summary>Gets or sets the existing MCP document session identifier.</summary>
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;
}
