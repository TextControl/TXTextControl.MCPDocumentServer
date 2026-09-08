using System;
using System.ComponentModel;
using ModelContextProtocol.Server;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Tools;

/// <summary>Focused, model-friendly tools for real merge and form fields.</summary>
[McpServerToolType]
public sealed class FieldTools(DocumentWorkflowService workflow)
{
    [McpServerTool(Name = "insert_merge_field", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Inserts one or more real Word-compatible MERGEFIELD ApplicationFields into an existing document; never insert '{{name}}' placeholder text. Required: sessionId and fieldName. To replace names or placeholders, set matchText and one of replaceAll=true, occurrenceIndex, or nearTextPosition. nearTextPosition is a non-authoritative browser-position hint that selects the closest server-side match and is ideal for editor selections inside tables. Set fieldText to the original matched text when its visible value must be preserved. Use start, length, and expectedText only with coordinates returned by MCP inspection/search tools. To insert without replacing text, set textPosition, or paragraphIndex with placement start/end. A paragraph placement of replace replaces its text. For a table cell set tableId, rowIndex, columnIndex and placement start/end/replace. For a header/footer set headerFooterType and placement start/end/replace; optional textPosition is relative to that header/footer. Omit every target to insert at the body end. Use exactly one target kind.")]
    public object InsertMergeField(InsertMergeFieldRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = FieldsCapabilityPack.AppendMergeField,
                        FieldName = request.FieldName,
                        FieldText = request.FieldText,
                        Parameters = request.Parameters,
                        MatchText = request.MatchText,
                        OccurrenceIndex = request.OccurrenceIndex,
                        NearTextPosition = request.NearTextPosition,
                        ReplaceAll = request.ReplaceAll,
                        MatchCase = request.MatchCase,
                        WholeWord = request.WholeWord,
                        Start = request.Start,
                        Length = request.Length,
                        ExpectedText = request.ExpectedText,
                        TextPosition = request.TextPosition,
                        ParagraphIndex = request.ParagraphIndex,
                        TableId = request.TableId,
                        RowIndex = request.RowIndex,
                        ColumnIndex = request.ColumnIndex,
                        HeaderFooterType = request.HeaderFooterType,
                        SectionIndex = request.SectionIndex,
                        Placement = request.Placement
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "insert_form_field", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Inserts one or more real TX Text Control text, selection, checkbox, or date form fields into an existing document. Required: sessionId, fieldName, and formFieldType. Replace existing text with matchText plus replaceAll, occurrenceIndex, or nearTextPosition; the latter selects the server match closest to a browser-editor position. Alternatively target an MCP-inspected start/length range with expectedText, an absolute textPosition, a paragraphIndex with placement start/end/replace, a table cell, or a header/footer. Omit every target for the body end. This creates field markup, not placeholder text.")]
    public object InsertFormField(InsertFormFieldRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = FieldsCapabilityPack.AppendFormField,
                        FieldName = request.FieldName,
                        FormFieldType = request.FormFieldType,
                        Text = request.Text,
                        Date = request.Date,
                        Checked = request.Checked,
                        Items = request.Items,
                        Editable = request.Editable,
                        Enabled = request.Enabled,
                        EmptyWidth = request.EmptyWidth,
                        MatchText = request.MatchText,
                        OccurrenceIndex = request.OccurrenceIndex,
                        NearTextPosition = request.NearTextPosition,
                        ReplaceAll = request.ReplaceAll,
                        MatchCase = request.MatchCase,
                        WholeWord = request.WholeWord,
                        Start = request.Start,
                        Length = request.Length,
                        ExpectedText = request.ExpectedText,
                        TextPosition = request.TextPosition,
                        ParagraphIndex = request.ParagraphIndex,
                        TableId = request.TableId,
                        RowIndex = request.RowIndex,
                        ColumnIndex = request.ColumnIndex,
                        HeaderFooterType = request.HeaderFooterType,
                        SectionIndex = request.SectionIndex,
                        Placement = request.Placement
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "create_merge_block", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Creates a real repeating MailMerge block (txmb_ SubTextPart) around existing content. Required: sessionId, blockName, and exactly one target: tableId plus rowIndex; start plus length and expectedText; or startParagraphIndex plus optional endParagraphIndex. It wraps existing content and does not insert '{{#block}}' text.")]
    public object CreateMergeBlock(CreateMergeBlockRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = FieldsCapabilityPack.AppendMergeBlock,
                        BlockName = request.BlockName,
                        BlockId = request.BlockId,
                        TableId = request.TableId,
                        RowIndex = request.RowIndex,
                        Start = request.Start,
                        Length = request.Length,
                        ExpectedText = request.ExpectedText,
                        StartParagraphIndex = request.StartParagraphIndex,
                        EndParagraphIndex = request.EndParagraphIndex
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "update_merge_field", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Updates every real MERGEFIELD ApplicationField with fieldName in the body, headers, and footers. Set fieldText to change visible text and/or parameters to rename or replace its field parameters. The call fails when no matching field exists; inspect with get_template_merge_fields first when uncertain.")]
    public object UpdateMergeField(UpdateMergeFieldRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = FieldsCapabilityPack.UpdateMergeField,
                        FieldName = request.FieldName,
                        FieldText = request.FieldText,
                        Parameters = request.Parameters
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "update_form_field", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Updates every real TX Text Control form field with fieldName in the body, headers, and footers. Supports text/date/checked/items/editable/enabled values. The call fails when no matching field exists; inspect with get_template_form_fields first when uncertain.")]
    public object UpdateFormField(UpdateFormFieldRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = FieldsCapabilityPack.UpdateFormField,
                        FieldName = request.FieldName,
                        Text = request.Text,
                        Date = request.Date,
                        Checked = request.Checked,
                        Items = request.Items,
                        Editable = request.Editable,
                        Enabled = request.Enabled
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "clear_application_fields", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Removes all ApplicationField markup, including MERGEFIELD and other application fields, from the body, headers, and footers. keepText defaults to true so visible values remain. This does not remove form fields.")]
    public object ClearApplicationFields(ClearFieldsRequest request)
        => ClearFields(request, FieldsCapabilityPack.ClearApplicationFields);

    [McpServerTool(Name = "clear_form_fields", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Removes all TX Text Control form-field markup from the body, headers, and footers. keepText defaults to true so visible values remain. This does not remove MERGEFIELD ApplicationFields.")]
    public object ClearFormFields(ClearFieldsRequest request)
        => ClearFields(request, FieldsCapabilityPack.ClearFormFields);

    private object ClearFields(ClearFieldsRequest request, string operationType)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations = [new DocumentOperation { Type = operationType, KeepText = request.KeepText }]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }
}
