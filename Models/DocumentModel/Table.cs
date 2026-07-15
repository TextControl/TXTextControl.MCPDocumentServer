using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Table
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

    [JsonPropertyName("columnWidths")]
    public List<float?> ColumnWidths { get; set; } = [];

    [JsonPropertyName("columnWidthUnit")]
    public string? ColumnWidthUnit { get; set; }

    [JsonPropertyName("rows")]
    public List<TableRow> Rows { get; set; } = [];
}
