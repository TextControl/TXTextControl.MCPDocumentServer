using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class UpdateFormFieldOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.UpdateFormField;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.UpdateFormField,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Updates existing TX Text Control form fields by name.",
        Intent = "Use to prefill form fields without MailMerge or to adjust checkbox/selection/date values before export.",
        RequiredProperties = ["type", "fieldName"],
        OptionalProperties = ["text", "date", "checked", "items", "editable", "enabled"],
        Properties = new()
        {
            ["fieldName"] = "Existing form field name.",
            ["text"] = "New text/value for text and selection fields.",
            ["date"] = "New date value for date fields.",
            ["checked"] = "New checkbox state.",
            ["items"] = "Replaces selection field item list.",
            ["editable"] = "Sets selection field editable state.",
            ["enabled"] = "Sets form field enabled state."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.UpdateFormField,
            ["fieldName"] = "approved",
            ["checked"] = true
        },
        ModelEffects = ["Updates matching neutral form field blocks by name."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var fieldName = FormFieldOperationUtilities.RequireFieldName(operation.FieldName);
        var updated = 0;

        if (context.TryGetTextControl(out var tx))
        {
            foreach (FormField field in tx.FormFields)
            {
                if (!string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FormFieldOperationUtilities.ApplyValue(field, operation);
                updated++;
            }
        }

        var updatedModel = UpdateModelFields(context.Document.Sections.SelectMany(section => section.Blocks), fieldName, operation);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Updated {Math.Max(updated, updatedModel)} form field(s) named '{fieldName}'.",
            TargetType = "formField",
            TargetId = fieldName,
            Location = "document.formFields",
            Metadata = new Dictionary<string, object?>
            {
                ["fieldName"] = fieldName,
                ["updatedTxFieldCount"] = updated,
                ["updatedModelFieldCount"] = updatedModel
            }
        };
    }

    private static int UpdateModelFields(IEnumerable<DocumentModel.DocumentBlock> blocks, string fieldName, DocumentOperation operation)
    {
        var updated = 0;
        foreach (var block in blocks)
        {
            if (block.Field is not null
                && string.Equals(block.Field.Type, "form", StringComparison.OrdinalIgnoreCase)
                && string.Equals(block.Field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyModelValue(block.Field, operation);
                updated++;
            }

            if (block.Table is null)
            {
                continue;
            }

            foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells))
            {
                updated += UpdateModelFields(cell.Blocks, fieldName, operation);
            }
        }

        return updated;
    }

    private static void ApplyModelValue(DocumentModel.Field field, DocumentOperation operation)
    {
        if (operation.Checked.HasValue)
        {
            field.Value = operation.Checked.Value.ToString().ToLowerInvariant();
        }
        else if (!string.IsNullOrWhiteSpace(operation.Date))
        {
            field.Value = operation.Date;
        }
        else if (operation.Text is not null || operation.FieldText is not null)
        {
            field.Value = operation.Text ?? operation.FieldText;
        }

        if (operation.Items.Count > 0)
        {
            field.Properties["items"] = string.Join("|", operation.Items);
        }

        if (operation.Editable.HasValue)
        {
            field.Properties["editable"] = operation.Editable.Value.ToString().ToLowerInvariant();
        }

        if (operation.Enabled.HasValue)
        {
            field.Properties["enabled"] = operation.Enabled.Value.ToString().ToLowerInvariant();
        }
    }
}
