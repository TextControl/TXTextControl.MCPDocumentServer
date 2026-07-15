using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class TableCell
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("blocks")]
    public List<DocumentBlock> Blocks { get; set; } = [];

    [JsonPropertyName("cellStyle")]
    public CellStyleDefinition? CellStyle { get; set; }

    [JsonPropertyName("columnSpan")]
    public int ColumnSpan { get; set; } = 1;

    [JsonPropertyName("rowSpan")]
    public int RowSpan { get; set; } = 1;
}
