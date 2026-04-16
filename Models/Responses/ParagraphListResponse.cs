using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class ParagraphListResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = [];
}
