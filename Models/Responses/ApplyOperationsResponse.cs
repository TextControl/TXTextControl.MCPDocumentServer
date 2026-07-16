using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class ApplyOperationsResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<OperationResult> Results { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
