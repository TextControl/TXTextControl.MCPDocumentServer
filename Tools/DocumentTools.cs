using System;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Services;

namespace TxTextControl.McpServer.Tools;

/// <summary>
/// Document/session lifecycle tools for the MCP server.
/// </summary>
[McpServerToolType]
public sealed class DocumentTools
{
    private readonly DocumentWorkflowService _workflow;

    public DocumentTools(DocumentWorkflowService workflow)
    {
        _workflow = workflow;
    }

    /// <summary>
    /// Create a new, empty document session.
    /// </summary>
    [McpServerTool, Description("Creates a new document session and initializes an empty document in TX internal format. Returns a generated sessionId that must be used in subsequent tools (for example format_text, get_text, search_text, get_as_base64, and delete_session). Use this as the starting point when you do not already have document content to load.")]
    public object CreateDocument()
    {
        try
        {
            return _workflow.CreateDocument();
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    /// <summary>
    /// Load base64 document content into a session.
    /// </summary>
    [McpServerTool, Description("Loads a base64-encoded document into a session. If sessionId is provided, the existing session document is overwritten. If sessionId is omitted, a new session is created automatically. Request body must provide request.data (base64 content). Returns the target sessionId to continue with content tools.")]
    public object LoadFromBase64(LoadFromBase64Request request, string? sessionId = null)
    {
        try
        {
            return _workflow.LoadFromBase64(request, sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    /// <summary>
    /// Export a session document as base64 in a requested output format.
    /// </summary>
    [McpServerTool, Description("Exports the current session document as base64. Required fields: request.sessionId and request.format. Supported formats: tx, docx, pdf, html, md. Returns sessionId, normalized format, and base64Document. Use this tool as the final step when the caller needs downloadable file data.")]
    public object GetAsBase64(GetAsBase64Request request)
    {
        try
        {
            return _workflow.GetAsBase64(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    /// <summary>
    /// Get basic session information.
    /// </summary>
    [McpServerTool, Description("Validates that a session exists and returns its sessionId. Useful for guard checks before content operations. If the session does not exist, a not_found error is returned.")]
    public object GetSession(string sessionId)
    {
        try
        {
            return _workflow.GetSession(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    /// <summary>
    /// Delete a session and all associated files.
    /// </summary>
    [McpServerTool, Description("Deletes a session and removes all related session files from disk. Input: sessionId. Returns { deleted, sessionId } where deleted indicates whether a session directory was removed.")]
    public object DeleteSession(string sessionId)
    {
        try
        {
            var deleted = _workflow.DeleteSession(sessionId);
            return new { deleted, sessionId };
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }
}
