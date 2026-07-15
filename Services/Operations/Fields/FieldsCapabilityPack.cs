using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class FieldsCapabilityPack : ICapabilityPack
{
    public const string PackName = "Fields";
    public const string AppendMergeField = "append_merge_field";
    public const string UpdateMergeField = "update_merge_field";
    public const string ClearApplicationFields = "clear_application_fields";
    public const string AppendMergeBlock = "append_merge_block";
    public const string AppendFormField = "append_form_field";
    public const string UpdateFormField = "update_form_field";
    public const string ClearFormFields = "clear_form_fields";

    public string Name => PackName;

    public string Description =>
        "Creates and manages Word-compatible TX Text Control ApplicationFields, form fields, and merge-block SubTextParts.";

    public IReadOnlyCollection<string> OperationTypes { get; } =
    [
        AppendMergeField,
        UpdateMergeField,
        ClearApplicationFields,
        AppendMergeBlock,
        AppendFormField,
        UpdateFormField,
        ClearFormFields
    ];
}
