using System.Collections.Generic;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Models;

/// <summary>
/// Template fields and repeating blocks extracted from one document revision.
/// </summary>
public sealed record TemplateContentSnapshot(
    IReadOnlyList<TemplateMergeFieldInfo> MergeFields,
    IReadOnlyList<TemplateMergeBlockInfo> MergeBlocks,
    IReadOnlyList<TemplateFormFieldInfo> FormFields);
