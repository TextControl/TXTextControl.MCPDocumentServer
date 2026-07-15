using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class Image
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("altText")]
    public string? AltText { get; set; }

    [JsonPropertyName("width")]
    public float? Width { get; set; }

    [JsonPropertyName("height")]
    public float? Height { get; set; }

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "px";

    [JsonPropertyName("horizontalScaling")]
    public int? HorizontalScaling { get; set; }

    [JsonPropertyName("verticalScaling")]
    public int? VerticalScaling { get; set; }

    [JsonPropertyName("insertionMode")]
    public string? InsertionMode { get; set; }

    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    [JsonPropertyName("locationX")]
    public float? LocationX { get; set; }

    [JsonPropertyName("locationY")]
    public float? LocationY { get; set; }

    [JsonPropertyName("locationUnit")]
    public string? LocationUnit { get; set; }
}
