using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Simplified document workflow service.
/// </summary>
public sealed class DocumentWorkflowService
{
    private readonly DocumentSessionService _sessions;
    private readonly ITxDocumentEngine _engine;

    public DocumentWorkflowService(
        DocumentSessionService sessions,
        ITxDocumentEngine engine)
    {
        _sessions = sessions;
        _engine = engine;
    }

    /// <summary>
    /// Create a new document session with a generated session id.
    /// </summary>
    public DocumentResponse CreateDocument()
    {
        var session = _sessions.Create();
        var state = _engine.CreateEmpty(session.WorkingDocumentPath);
        _sessions.SaveState(session, state);

        return new DocumentResponse
        {
            SessionId = session.SessionId
        };
    }

    /// <summary>
    /// Load a base64-encoded document into an existing session document (overwrite).
    /// </summary>
    public DocumentResponse LoadFromBase64(LoadFromBase64Request request, string? sessionId = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Data))
        {
            throw new ArgumentException("'data' is required.", nameof(request));
        }

        var session = string.IsNullOrWhiteSpace(sessionId)
            ? _sessions.Create()
            : _sessions.Get(sessionId);

        var state = _engine.LoadFromBase64(request.Data, session.WorkingDocumentPath);
        _sessions.SaveState(session, state);

        return new DocumentResponse
        {
            SessionId = session.SessionId
        };
    }

    /// <summary>
    /// Return the current session document as base64-encoded DOCX.
    /// </summary>
    public DocumentBase64Response GetAsBase64(GetAsBase64Request request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Format))
        {
            throw new ArgumentException("'format' is required.", nameof(request));
        }

        var allowed = new[] { "tx", "docx", "pdf", "html", "md" };
        if (!allowed.Contains(request.Format, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'format' must be one of: tx, docx, pdf, html, md.", nameof(request));
        }

        var session = _sessions.Get(request.SessionId);
        var normalizedFormat = request.Format.Trim().ToLowerInvariant();

        return new DocumentBase64Response
        {
            SessionId = session.SessionId,
            Format = normalizedFormat,
            Base64Document = _engine.GetAsBase64(session.WorkingDocumentPath, normalizedFormat)
        };
    }

    /// <summary>
    /// Format the entire text in the session document.
    /// </summary>
    public DocumentResponse FormatText(string sessionId, FormatTextRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var session = _sessions.Get(sessionId);
        var state = _engine.FormatText(session.WorkingDocumentPath, request);
        _sessions.SaveState(session, state);

        return new DocumentResponse
        {
            SessionId = session.SessionId
        };
    }

    /// <summary>
    /// Read paragraph texts for an existing session and optional start/end range.
    /// </summary>
    public ParagraphListResponse GetParagraphs(string sessionId, int? start = null, int? end = null)
    {
        var session = _sessions.Get(sessionId);

        return new ParagraphListResponse
        {
            SessionId = session.SessionId,
            Paragraphs = _engine.GetParagraphs(session.WorkingDocumentPath, start, end).ToList()
        };
    }

    /// <summary>
    /// Search for text in the session document.
    /// </summary>
    public SearchTextResponse SearchText(string sessionId, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        var session = _sessions.Get(sessionId);

        return new SearchTextResponse
        {
            SessionId = session.SessionId,
            Matches = _engine.SearchText(session.WorkingDocumentPath, text, matchCase, wholeWord).ToList()
        };
    }

    /// <summary>
    /// Search for text in the session document and return index ranges.
    /// </summary>
    public SearchTextRangesResponse SearchTextRanges(string sessionId, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        var session = _sessions.Get(sessionId);

        return new SearchTextRangesResponse
        {
            SessionId = session.SessionId,
            Matches = _engine.SearchTextRanges(session.WorkingDocumentPath, text, matchCase, wholeWord).ToList()
        };
    }

    /// <summary>
    /// Get the complete text of the document in the session.
    /// </summary>
    public DocumentTextResponse GetText(string sessionId)
    {
        var session = _sessions.Get(sessionId);

        return new DocumentTextResponse
        {
            SessionId = session.SessionId,
            Text = _engine.GetText(session.WorkingDocumentPath)
        };
    }

    /// <summary>
    /// Get session information.
    /// </summary>
    public DocumentResponse GetSession(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        return new DocumentResponse
        {
            SessionId = session.SessionId
        };
    }

    /// <summary>
    /// Delete a session.
    /// </summary>
    public bool DeleteSession(string sessionId)
    {
        return _sessions.Delete(sessionId);
    }
}
