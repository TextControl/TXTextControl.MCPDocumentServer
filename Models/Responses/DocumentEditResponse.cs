using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentEditResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public bool Changed { get; set; }
    public int EditsApplied { get; set; }
    public long Revision { get; set; }
    public IReadOnlyList<SearchTextRange> ReplacedRanges { get; set; } = [];
    public IReadOnlyList<int> ParagraphIndexes { get; set; } = [];
}
