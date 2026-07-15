using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class TemplateFormFieldsResponse
{
    public string SessionId { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public IReadOnlyList<TemplateFormFieldInfo> Fields { get; set; } = [];
}
