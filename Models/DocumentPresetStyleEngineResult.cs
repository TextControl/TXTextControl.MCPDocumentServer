using System.Collections.Generic;

namespace TxTextControl.McpServer.Models;

/// <summary>Contains the document state and metrics produced by server-owned preset styling.</summary>
public sealed record DocumentPresetStyleEngineResult(
    DocumentState State,
    IReadOnlyDictionary<string, int> AppliedStyles,
    int TablesStyled,
    bool PageLayoutApplied,
    IReadOnlyList<string> Warnings);
