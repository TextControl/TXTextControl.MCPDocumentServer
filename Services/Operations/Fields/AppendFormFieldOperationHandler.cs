using System;
using System.Collections.Generic;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendFormFieldOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.AppendFormField;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.AppendFormField,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Appends or inserts a TX Text Control form field: text, selection/dropdown, checkbox, or date.",
        Intent = "Use when creating fillable DOCX/PDF templates whose fields can later be preselected or flattened by MailMerge.",
        RequiredProperties = ["type", "fieldName", "formFieldType"],
        OptionalProperties = ["text", "date", "checked", "items", "editable", "enabled", "emptyWidth", "tableId", "rowIndex", "columnIndex", "placement"],
        Properties = new()
        {
            ["fieldName"] = "Form field name used by MailMerge JSON/object data.",
            ["formFieldType"] = "One of: text, selection, checkbox, date. Aliases dropdown, combobox, and check are accepted.",
            ["text"] = "Initial text/value for text, selection, and date fields.",
            ["date"] = "Initial date value for date fields.",
            ["checked"] = "Initial checkbox state.",
            ["items"] = "Available values for selection/dropdown fields.",
            ["editable"] = "Allows custom values for selection fields.",
            ["enabled"] = "Whether the form field is editable.",
            ["emptyWidth"] = "TX empty field width. Defaults to 1000.",
            ["tableId"] = "Optional target table id. When set, rowIndex and columnIndex are required.",
            ["placement"] = "Optional target placement: end, start, or replace. Defaults to end."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.AppendFormField,
            ["fieldName"] = "contract_type",
            ["formFieldType"] = "selection",
            ["items"] = new[] { "Standard", "Enterprise", "Trial" },
            ["text"] = "Standard"
        },
        ModelEffects = ["Adds a neutral field block with type 'form' either to document.sections[].blocks[] or to a table cell's blocks."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var fieldName = FormFieldOperationUtilities.RequireFieldName(operation.FieldName);
        var type = FormFieldOperationUtilities.ResolveType(operation.FormFieldType);
        var fieldId = string.IsNullOrWhiteSpace(operation.FieldId)
            ? Guid.NewGuid().ToString("N")
            : operation.FieldId!.Trim();

        if (context.TryGetTextControl(out var tx))
        {
            FormFieldOperationUtilities.SetInsertionPoint(context, tx, operation);
            var field = FormFieldOperationUtilities.CreateFormField(operation, fieldName, type);
            if (!tx.FormFields.Add(field))
            {
                throw new InvalidOperationException($"TX Text Control could not insert form field '{fieldName}'.");
            }
        }

        var modelField = FormFieldOperationUtilities.ToModelField(fieldId, fieldName, type, operation);
        var block = new DocumentModel.DocumentBlock
        {
            Type = "field",
            Field = modelField
        };
        FormFieldOperationUtilities.AddModelField(context, operation, block);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Inserted {type} form field '{fieldName}'.",
            TargetType = "formField",
            TargetId = fieldId,
            Location = FormFieldOperationUtilities.Location(operation),
            Metadata = new Dictionary<string, object?>
            {
                ["fieldId"] = fieldId,
                ["fieldName"] = fieldName,
                ["formFieldType"] = type,
                ["txType"] = FormFieldOperationUtilities.ToTxTypeName(type)
            }
        };
    }
}
