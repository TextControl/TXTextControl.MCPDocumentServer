using System;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Services;

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

    [McpServerTool, Description("Applies character formatting to either (a) an explicit text range or (b) one paragraph by index. Required: sessionId and request payload. Range mode requires request.start and request.length. Paragraph mode requires request.paragraphIndex. Do not combine range and paragraph inputs in one call. Supported properties: bold, italic, underline, color_hex, font_name, font_size (points). Returns sessionId after saving changes.")]
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

    [McpServerTool, Description("Reads paragraph text values from a session document. Input: sessionId, optional start and end for slicing by paragraph index. Returns sessionId and paragraphs array. If start/end are omitted, all paragraphs are returned.")]
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

    [McpServerTool, Description("Searches text and returns exact match ranges as (start, length) using TX Text Control Find. Input: sessionId, text, optional matchCase, optional wholeWord. Returns sessionId and matches array with character offsets in document text coordinates.")]
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

    [McpServerTool, Description("Returns the complete plain text content of the session document. Input: sessionId. Useful for summarization, validation, and follow-up search/planning steps.")]
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
