using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendMergeFieldOperationHandler : IDocumentOperationHandler
{
    public string Type => FieldsCapabilityPack.AppendMergeField;
    public string CapabilityPack => FieldsCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = FieldsCapabilityPack.AppendMergeField,
        CapabilityPack = FieldsCapabilityPack.PackName,
        Description = "Inserts real Word-compatible MERGEFIELD ApplicationFields at deterministic positions throughout a document.",
        Intent = "Use for named MailMerge placeholders. To replace existing placeholder/name text, use matchText with replaceAll or occurrenceIndex; never insert '{{name}}' as ordinary text.",
        RequiredProperties = ["type", "fieldName"],
        OptionalProperties = ["fieldText", "parameters", "matchText", "occurrenceIndex", "nearTextPosition", "replaceAll", "matchCase", "wholeWord", "start", "length", "expectedText", "textPosition", "paragraphIndex", "tableId", "rowIndex", "columnIndex", "headerFooterType", "sectionIndex", "placement"],
        Properties = new()
        {
            ["fieldName"] = "Merge field name stored as the first MERGEFIELD parameter.",
            ["fieldText"] = "Visible placeholder text. Defaults to fieldName.",
            ["parameters"] = "Optional complete ApplicationField parameter list. Defaults to [fieldName].",
            ["matchText"] = "Replaces matching body text with a real merge field. Use replaceAll=true for every occurrence or occurrenceIndex for one.",
            ["nearTextPosition"] = "Selects the match nearest this approximate browser-editor position. Use with matchText; do not combine with replaceAll or occurrenceIndex.",
            ["start"] = "Zero-based body character start. Requires length and should include expectedText.",
            ["length"] = "Character count replaced by the field.",
            ["expectedText"] = "Expected current text for a start/length replacement; the operation fails if it differs.",
            ["textPosition"] = "Zero-based insertion position in the body, or relative to a targeted table cell/header/footer.",
            ["paragraphIndex"] = "Zero-based paragraph target used with placement start, end, or replace.",
            ["tableId"] = "Table target; rowIndex and columnIndex are required.",
            ["headerFooterType"] = "Header/footer target: header, footer, firstPageHeader, firstPageFooter, evenHeader, or evenFooter.",
            ["placement"] = "For paragraph/cell/header/footer targets: start, end, or replace. Defaults to end."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.AppendMergeField,
            ["fieldName"] = "PartyName",
            ["fieldText"] = "Party Name",
            ["matchText"] = "Acme Corporation",
            ["replaceAll"] = true
        },
        ModelEffects = ["Creates actual MERGEFIELD markup; positional body fields remain authoritative in the physical TX document."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        string fieldName = RequireFieldName(operation.FieldName);
        List<string> parameters = NormalizeParameters(operation.Parameters, fieldName);
        string visibleText = string.IsNullOrWhiteSpace(operation.FieldText) ? fieldName : operation.FieldText!.Trim();
        string fieldId = string.IsNullOrWhiteSpace(operation.FieldId) ? Guid.NewGuid().ToString("N") : operation.FieldId!.Trim();
        FieldInsertionTarget target = FieldInsertionUtilities.Resolve(operation, allowMatch: true, allowHeaderFooter: true);
        int insertedCount = 0;
        List<Dictionary<string, object?>> insertedRanges = [];

        if (context.TryGetTextControl(out ServerTextControl tx))
        {
            if (target.Kind == FieldInsertionTargetKind.HeaderFooter)
            {
                HeaderFooter headerFooter = FieldInsertionUtilities.GetOrCreateHeaderFooter(tx, target.HeaderFooterType!.Value);
                FieldInsertionUtilities.PrepareHeaderFooterSelection(headerFooter, target);
                AddField(headerFooter.ApplicationFields, fieldName, visibleText, parameters);
                insertedCount = 1;
            }
            else
            {
                IReadOnlyList<FieldInsertionRange> ranges = FieldInsertionUtilities.ResolveBodyRanges(tx, target);
                foreach (FieldInsertionRange range in ranges.OrderByDescending(range => range.Start))
                {
                    FieldInsertionUtilities.PrepareSelection(tx, range);
                    AddField(tx.ApplicationFields, fieldName, visibleText, parameters);
                    insertedRanges.Add(new Dictionary<string, object?>
                    {
                        ["start"] = range.Start,
                        ["replacedLength"] = range.Length,
                        ["insertedLength"] = visibleText.Length
                    });
                    insertedCount++;
                }
            }
        }

        DocumentModel.DocumentBlock fieldBlock = CreateModelField(fieldId, fieldName, visibleText, parameters, target);
        AddModelField(context, target, fieldBlock);
        string location = FieldInsertionUtilities.DescribeLocation(target);
        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Inserted {insertedCount} real MERGEFIELD instance(s) named '{fieldName}' at {location}.",
            TargetType = "field",
            TargetId = target.ReplaceAll ? null : fieldId,
            Location = location,
            Metadata = new Dictionary<string, object?>
            {
                ["fieldId"] = fieldId,
                ["fieldName"] = fieldName,
                ["fieldType"] = "merge",
                ["typeName"] = "MERGEFIELD",
                ["parameters"] = parameters,
                ["insertedFieldCount"] = insertedCount,
                ["targetKind"] = target.Kind.ToString(),
                ["placement"] = target.Placement.ToString().ToLowerInvariant(),
                ["ranges"] = insertedRanges
            }
        };
    }

    private static void AddField(ApplicationFieldCollection collection, string fieldName, string visibleText, IReadOnlyList<string> parameters)
    {
        var field = new ApplicationField(ApplicationFieldFormat.MSWord, "MERGEFIELD", visibleText, parameters.ToArray());
        if (!collection.Add(field))
        {
            throw new InvalidOperationException($"TX Text Control could not insert merge field '{fieldName}' at the selected position.");
        }
    }

    private static DocumentModel.DocumentBlock CreateModelField(string fieldId, string fieldName, string visibleText, IReadOnlyList<string> parameters, FieldInsertionTarget target)
        => new()
        {
            Type = "field",
            Field = new DocumentModel.Field
            {
                Id = fieldId,
                Type = "merge",
                Name = fieldName,
                Value = visibleText,
                Properties = new Dictionary<string, string>
                {
                    ["format"] = "MSWord",
                    ["typeName"] = "MERGEFIELD",
                    ["parameters"] = string.Join("|", parameters),
                    ["location"] = FieldInsertionUtilities.DescribeLocation(target)
                }
            }
        };

    private static void AddModelField(DocumentOperationContext context, FieldInsertionTarget target, DocumentModel.DocumentBlock fieldBlock)
    {
        if (target.Kind == FieldInsertionTargetKind.TableCell)
        {
            DocumentModel.Table table = TableOperationUtilities.GetModelTable(context.Document, target.TableId!.Value.ToString());
            DocumentModel.TableCell cell = TableOperationUtilities.GetModelCell(table, target.RowIndex!.Value, target.ColumnIndex!.Value);
            if (target.Placement == FieldInsertionPlacement.Replace) cell.Blocks.Clear();
            if (target.Placement == FieldInsertionPlacement.Start) cell.Blocks.Insert(0, fieldBlock); else cell.Blocks.Add(fieldBlock);
        }
        else if (target.Kind == FieldInsertionTargetKind.DocumentEnd)
        {
            context.GetMainSection().Blocks.Add(fieldBlock);
        }
        else if (target.Kind == FieldInsertionTargetKind.HeaderFooter)
        {
            DocumentModel.Section section = context.GetSection(target.SectionIndex);
            DocumentModel.HeaderFooter model = target.HeaderFooterType is HeaderFooterType.Header or HeaderFooterType.FirstPageHeader or HeaderFooterType.EvenHeader
                ? section.Header ??= new DocumentModel.HeaderFooter { Type = FieldInsertionUtilities.ToModelHeaderFooterName(target.HeaderFooterType.Value) }
                : section.Footer ??= new DocumentModel.HeaderFooter { Type = FieldInsertionUtilities.ToModelHeaderFooterName(target.HeaderFooterType!.Value) };
            if (target.Placement == FieldInsertionPlacement.Replace) model.Blocks.Clear();
            if (target.Placement == FieldInsertionPlacement.Start) model.Blocks.Insert(0, fieldBlock); else model.Blocks.Add(fieldBlock);
        }
    }

    internal static string RequireFieldName(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) throw new ArgumentException("fieldName is required.");
        return fieldName.Trim();
    }

    internal static List<string> NormalizeParameters(IReadOnlyList<string> parameters, string fieldName)
    {
        List<string> normalized = parameters.Where(parameter => !string.IsNullOrWhiteSpace(parameter)).Select(parameter => parameter.Trim()).ToList();
        if (normalized.Count == 0) normalized.Add(fieldName);
        return normalized;
    }
}
