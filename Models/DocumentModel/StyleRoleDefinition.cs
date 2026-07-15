using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class StyleRoleDefinition
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Title";

    [JsonPropertyName("heading1")]
    public string Heading1 { get; set; } = "Heading";

    [JsonPropertyName("heading2")]
    public string Heading2 { get; set; } = "Heading2";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "Body";
}
