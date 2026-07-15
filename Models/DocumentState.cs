using System.Collections.Generic;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Models;

public sealed class DocumentState
{
    public string WorkingDocumentPath { get; set; } = string.Empty;
    public Document Document { get; set; } = new();
    public Dictionary<string, TextStyleDefinition> Styles { get; set; } = new();
    public List<OperationResult> LastOperationResults { get; set; } = [];
}
