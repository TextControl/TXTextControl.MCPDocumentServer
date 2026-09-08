using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

/// <summary>Adds one or more rows to a table selected by id or document order.</summary>
public sealed class AddTableRowsRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Authoritative table id returned by get_document_tables.</summary>
    [JsonPropertyName("tableId")]
    public string? TableId { get; set; }

    /// <summary>One-based table number in document order, for example 2 for the second table.</summary>
    [JsonPropertyName("tableNumber")]
    public int? TableNumber { get; set; }

    /// <summary>Number of empty rows to add when rows is omitted.</summary>
    [JsonPropertyName("count")]
    public int? Count { get; set; }

    /// <summary>Optional row data. Each inner array is one new row.</summary>
    [JsonPropertyName("rows")]
    public List<List<string>> Rows { get; set; } = [];
}
