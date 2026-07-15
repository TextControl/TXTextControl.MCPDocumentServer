using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class ClearFormFieldsOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.ClearFormFields;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.ClearFormFields,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Removes all TX Text Control form fields, optionally keeping their visible text.",
        Intent = "Use to flatten/remove interactive form controls outside MailMerge.",
        RequiredProperties = ["type"],
        OptionalProperties = ["keepText"],
        Properties = new()
        {
            ["keepText"] = "When true, removes field markup but keeps visible text. Defaults to true."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.ClearFormFields,
            ["keepText"] = true
        },
        ModelEffects = ["Removes neutral form field blocks from the document model."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var removedTx = 0;
        if (context.TryGetTextControl(out var tx))
        {
            while (tx.FormFields.Count > 0)
            {
                var enumerator = tx.FormFields.GetEnumerator();
                if (!enumerator.MoveNext())
                {
                    break;
                }

                tx.FormFields.Remove((FormField)enumerator.Current, operation.KeepText);
                removedTx++;
            }
        }

        var removedModel = RemoveFormFieldBlocks(context.Document.Sections.SelectMany(section => section.Blocks).ToList());

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Removed {Math.Max(removedTx, removedModel)} form field(s).",
            TargetType = "formFields",
            Location = "document.formFields",
            Metadata = new Dictionary<string, object?>
            {
                ["removedTxFieldCount"] = removedTx,
                ["removedModelFieldCount"] = removedModel,
                ["keepText"] = operation.KeepText
            }
        };
    }

    private static int RemoveFormFieldBlocks(List<DocumentModel.DocumentBlock> blocks)
    {
        var removed = blocks.RemoveAll(block =>
            string.Equals(block.Type, "field", StringComparison.OrdinalIgnoreCase)
            && block.Field is not null
            && string.Equals(block.Field.Type, "form", StringComparison.OrdinalIgnoreCase));

        foreach (var table in blocks
                     .Select(block => block.Table)
                     .Where(table => table is not null))
        {
            foreach (var cell in table!.Rows.SelectMany(row => row.Cells))
            {
                removed += RemoveFormFieldBlocks(cell.Blocks);
            }
        }

        return removed;
    }
}
