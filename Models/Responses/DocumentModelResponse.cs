using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentModelResponse
{
    public string SessionId { get; set; } = string.Empty;
    public Document Document { get; set; } = new();
}
