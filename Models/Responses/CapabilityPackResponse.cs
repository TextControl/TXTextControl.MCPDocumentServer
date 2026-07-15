using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class CapabilityPackResponse
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public List<string> OperationTypes { get; set; } = [];
}
