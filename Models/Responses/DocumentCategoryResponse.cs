using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentCategoryResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public double Confidence { get; set; }
    public List<string> Signals { get; set; } = [];
    public List<DocumentSuggestedAction> SuggestedActions { get; set; } = [];
}

public sealed class DocumentSuggestedAction
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}
