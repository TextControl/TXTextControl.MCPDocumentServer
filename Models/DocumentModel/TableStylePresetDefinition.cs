using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class TableStylePresetDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("headerRowIndex")]
    public int HeaderRowIndex { get; set; }

    [JsonPropertyName("headerStyle")]
    public TextStyleDefinition? HeaderStyle { get; set; }

    [JsonPropertyName("headerCellStyle")]
    public CellStyleDefinition? HeaderCellStyle { get; set; }

    [JsonPropertyName("bodyStyle")]
    public TextStyleDefinition? BodyStyle { get; set; }

    [JsonPropertyName("bodyCellStyle")]
    public CellStyleDefinition? BodyCellStyle { get; set; }

    [JsonPropertyName("alternatingRowStyle")]
    public TextStyleDefinition? AlternatingRowStyle { get; set; }

    [JsonPropertyName("alternatingRowCellStyle")]
    public CellStyleDefinition? AlternatingRowCellStyle { get; set; }
}
