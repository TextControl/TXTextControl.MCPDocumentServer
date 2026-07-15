namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentAutomationCapability
{
    public string Type { get; set; } = string.Empty;
    public string CapabilityPack { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
