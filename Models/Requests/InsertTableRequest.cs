using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Compact request for inserting a table into an existing document session.</summary>
public sealed class InsertTableRequest
{
    /// <summary>Existing document session to update.</summary>
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Table data represented as rows containing cell text.</summary>
    [JsonPropertyName("rows"), JsonRequired]
    public List<List<string>> Rows { get; set; } = [];

    /// <summary>Optional TX Text Control table identifier.</summary>
    [JsonPropertyName("tableId")]
    public string? TableId { get; set; }

    /// <summary>Optional configured table style name.</summary>
    [JsonPropertyName("styleName")]
    public string? StyleName { get; set; }

    /// <summary>Optional width for each table column.</summary>
    [JsonPropertyName("columnWidths")]
    public List<float?> ColumnWidths { get; set; } = [];

    /// <summary>Unit used by <see cref="ColumnWidths"/>.</summary>
    [JsonPropertyName("columnWidthUnit")]
    public string? ColumnWidthUnit { get; set; }

    /// <summary>Zero-based paragraph index used for before or after placement.</summary>
    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    /// <summary>Placement mode: end, before, or after.</summary>
    [JsonPropertyName("placement")]
    public string? Placement { get; set; }
}
