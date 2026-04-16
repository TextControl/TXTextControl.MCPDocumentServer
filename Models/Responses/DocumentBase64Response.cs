namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentBase64Response
{
    public string SessionId { get; set; } = string.Empty;
    public string Format { get; set; } = "docx";
    public string Base64Document { get; set; } = string.Empty;
}
