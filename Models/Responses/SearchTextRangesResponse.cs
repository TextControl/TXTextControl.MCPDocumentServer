using System.Collections.Generic;
using TxTextControl.McpServer.Models;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class SearchTextRangesResponse
{
    public string SessionId { get; set; } = string.Empty;
    public List<SearchTextRange> Matches { get; set; } = [];
}
