using System;

namespace TxTextControl.McpServer.Models;

public sealed class DocumentSession
{
    public string SessionId { get; init; } = default!;
    public string WorkingDirectory { get; init; } = default!;
    public string StatePath { get; init; } = default!;
    public string WorkingDocumentPath { get; init; } = default!;
    public string? LoadedTemplateName { get; set; }
    public DateTime CreatedUtc { get; init; }
    public DateTime LastAccessUtc { get; set; }

    internal object SyncRoot { get; } = new();
    internal DocumentState? CachedState { get; set; }
    internal DocumentContentSnapshot? CachedContent { get; set; }
    internal TemplateContentSnapshot? CachedTemplate { get; set; }
}
