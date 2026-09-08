using System.Collections.Generic;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Models;

public sealed record DocumentEditEngineResult(
    DocumentState State,
    string TargetKind,
    int EditsApplied,
    IReadOnlyList<SearchTextRange> ReplacedRanges,
    IReadOnlyList<int> ParagraphIndexes);
