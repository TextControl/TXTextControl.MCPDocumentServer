using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class SearchTextResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<int> Matches { get; set; } = [];
}
