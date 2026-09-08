using System;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Services;
using Microsoft.AspNetCore.Http;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Tools;

/// <summary>
/// Document/session lifecycle tools for the MCP server.
/// </summary>
[McpServerToolType]
public sealed class DocumentTools
{
    private readonly DocumentWorkflowService _workflow;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DocumentTools(DocumentWorkflowService workflow, IHttpContextAccessor httpContextAccessor)
    {
        _workflow = workflow;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Create a new, empty document session.
    /// </summary>
    [McpServerTool(Name = "create_empty_document"), Description("Advanced lifecycle tool: creates an intentionally empty session with the configured default page layout. Do not use it for a user request to create a complete document; use create_document or a matching server-owned recipe instead.")]
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
    [McpServerTool(Name = "load_document"), Description("Loads one uploaded base64 document without changing its content. Required: request.data. Optional request.sourceFormat: auto, tx, rtf, docx, html, pdf, md, or txt; specify it when known for deterministic import. Optional sessionId overwrites that session, otherwise a new session is created. Returns sessionId. After loading: use inspect_document for questions, edit_document for text changes, or convert_document for conversion.")]
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
    [McpServerTool, Description("Exports the current session document as base64. Required fields: request.sessionId and request.format. Supported formats: tx, rtf, docx, pdf, html, md, txt. Returns sessionId, normalized format, and base64Document. Use this tool as the final step when the caller needs inline file data.")]
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

    [McpServerTool(
        Name = "convert_document",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DocumentExportResponse)),
     Description("Converts a document with TX Text Control and performs no inspection, generation, rewriting, or styling. Provide exactly one source: request.sessionId for an already loaded document, or request.data plus optional request.sourceFormat for an uploaded document. Required request.outputFormat: tx, rtf, docx, pdf, html, md, or txt. Optional request.fileName. For 'convert X to Y', call only this tool and return its downloadUri.")]
    public object ConvertDocument(ConvertDocumentRequest request)
    {
        try
        {
            DocumentExportResponse response = _workflow.ConvertDocument(request);
            SetDownloadUri(response);
            return response;
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    /// <summary>
    /// Creates a document export that clients can download without Base64 encoding.
    /// </summary>
    [McpServerTool(
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DocumentExportResponse)),
     Description("Creates a server-side document export and returns a compact HTTPS/HTTP download link plus file metadata. Required: request.sessionId and request.format. Optional: request.fileName. Supported formats: tx, rtf, docx, pdf, html, md, txt. Prefer this over get_as_base64 for AI and high-performance clients.")]
    public object CreateDocumentExport(CreateDocumentExportRequest request)
    {
        try
        {
            DocumentExportResponse response = _workflow.CreateDocumentExport(request);
            SetDownloadUri(response);
            return response;
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


    private void SetDownloadUri(DocumentExportResponse response)
    {
        HttpRequest httpRequest = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("The HTTP request context is unavailable.");
        response.DownloadUri = $"{httpRequest.Scheme}://{httpRequest.Host}{httpRequest.PathBase}/exports/{Uri.EscapeDataString(response.SessionId)}/{response.ExportId}";
    }
}
