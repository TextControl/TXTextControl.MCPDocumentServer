using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class OperationResult
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "applied";
    public string? Detail { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Location { get; set; }
    public Dictionary<string, object?> Metadata { get; set; } = new();
}
