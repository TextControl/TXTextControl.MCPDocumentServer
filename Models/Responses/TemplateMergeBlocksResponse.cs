using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class TemplateMergeBlocksResponse
{
    public string SessionId { get; set; } = string.Empty;
    public int BlockCount { get; set; }
    public IReadOnlyList<TemplateMergeBlockInfo> Blocks { get; set; } = [];
}
