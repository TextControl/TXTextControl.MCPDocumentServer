using System.Collections.Generic;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Models;

public sealed class DocumentState
{
    /// <summary>
    /// Monotonically increasing durable session revision. It changes after each committed document mutation.
    /// </summary>
    public long Revision { get; set; }

    public string WorkingDocumentPath { get; set; } = string.Empty;
    public Document Document { get; set; } = new();
    public Dictionary<string, TextStyleDefinition> Styles { get; set; } = new();
    public List<OperationResult> LastOperationResults { get; set; } = [];

    /// <summary>
    /// Hash of the source bytes used by the most recent load operation. Mutations clear this value.
    /// </summary>
    public string? SourceContentHash { get; set; }

    internal DocumentContentSnapshot? ContentSnapshot { get; set; }
}
