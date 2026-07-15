using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class TemplateMergeFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public IReadOnlyList<string> Parameters { get; set; } = [];
}
