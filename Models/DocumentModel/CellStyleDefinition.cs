using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.DocumentModel;

public sealed class CellStyleDefinition
{
    [JsonPropertyName("backgroundColorHex")]
    public string? BackgroundColorHex { get; set; }

    [JsonPropertyName("border")]
    public CellBorderDefinition? Border { get; set; }

    [JsonPropertyName("paddingLeft")]
    public float? PaddingLeft { get; set; }

    [JsonPropertyName("paddingRight")]
    public float? PaddingRight { get; set; }

    [JsonPropertyName("paddingTop")]
    public float? PaddingTop { get; set; }

    [JsonPropertyName("paddingBottom")]
    public float? PaddingBottom { get; set; }

    [JsonPropertyName("paddingUnit")]
    public string PaddingUnit { get; set; } = "pt";

    [JsonPropertyName("horizontalAlignment")]
    public string? HorizontalAlignment { get; set; }

    [JsonPropertyName("verticalAlignment")]
    public string? VerticalAlignment { get; set; }
}

public sealed class CellBorderDefinition
{
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; set; }

    [JsonPropertyName("left")]
    public CellBorderSideDefinition? Left { get; set; }

    [JsonPropertyName("top")]
    public CellBorderSideDefinition? Top { get; set; }

    [JsonPropertyName("right")]
    public CellBorderSideDefinition? Right { get; set; }

    [JsonPropertyName("bottom")]
    public CellBorderSideDefinition? Bottom { get; set; }
}

public sealed class CellBorderSideDefinition
{
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; set; }
}
