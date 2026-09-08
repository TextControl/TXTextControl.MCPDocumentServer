using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class ClearApplicationFieldsOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.ClearApplicationFields;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.ClearApplicationFields,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Removes all TX Text Control ApplicationFields from the document, optionally keeping visible text.",
        Intent = "Use to flatten templates after field replacement or to remove all field markup.",
        RequiredProperties = ["type"],
        OptionalProperties = ["keepText"],
        Properties = new()
        {
            ["keepText"] = "When true, removes field markup but keeps visible text. Defaults to true."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.ClearApplicationFields,
            ["keepText"] = true
        },
        ModelEffects = ["Removes neutral field blocks from the document model."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (context.TryGetTextControl(out var tx))
        {
            foreach (FieldContainer container in FieldInsertionUtilities.EnumerateFieldContainers(tx))
            {
                container.Content.ApplicationFields.Clear(operation.KeepText);
            }
        }

        var removed = 0;
        foreach (var section in context.Document.Sections)
        {
            removed += RemoveFieldBlocks(section.Blocks);
            if (section.Header is not null) removed += RemoveFieldBlocks(section.Header.Blocks);
            if (section.Footer is not null) removed += RemoveFieldBlocks(section.Footer.Blocks);
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Removed {removed} field block(s) from the document model.",
            TargetType = "fields",
            Location = "document.fields",
            Metadata = new Dictionary<string, object?>
            {
                ["removedFieldBlockCount"] = removed,
                ["keepText"] = operation.KeepText
            }
        };
    }

    private static int RemoveFieldBlocks(List<DocumentModel.DocumentBlock> blocks)
    {
        var removed = blocks.RemoveAll(block =>
            string.Equals(block.Type, "field", StringComparison.OrdinalIgnoreCase)
            && block.Field is not null
            && !string.Equals(block.Field.Type, "form", StringComparison.OrdinalIgnoreCase));

        foreach (var table in blocks
                     .Where(block => string.Equals(block.Type, "table", StringComparison.OrdinalIgnoreCase))
                     .Select(block => block.Table)
                     .Where(table => table is not null))
        {
            foreach (var cell in table!.Rows.SelectMany(row => row.Cells))
            {
                removed += RemoveFieldBlocks(cell.Blocks);
            }
        }

        return removed;
    }
}
