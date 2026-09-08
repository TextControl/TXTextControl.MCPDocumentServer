using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class UpdateMergeFieldOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.UpdateMergeField;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.UpdateMergeField,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Updates visible text and/or parameters for existing MERGEFIELD ApplicationFields by field name.",
        Intent = "Use for renaming merge placeholders or changing visible placeholder text in a template.",
        RequiredProperties = ["type", "fieldName"],
        OptionalProperties = ["fieldText", "parameters"],
        Properties = new()
        {
            ["fieldName"] = "Existing MERGEFIELD name to match against Parameters[0].",
            ["fieldText"] = "Optional replacement visible field text.",
            ["parameters"] = "Optional replacement ApplicationField parameters."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.UpdateMergeField,
            ["fieldName"] = "Address",
            ["fieldText"] = "Customer Address",
            ["parameters"] = new[] { "Customer.Address" }
        },
        ModelEffects = ["Updates matching neutral field blocks by name."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var fieldName = AppendMergeFieldOperationHandler.RequireFieldName(operation.FieldName);
        var replacementParameters = operation.Parameters.Count > 0
            ? AppendMergeFieldOperationHandler.NormalizeParameters(operation.Parameters, fieldName)
            : null;
        var replacementName = replacementParameters?[0] ?? fieldName;
        var replacementText = string.IsNullOrWhiteSpace(operation.FieldText)
            ? null
            : operation.FieldText!.Trim();
        var updatedCount = 0;

        if (context.TryGetTextControl(out var tx))
        {
            foreach (FieldContainer container in FieldInsertionUtilities.EnumerateFieldContainers(tx))
            {
                foreach (ApplicationField field in container.Content.ApplicationFields)
                {
                    if (!IsMatchingMergeField(field, fieldName))
                    {
                        continue;
                    }

                    if (replacementText is not null)
                    {
                        field.Text = replacementText;
                    }

                    if (replacementParameters is not null)
                    {
                        field.Parameters = replacementParameters.ToArray();
                    }

                    updatedCount++;
                }
            }
        }

        var updatedFieldIds = new List<string>();
        foreach (var field in EnumerateModelFields(context.Document)
                     .Where(field => string.Equals(field.Type, "merge", StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
        {
            field.Name = replacementName;
            updatedFieldIds.Add(field.Id);
            if (replacementText is not null)
            {
                field.Value = replacementText;
            }

            field.Properties["typeName"] = "MERGEFIELD";
            field.Properties["parameters"] = string.Join("|", replacementParameters ?? [replacementName]);
        }

        if (updatedCount == 0 && updatedFieldIds.Count == 0)
        {
            throw new ArgumentException($"No MERGEFIELD named '{fieldName}' was found. Inspect template fields and retry with an existing field name.");
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Updated {updatedCount} MERGEFIELD instance(s) named '{fieldName}'.",
            TargetType = "field",
            TargetId = updatedFieldIds.Count == 1 ? updatedFieldIds[0] : null,
            Location = "fields",
            Metadata = new Dictionary<string, object?>
            {
                ["fieldName"] = fieldName,
                ["replacementName"] = replacementName,
                ["updatedTxFieldCount"] = updatedCount,
                ["fieldIds"] = updatedFieldIds
            }
        };
    }

    private static bool IsMatchingMergeField(ApplicationField field, string fieldName)
        => string.Equals(field.TypeName, "MERGEFIELD", StringComparison.OrdinalIgnoreCase)
           && field.Parameters.Length > 0
           && string.Equals(field.Parameters[0], fieldName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<DocumentModel.Field> EnumerateModelFields(DocumentModel.Document document)
    {
        foreach (DocumentModel.Section section in document.Sections)
        {
            foreach (DocumentModel.Field field in EnumerateBlockFields(section.Blocks)) yield return field;
            if (section.Header is not null)
                foreach (DocumentModel.Field field in EnumerateBlockFields(section.Header.Blocks)) yield return field;
            if (section.Footer is not null)
                foreach (DocumentModel.Field field in EnumerateBlockFields(section.Footer.Blocks)) yield return field;
        }
    }

    private static IEnumerable<DocumentModel.Field> EnumerateBlockFields(IEnumerable<DocumentModel.DocumentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (string.Equals(block.Type, "field", StringComparison.OrdinalIgnoreCase) && block.Field is not null)
            {
                yield return block.Field;
            }

            if (string.Equals(block.Type, "table", StringComparison.OrdinalIgnoreCase) && block.Table is not null)
            {
                foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells))
                {
                    foreach (var field in EnumerateBlockFields(cell.Blocks))
                    {
                        yield return field;
                    }
                }
            }
        }
    }
}
