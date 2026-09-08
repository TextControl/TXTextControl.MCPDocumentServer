using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class TemplateFormFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool? Checked { get; set; }
    public string? Date { get; set; }
    public bool? Editable { get; set; }
    public bool Enabled { get; set; }
    public IReadOnlyList<string> Items { get; set; } = [];
    public string Location { get; set; } = "body";
}

