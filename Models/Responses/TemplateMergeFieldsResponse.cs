using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class TemplateMergeFieldsResponse
{
    public string SessionId { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public IReadOnlyList<TemplateMergeFieldInfo> Fields { get; set; } = [];
}
