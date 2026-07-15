using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class PageLayoutDefinition
{
    [JsonPropertyName("pageSize")]
    public string? PageSize { get; set; }

    [JsonPropertyName("orientation")]
    public string? Orientation { get; set; }

    [JsonPropertyName("pageWidth")]
    public float? PageWidth { get; set; }

    [JsonPropertyName("pageHeight")]
    public float? PageHeight { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("marginLeft")]
    public float? MarginLeft { get; set; }

    [JsonPropertyName("marginRight")]
    public float? MarginRight { get; set; }

    [JsonPropertyName("marginTop")]
    public float? MarginTop { get; set; }

    [JsonPropertyName("marginBottom")]
    public float? MarginBottom { get; set; }
}
