using System;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Tools;

/// <summary>
/// Content/query tools for existing session documents.
/// </summary>
[McpServerToolType]
public sealed class ContentTools
{
    private readonly DocumentWorkflowService _workflow;

    public ContentTools(DocumentWorkflowService workflow)
    {
        _workflow = workflow;
    }

    [McpServerTool(Name = "extract_knowledge_blocks", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(KnowledgeExtractionResponse)),
     Description("Host-controlled reference ingestion only. Read lossless paragraphs and table rows, with repeated first-row context, from an isolated session. Required sessionId; startBlock defaults to 0. Continue with nextBlock until null. Do not use in ordinary model conversations; use inspect_document instead. Source locators are labels, not editor positions.")]
    public object ExtractKnowledgeBlocks(string sessionId, int startBlock = 0)
    {
        try { return _workflow.ExtractKnowledgeBlocks(sessionId, startBlock); }
        catch (Exception ex) { return ToolErrorMapper.Map(ex); }
    }

    [McpServerTool(Name = "inspect_document", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DocumentInspectionResponse)),
     Description("Primary token-safe read tool for questions about an uploaded or existing document. Returns one bounded paragraph chunk with stable zero-based paragraph indexes, returnedCharacters, truncated, and nextParagraphIndex. Required: request.sessionId. Optional request.query focuses the response around relevant paragraphs; omit query to read sequentially. Optional startParagraphIndex, paragraphCount, contextParagraphs, and maxCharacters support paging. maxCharacters defaults to 8000; keep it at or below 12000 during a model tool-call loop. When truncated is true, continue from nextParagraphIndex only if the question requires more content. Never call get_text to work around paging. For a question, answer only from returned content and do not call any mutation or creation tool.")]
    public object InspectDocument(InspectDocumentRequest request)
    {
        try
        {
            return _workflow.InspectDocument(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool(
        Name = "classify_document",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DocumentCategoryResponse)),
     Description("Fast deterministic classification of the live document. Required: request.sessionId. Returns one broad category (Legal, Healthcare, Accounting, Finance, Human Resources, Sales & Marketing, Technical, Education, Transportation, or General), confidence, matching signals, and category-specific suggested actions with ready-to-use prompts. Use this to populate document quick actions; summary remains a universal client action. This tool inspects only the current session and does not call an LLM or modify the document.")]
    public object ClassifyDocument(ClassifyDocumentRequest request)
    {
        try
        {
            return _workflow.ClassifyDocument(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool(Name = "edit_document", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DocumentEditResponse)),
     Description("Primary deterministic text-edit tool for an existing session. Inspect first to obtain exact text or paragraph indexes. Required: request.sessionId and request.replacementText. Select exactly one target: matchText (optionally occurrenceIndex or replaceAll), paragraphIndex, startParagraphIndex plus optional endParagraphIndex, or start plus length. For start/length edits, pass expectedText from the inspection or active editor selection; the server rejects stale or invented coordinates when the current range differs. It changes only the selected text and never creates or regenerates a document. Success returns changed=true, editsApplied greater than zero, and the committed revision. Do not claim success from the request alone. When the user names a clause or part by heading, prefer inspect_document_section followed by replace_document_section. For formatting-only or structural edits, use format_text or advanced apply_operations after inspection.")]
    public object EditDocument(EditDocumentRequest request)
    {
        try
        {
            return _workflow.EditDocument(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Applies character appearance to either (a) an explicit MCP text range or (b) all characters in one paragraph by zero-based MCP paragraph index. Required: sessionId and request payload. Range mode requires request.start and request.length. Paragraph mode requires request.paragraphIndex, where the first paragraph is 0 and the second is 1. Do not combine range and paragraph inputs. Supported properties: bold, italic, underline, color_hex, font_name, font_size (points). This tool does not change paragraph alignment or spacing; use format_paragraph for those requests. For every/all occurrence of text, use format_text_occurrences.")]
    public object FormatText(string sessionId, FormatTextRequest request)
    {
        try
        {
            return _workflow.FormatText(sessionId, request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool(
        Name = "format_text_occurrences",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Primary tool for requests to format every/all occurrence of text. Required: sessionId, matchText, and at least one of bold, italic, underline, colorHex, fontName, or fontSize. The server uses TX Text Control Find and applies formatting atomically to authoritative server ranges; do not call search_text_ranges or format_text repeatedly. Set wholeWord=true when the user names a word, so matching 'information' does not also format it inside a larger word. Optional matchCase and maxOccurrences narrow the matches. fontSizeUnit is pt by default. Success returns one applied operation whose occurrenceCount is the number actually formatted; never claim a different count.")]
    public object FormatTextOccurrences(FormatTextOccurrencesRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.MatchText);
            if (!request.Bold.HasValue
                && !request.Italic.HasValue
                && !request.Underline.HasValue
                && string.IsNullOrWhiteSpace(request.ColorHex)
                && string.IsNullOrWhiteSpace(request.FontName)
                && !request.FontSize.HasValue)
            {
                throw new ArgumentException("At least one formatting property is required.", nameof(request));
            }

            return _workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = BasicTextCapabilityPack.FormatTextOccurrences,
                        MatchText = request.MatchText,
                        Style = new TextStyleDefinition
                        {
                            Bold = request.Bold,
                            Italic = request.Italic,
                            Underline = request.Underline,
                            ColorHex = request.ColorHex,
                            FontName = request.FontName,
                            FontSize = request.FontSize,
                            FontSizeUnit = request.FontSizeUnit
                        },
                        MatchCase = request.MatchCase,
                        WholeWord = request.WholeWord,
                        MaxOccurrences = request.MaxOccurrences
                    }
                ]
            });
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Advanced compatibility read tool. Prefer inspect_document for ordinary questions and edit planning. Reads paragraph text values from a session document with optional start/end indexes.")]
    public object GetParagraphs(string sessionId, int? start = null, int? end = null)
    {
        try
        {
            return _workflow.GetParagraphs(sessionId, start, end);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Searches paragraph text and returns paragraph indices where matches occur. Input: sessionId, text, optional matchCase, optional wholeWord. This is paragraph-level matching, not character-position matching. Use search_text_ranges when exact character offsets are required.")]
    public object SearchText(string sessionId, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        try
        {
            return _workflow.SearchText(sessionId, text, matchCase, wholeWord);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Searches text and returns exact match ranges as (start, length) using TX Text Control Find. Input: sessionId, text, optional matchCase, optional wholeWord. Returned values are authoritative server selection coordinates safe for format_text, edit_document, insert_merge_field, insert_form_field, and merge-block ranges, including text inside tables. Do not substitute browser-editor offsets.")]
    public object SearchTextRanges(string sessionId, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        try
        {
            return _workflow.SearchTextRanges(sessionId, text, matchCase, wholeWord);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Advanced compatibility read tool returning complete plain text. Prefer inspect_document because it includes stable paragraph indexes, query focus, and paging.")]
    public object GetText(string sessionId)
    {
        try
        {
            return _workflow.GetText(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }
}
