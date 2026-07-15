using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class HeaderFooter
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "default";

    [JsonPropertyName("blocks")]
    public List<DocumentBlock> Blocks { get; set; } = [];
}
