namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentTextResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
