using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Updates existing real TX Text Control form fields by name.</summary>
public sealed class UpdateFormFieldRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("fieldName"), JsonRequired]
    public string FieldName { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("checked")]
    public bool? Checked { get; set; }

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];

    [JsonPropertyName("editable")]
    public bool? Editable { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}
