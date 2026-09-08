using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentInspectionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public int TotalParagraphs { get; set; }
    public string? Query { get; set; }
    public int MatchCount { get; set; }
    public IReadOnlyList<int> MatchParagraphIndexes { get; set; } = [];
    public IReadOnlyList<IndexedParagraphResponse> Paragraphs { get; set; } = [];
    public int ReturnedCharacters { get; set; }
    public bool Truncated { get; set; }
    public int? NextParagraphIndex { get; set; }
}

public sealed class IndexedParagraphResponse
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? StyleName { get; set; }
}
