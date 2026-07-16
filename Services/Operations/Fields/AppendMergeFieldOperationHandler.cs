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
        Description = "Appends or inserts a Word-compatible MERGEFIELD ApplicationField at the end of the document or into a table cell.",
        Intent = "Use when creating mail merge templates with named placeholders, including placeholders inside table cells.",
        RequiredProperties = ["type", "fieldName"],
        OptionalProperties = ["fieldText", "parameters", "tableId", "rowIndex", "columnIndex", "placement"],
        Properties = new()
        {
            ["fieldName"] = "Merge field name. Stored as the first MERGEFIELD parameter.",
            ["fieldText"] = "Optional visible placeholder text. Defaults to the field name.",
            ["parameters"] = "Optional full ApplicationField parameters. If omitted, [fieldName] is used.",
            ["tableId"] = "Optional target table id. When set, rowIndex and columnIndex are required.",
            ["rowIndex"] = "Optional zero-based target table row index.",
            ["columnIndex"] = "Optional zero-based target table column index.",
            ["placement"] = "Optional target placement: end, start, or replace. Defaults to end."
        },
        Example = new()
        {
            ["type"] = FieldsCapabilityPack.AppendMergeField,
            ["fieldName"] = "CustomerName",
            ["fieldText"] = "Customer Name",
            ["tableId"] = "10",
            ["rowIndex"] = 1,
            ["columnIndex"] = 0,
            ["placement"] = "replace"
        },
        ModelEffects = ["Adds a field block with type 'merge' either to document.sections[].blocks[] or to a table cell's blocks."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var fieldName = RequireFieldName(operation.FieldName);
        var parameters = NormalizeParameters(operation.Parameters, fieldName);
        var visibleText = string.IsNullOrWhiteSpace(operation.FieldText)
            ? fieldName
            : operation.FieldText!.Trim();
        var fieldId = string.IsNullOrWhiteSpace(operation.FieldId)
            ? Guid.NewGuid().ToString("N")
            : operation.FieldId!.Trim();

        var target = ResolveTarget(operation);

        if (context.TryGetTextControl(out var tx))
        {
            var field = new ApplicationField(
                ApplicationFieldFormat.MSWord,
                "MERGEFIELD",
                visibleText,
                parameters.ToArray());

            var insertionIndex = SetInsertionPoint(context, tx, target);

            if (!tx.ApplicationFields.Add(field))
            {
                throw new InvalidOperationException(
                    $"TX Text Control could not insert merge field '{fieldName}' at position {insertionIndex} (text length {(tx.Text ?? string.Empty).Length}, correction {context.DocumentPositionCorrection}).");
            }

            if (visibleText.Length > 0)
            {
                SelectInsertedFieldText(tx, target, field, insertionIndex, visibleText.Length);
                var selection = tx.Selection;
                DocumentOperationFormatter.ApplyStyle(selection, ResolveTargetTextStyle(context, target));
                tx.Selection = selection;
            }

            if (target.Kind == MergeFieldTargetKind.DocumentEnd)
            {
                context.InlineDocumentEndInsertionIndex = insertionIndex + visibleText.Length;
                context.HasOpenParagraph = true;
            }
        }

        var fieldBlock = new DocumentModel.DocumentBlock
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
                    ["parameters"] = string.Join("|", parameters)
                }
            }
        };

        string location;
        if (target.Kind == MergeFieldTargetKind.TableCell)
        {
            var modelTable = TableOperationUtilities.GetModelTable(context.Document, target.TableId!.Value.ToString());
            var modelCell = TableOperationUtilities.GetModelCell(modelTable, target.RowIndex!.Value, target.ColumnIndex!.Value);
            if (target.Placement == MergeFieldPlacement.Replace)
            {
                modelCell.Blocks.Clear();
            }

            if (target.Placement == MergeFieldPlacement.Start)
            {
                modelCell.Blocks.Insert(0, fieldBlock);
            }
            else
            {
                modelCell.Blocks.Add(fieldBlock);
            }

            location = $"tables['{target.TableId}'].rows[{target.RowIndex}].cells[{target.ColumnIndex}].blocks";
        }
        else
        {
            var section = context.GetMainSection();
            var blockIndex = section.Blocks.Count;
            section.Blocks.Add(fieldBlock);
            location = $"sections[0].blocks[{blockIndex}].field";
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = target.Kind == MergeFieldTargetKind.TableCell
                ? $"Inserted MERGEFIELD '{fieldName}' into table '{target.TableId}' cell ({target.RowIndex}, {target.ColumnIndex})."
                : $"Appended MERGEFIELD '{fieldName}'.",
            TargetType = "field",
            TargetId = fieldId,
            Location = location,
            Metadata = new Dictionary<string, object?>
            {
                ["fieldId"] = fieldId,
                ["fieldName"] = fieldName,
                ["fieldType"] = "merge",
                ["typeName"] = "MERGEFIELD",
                ["parameters"] = parameters,
                ["tableId"] = target.TableId?.ToString(),
                ["rowIndex"] = target.RowIndex,
                ["columnIndex"] = target.ColumnIndex,
                ["placement"] = target.Placement.ToString().ToLowerInvariant()
            }
        };
    }

    private static MergeFieldTarget ResolveTarget(DocumentOperation operation)
    {
        var placement = ResolvePlacement(operation.Placement);
        var hasTableTarget = !string.IsNullOrWhiteSpace(operation.TableId)
                             || operation.RowIndex.HasValue
                             || operation.ColumnIndex.HasValue;
        if (!hasTableTarget)
        {
            return new MergeFieldTarget(MergeFieldTargetKind.DocumentEnd, null, null, null, placement);
        }

        return new MergeFieldTarget(
            MergeFieldTargetKind.TableCell,
            TableOperationUtilities.RequireTableId(operation.TableId),
            TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex)),
            TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex)),
            placement);
    }

    private static MergeFieldPlacement ResolvePlacement(string? placement)
    {
        if (string.IsNullOrWhiteSpace(placement))
        {
            return MergeFieldPlacement.End;
        }

        return placement.Trim().ToLowerInvariant() switch
        {
            "end" => MergeFieldPlacement.End,
            "start" => MergeFieldPlacement.Start,
            "replace" => MergeFieldPlacement.Replace,
            _ => throw new ArgumentException("placement must be 'end', 'start', or 'replace'.")
        };
    }

    private static int SetInsertionPoint(DocumentOperationContext context, ServerTextControl tx, MergeFieldTarget target)
    {
        if (target.Kind == MergeFieldTargetKind.DocumentEnd)
        {
            var insertionIndex = (tx.Text ?? string.Empty).Length;
            tx.Selection = new Selection(insertionIndex, 0);
            return insertionIndex;
        }

        var table = TableOperationUtilities.GetTxTable(tx, target.TableId!.Value);
        var cell = TableOperationUtilities.GetTxCell(table, target.RowIndex!.Value, target.ColumnIndex!.Value);
        if (target.Placement == MergeFieldPlacement.Replace)
        {
            cell.Text = string.Empty;
        }

        var offset = target.Placement switch
        {
            MergeFieldPlacement.Start or MergeFieldPlacement.Replace => 0,
            MergeFieldPlacement.End => cell.Text?.Length ?? 0,
            _ => 0
        };

        var cellInsertionIndex = Math.Max(0, cell.Start - 1 + offset);
        tx.Selection = new Selection(cellInsertionIndex, 0);
        return cellInsertionIndex;
    }

    private static DocumentModel.TextStyleDefinition ResolveTargetTextStyle(
        DocumentOperationContext context,
        MergeFieldTarget target)
    {
        if (target.Kind == MergeFieldTargetKind.TableCell)
        {
            var modelTable = TableOperationUtilities.TryGetModelTable(context.Document, target.TableId!.Value.ToString());
            if (modelTable is not null
                && target.RowIndex!.Value < modelTable.Rows.Count
                && target.ColumnIndex!.Value < modelTable.Rows[target.RowIndex.Value].Cells.Count)
            {
                var modelCell = modelTable.Rows[target.RowIndex.Value].Cells[target.ColumnIndex.Value];
                var runStyle = modelCell.Blocks
                    .Select(block => block.Paragraph)
                    .Where(paragraph => paragraph is not null)
                    .SelectMany(paragraph => paragraph!.Runs)
                    .Select(run => run.Style)
                    .FirstOrDefault(style => style is not null);

                if (runStyle is not null)
                {
                    return runStyle;
                }
            }
        }

        return context.GetDefaultTextStyle();
    }

    private static void SelectInsertedFieldText(
        ServerTextControl tx,
        MergeFieldTarget target,
        ApplicationField field,
        int insertionIndex,
        int visibleTextLength)
    {
        if (target.Kind == MergeFieldTargetKind.TableCell)
        {
            var table = TableOperationUtilities.GetTxTable(tx, target.TableId!.Value);
            TableOperationUtilities.GetTxCell(table, target.RowIndex!.Value, target.ColumnIndex!.Value).Select();
            return;
        }

        tx.Selection = new Selection(insertionIndex, visibleTextLength);
    }

    private static int GetDocumentEndInsertionIndex(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return Math.Max(0, text.Length - 2);
        }

        if (text.EndsWith('\n') || text.EndsWith('\r'))
        {
            return Math.Max(0, text.Length - 1);
        }

        return text.Length;
    }

    internal static string RequireFieldName(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("fieldName is required.");
        }

        return fieldName.Trim();
    }

    internal static List<string> NormalizeParameters(IReadOnlyList<string> parameters, string fieldName)
    {
        var normalized = new List<string>();
        foreach (var parameter in parameters)
        {
            if (!string.IsNullOrWhiteSpace(parameter))
            {
                normalized.Add(parameter.Trim());
            }
        }

        if (normalized.Count == 0)
        {
            normalized.Add(fieldName);
        }

        return normalized;
    }

    private enum MergeFieldTargetKind
    {
        DocumentEnd,
        TableCell
    }

    private enum MergeFieldPlacement
    {
        End,
        Start,
        Replace
    }

    private sealed record MergeFieldTarget(
        MergeFieldTargetKind Kind,
        int? TableId,
        int? RowIndex,
        int? ColumnIndex,
        MergeFieldPlacement Placement);
}
