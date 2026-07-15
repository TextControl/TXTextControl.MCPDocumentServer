namespace TxTextControl.McpServer.Models.Responses;

public sealed class TemplateMergeBlockInfo
{
    public string Name { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public int Id { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
    public string TextPreview { get; set; } = string.Empty;
}
