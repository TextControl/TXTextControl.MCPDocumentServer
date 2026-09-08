using System.Collections.Generic;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Models;

/// <summary>
/// Result of a single-pass template merge and its before/after field inspection.
/// </summary>
public sealed record MergeTemplateEngineResult(
    DocumentState State,
    IReadOnlyList<TemplateMergeFieldInfo> FieldsBefore,
    IReadOnlyList<TemplateMergeFieldInfo> FieldsAfter,
    IReadOnlyList<TemplateFormFieldInfo> FormFieldsBefore,
    IReadOnlyList<TemplateFormFieldInfo> FormFieldsAfter);
