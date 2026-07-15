using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Section
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

    [JsonPropertyName("pageLayout")]
    public PageLayoutDefinition? PageLayout { get; set; }

    [JsonPropertyName("header")]
    public HeaderFooter? Header { get; set; }

    [JsonPropertyName("footer")]
    public HeaderFooter? Footer { get; set; }

    [JsonPropertyName("blocks")]
    public List<DocumentBlock> Blocks { get; set; } = [];
}
