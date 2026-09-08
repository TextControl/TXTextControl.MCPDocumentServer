using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendFormFieldOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.AppendFormField;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.AppendFormField,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Inserts one or more real TX Text Control text, selection, checkbox, or date form fields at deterministic body, table, header, or footer positions.",
        Intent = "Use for fillable templates whose controls must remain editable or be flattened later by MailMerge.",
        RequiredProperties = ["type", "fieldName", "formFieldType"],
        OptionalProperties = ["text", "date", "checked", "items", "editable", "enabled", "emptyWidth", "matchText", "occurrenceIndex", "nearTextPosition", "replaceAll", "matchCase", "wholeWord", "start", "length", "expectedText", "textPosition", "paragraphIndex", "tableId", "rowIndex", "columnIndex", "headerFooterType", "sectionIndex", "placement"],
        Properties = new()
        {
            ["fieldName"] = "Form field name used by MailMerge data.",
            ["formFieldType"] = "One of: text, selection, checkbox, date.",
            ["matchText"] = "Replaces matching body text with real form fields. Use replaceAll=true for every occurrence or occurrenceIndex for one.",
            ["nearTextPosition"] = "Selects the match nearest this approximate browser-editor position. Use with matchText.",
            ["start"] = "Zero-based body character start. Requires length and should include expectedText.",
            ["length"] = "Character count replaced by the form field.",
            ["expectedText"] = "Expected range text; stale coordinates are rejected.",
            ["textPosition"] = "Zero-based insertion position in the body, targeted cell, header, or footer.",
            ["paragraphIndex"] = "Zero-based paragraph target used with placement start, end, or replace.",
            ["tableId"] = "Table target; rowIndex and columnIndex are required.",
            ["headerFooterType"] = "Optional header/footer target.",
            ["placement"] = "For paragraph/cell/header/footer: start, end, or replace. Defaults to end."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.AppendFormField,
            ["fieldName"] = "contract_type",
            ["formFieldType"] = "selection",
            ["items"] = new[] { "Standard", "Enterprise", "Trial" },
            ["text"] = "Standard",
            ["paragraphIndex"] = 2,
            ["placement"] = "end"
        },
        ModelEffects = ["Creates real form-field markup; positional body fields remain authoritative in the physical TX document."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        string fieldName = FormFieldOperationUtilities.RequireFieldName(operation.FieldName);
        string type = FormFieldOperationUtilities.ResolveType(operation.FormFieldType);
        string fieldId = string.IsNullOrWhiteSpace(operation.FieldId) ? Guid.NewGuid().ToString("N") : operation.FieldId!.Trim();
        FieldInsertionTarget target = FieldInsertionUtilities.Resolve(operation, allowMatch: true, allowHeaderFooter: true);
        int insertedCount = 0;

        if (context.TryGetTextControl(out ServerTextControl tx))
        {
            if (target.Kind == FieldInsertionTargetKind.HeaderFooter)
            {
                HeaderFooter headerFooter = FieldInsertionUtilities.GetOrCreateHeaderFooter(tx, target.HeaderFooterType!.Value);
                FieldInsertionUtilities.PrepareHeaderFooterSelection(headerFooter, target);
                FormField field = FormFieldOperationUtilities.CreateFormField(operation, fieldName, type);
                if (!headerFooter.FormFields.Add(field))
                {
                    throw new InvalidOperationException($"TX Text Control could not insert form field '{fieldName}' into the header/footer.");
                }
                insertedCount = 1;
            }
            else
            {
                foreach (FieldInsertionRange range in FieldInsertionUtilities.ResolveBodyRanges(tx, target).OrderByDescending(range => range.Start))
                {
                    FieldInsertionUtilities.PrepareSelection(tx, range);
                    FormField field = FormFieldOperationUtilities.CreateFormField(operation, fieldName, type);
                    if (!tx.FormFields.Add(field))
                    {
                        throw new InvalidOperationException($"TX Text Control could not insert form field '{fieldName}' at the selected position.");
                    }
                    insertedCount++;
                }
            }
        }

        DocumentModel.DocumentBlock block = new()
        {
            Type = "field",
            Field = FormFieldOperationUtilities.ToModelField(fieldId, fieldName, type, operation)
        };
        block.Field!.Properties["location"] = FieldInsertionUtilities.DescribeLocation(target);
        AddModelField(context, target, block);
        string location = FieldInsertionUtilities.DescribeLocation(target);
        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Inserted {insertedCount} real {type} form field instance(s) named '{fieldName}' at {location}.",
            TargetType = "formField",
            TargetId = target.ReplaceAll ? null : fieldId,
            Location = location,
            Metadata = new Dictionary<string, object?>
            {
                ["fieldId"] = fieldId,
                ["fieldName"] = fieldName,
                ["formFieldType"] = type,
                ["txType"] = FormFieldOperationUtilities.ToTxTypeName(type),
                ["targetKind"] = target.Kind.ToString(),
                ["insertedFieldCount"] = insertedCount
            }
        };
    }

    private static void AddModelField(DocumentOperationContext context, FieldInsertionTarget target, DocumentModel.DocumentBlock block)
    {
        if (target.Kind == FieldInsertionTargetKind.TableCell)
        {
            DocumentModel.Table table = TableOperationUtilities.GetModelTable(context.Document, target.TableId!.Value.ToString());
            DocumentModel.TableCell cell = TableOperationUtilities.GetModelCell(table, target.RowIndex!.Value, target.ColumnIndex!.Value);
            if (target.Placement == FieldInsertionPlacement.Replace) cell.Blocks.Clear();
            if (target.Placement == FieldInsertionPlacement.Start) cell.Blocks.Insert(0, block); else cell.Blocks.Add(block);
        }
        else if (target.Kind == FieldInsertionTargetKind.DocumentEnd)
        {
            context.GetMainSection().Blocks.Add(block);
        }
        else if (target.Kind == FieldInsertionTargetKind.HeaderFooter)
        {
            DocumentModel.Section section = context.GetSection(target.SectionIndex);
            DocumentModel.HeaderFooter model = target.HeaderFooterType is HeaderFooterType.Header or HeaderFooterType.FirstPageHeader or HeaderFooterType.EvenHeader
                ? section.Header ??= new DocumentModel.HeaderFooter { Type = FieldInsertionUtilities.ToModelHeaderFooterName(target.HeaderFooterType.Value) }
                : section.Footer ??= new DocumentModel.HeaderFooter { Type = FieldInsertionUtilities.ToModelHeaderFooterName(target.HeaderFooterType!.Value) };
            if (target.Placement == FieldInsertionPlacement.Replace) model.Blocks.Clear();
            if (target.Placement == FieldInsertionPlacement.Start) model.Blocks.Insert(0, block); else model.Blocks.Add(block);
        }
    }
}
