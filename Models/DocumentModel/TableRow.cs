using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class TableRow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("cells")]
    public List<TableCell> Cells { get; set; } = [];
}
