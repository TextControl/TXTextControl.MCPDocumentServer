using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class MergeTemplateResponse
{
    public string SessionId { get; set; } = string.Empty;
    public int MergedFieldCount { get; set; }
    public int RemainingFieldCount { get; set; }
    public int MergedFormFieldCount { get; set; }
    public int RemainingFormFieldCount { get; set; }
    public IReadOnlyList<string> MergedFieldNames { get; set; } = [];
    public IReadOnlyList<TemplateMergeFieldInfo> RemainingFields { get; set; } = [];
    public IReadOnlyList<string> MergedFormFieldNames { get; set; } = [];
    public IReadOnlyList<TemplateFormFieldInfo> RemainingFormFields { get; set; } = [];
}
