using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Simplified document workflow service.
/// </summary>
public sealed class DocumentWorkflowService
{
    private readonly DocumentSessionService _sessions;
    private readonly ITxDocumentEngine _engine;
    private readonly DocumentAutomationOptions _automationOptions;

    private static readonly IReadOnlyDictionary<string, (string Term, int Weight)[]> CategorySignals =
        new Dictionary<string, (string, int)[]>(StringComparer.Ordinal)
        {
            ["Legal"] = [("agreement", 3), ("contract", 3), ("party", 2), ("confidential", 3), ("liability", 3), ("indemn", 4), ("warranty", 3), ("governing law", 4), ("termination", 2), ("hereby", 2), ("clause", 2)],
            ["Healthcare"] = [("patient", 4), ("diagnosis", 4), ("medical", 3), ("clinical", 3), ("medication", 3), ("treatment", 3), ("healthcare", 4), ("physician", 3), ("procedure", 2), ("hipaa", 5)],
            ["Accounting"] = [("invoice", 5), ("subtotal", 4), ("tax", 2), ("balance due", 4), ("accounts payable", 5), ("debit", 3), ("credit", 3), ("ledger", 4), ("line total", 3), ("payment terms", 3)],
            ["Finance"] = [("revenue", 3), ("cash flow", 4), ("financial statement", 5), ("investment", 3), ("portfolio", 4), ("asset", 2), ("liability", 2), ("forecast", 2), ("ebitda", 5), ("fiscal", 3)],
            ["Human Resources"] = [("employee", 4), ("employment", 4), ("human resources", 5), ("leave policy", 4), ("benefits", 2), ("compensation", 3), ("onboarding", 4), ("performance review", 4), ("workplace", 2)],
            ["Sales & Marketing"] = [("customer", 2), ("campaign", 4), ("marketing", 4), ("sales", 3), ("brand", 2), ("audience", 2), ("conversion", 3), ("value proposition", 4), ("lead", 2), ("market", 2)],
            ["Technical"] = [("software", 3), ("api", 4), ("architecture", 3), ("implementation", 2), ("deployment", 3), ("system", 2), ("database", 3), ("requirement", 2), ("security", 2), ("configuration", 2)],
            ["Education"] = [("student", 4), ("learning objective", 5), ("curriculum", 4), ("lesson", 3), ("course", 3), ("assessment", 2), ("instructor", 3), ("education", 3), ("syllabus", 5)],
            ["Transportation"] = [("transportation", 5), ("shipment", 4), ("freight", 4), ("carrier", 3), ("route", 2), ("fleet", 4), ("logistics", 4), ("delivery", 2), ("vehicle", 3), ("cargo", 3), ("warehouse", 2), ("transit", 2)]
        };

    private static readonly IReadOnlyDictionary<string, CategoryActionDefinition[]> CategoryActions =
        new Dictionary<string, CategoryActionDefinition[]>(StringComparer.Ordinal)
        {
            ["General"] =
            [
                new("key-points", "Extract key points", "Surface the most important information.", "Extract the key points from this document and organize them clearly."),
                new("action-list", "Create action list", "Turn decisions into next steps.", "Create a concise action list from this document, including owners or dates when stated."),
                new("simplify", "Simplify language", "Identify passages that could be clearer.", "Review this document and identify language that should be simplified, with suggested wording.")
            ],
            ["Legal"] =
            [
                new("risk-assessment", "Create risk assessment", "Identify exposure, ambiguity, and unfavorable terms.", "Create a legal risk assessment of this document. List each material risk, its severity, the affected clause, and a practical mitigation."),
                new("obligations", "Identify obligations", "Map duties, rights, and deadlines by party.", "Identify the obligations, rights, deadlines, and remedies for each party in this document."),
                new("legal-dates", "Extract dates & parties", "Collect defined parties and important dates.", "Extract all parties, effective dates, renewal dates, notice periods, and termination dates from this document."),
                new("clause-review", "Review key clauses", "Focus on liability, warranty, payment, and termination.", "Review the liability, warranty, payment, confidentiality, and termination clauses and flag unusual or missing protections.")
            ],
            ["Healthcare"] =
            [
                new("clinical-summary", "Create clinical summary", "Summarize findings and care information.", "Create a concise clinical summary using only information stated in this document."),
                new("care-items", "Extract care items", "List medications, procedures, and follow-ups.", "Extract medications, procedures, diagnoses, follow-up actions, and dates from this document."),
                new("missing-information", "Check missing information", "Identify absent or ambiguous patient details.", "Identify missing, inconsistent, or ambiguous healthcare information in this document. Do not infer facts."),
                new("patient-language", "Create patient version", "Explain the content in accessible language.", "Explain this document in clear patient-friendly language while preserving important cautions.")
            ],
            ["Accounting"] =
            [
                new("verify-totals", "Review totals", "Check amounts, tax, and balance relationships.", "Review the line items, subtotal, tax, total, and balance due. Report inconsistencies without inventing missing values."),
                new("extract-line-items", "Extract line items", "Create a structured view of billed items.", "Extract all invoice line items with description, quantity, unit price, and line total in a table."),
                new("payment-review", "Review payment terms", "Surface due dates, discounts, and penalties.", "Summarize payment terms, due dates, discounts, penalties, currencies, and payment instructions."),
                new("accounting-anomalies", "Find anomalies", "Flag duplicates and unusual values.", "Identify possible duplicates, missing values, inconsistent amounts, or unusual accounting entries in this document.")
            ],
            ["Finance"] =
            [
                new("financial-summary", "Financial summary", "Summarize position and performance.", "Summarize the financial position, performance, and major drivers stated in this document."),
                new("key-figures", "Extract key figures", "Collect important financial metrics.", "Extract the key financial figures, periods, currencies, and comparisons into a clear table."),
                new("financial-risks", "Assess financial risks", "Identify material risks and assumptions.", "Assess financial risks, assumptions, dependencies, and uncertainties stated in this document."),
                new("period-comparison", "Compare periods", "Explain significant changes over time.", "Compare the reported periods and explain the largest changes using only document data.")
            ],
            ["Human Resources"] =
            [
                new("policy-summary", "Summarize policy", "Explain the policy and who it affects.", "Summarize this HR document, who it applies to, and its main rules."),
                new("employee-obligations", "Employee obligations", "List employee and employer responsibilities.", "Extract employee and employer obligations, deadlines, approvals, and escalation paths."),
                new("hr-compliance", "Review compliance risks", "Flag ambiguity and inconsistent treatment.", "Review this document for HR compliance risks, ambiguity, inconsistent treatment, and missing process steps."),
                new("onboarding-checklist", "Create checklist", "Turn requirements into an actionable checklist.", "Create an onboarding or compliance checklist from this document with clear action items.")
            ],
            ["Sales & Marketing"] =
            [
                new("value-propositions", "Find value propositions", "Surface benefits and differentiators.", "Extract the main value propositions, customer benefits, proof points, and differentiators."),
                new("customer-needs", "Customer requirements", "Summarize needs, objections, and commitments.", "Summarize customer requirements, objections, commitments, and open questions in this document."),
                new("sales-followup", "Create follow-up", "Produce concrete sales next steps.", "Create a prioritized sales follow-up plan based on this document."),
                new("audience-rewrite", "Adapt for audience", "Recommend messaging improvements.", "Review the document for its intended audience and recommend clearer, more persuasive messaging.")
            ],
            ["Technical"] =
            [
                new("requirements", "Extract requirements", "List functional and non-functional needs.", "Extract functional requirements, non-functional requirements, constraints, and acceptance criteria."),
                new("dependencies", "Map dependencies", "Identify systems, teams, and prerequisites.", "Map technical dependencies, integrations, prerequisites, and responsible components from this document."),
                new("technical-risks", "Assess technical risks", "Flag security, scale, and delivery risks.", "Assess technical, security, scalability, reliability, and delivery risks stated or implied by this document."),
                new("implementation-plan", "Implementation checklist", "Turn the specification into steps.", "Create an implementation checklist from this document, ordered by dependency." )
            ],
            ["Education"] =
            [
                new("study-summary", "Create study summary", "Condense the material for review.", "Create a structured study summary of this document with the most important concepts."),
                new("learning-objectives", "Learning objectives", "Extract intended outcomes.", "Extract or infer clearly labeled learning objectives from this document."),
                new("review-questions", "Generate questions", "Create questions for self-assessment.", "Generate review questions and a separate answer key using only this document."),
                new("explain-simply", "Explain simply", "Make difficult concepts approachable.", "Explain the difficult concepts in this document in clear, accessible language.")
            ],
            ["Transportation"] =
            [
                new("shipment-status", "Summarize shipment status", "Surface routes, milestones, delays, and owners.", "Summarize every shipment, route, milestone, delay, delivery commitment, and responsible carrier stated in this document."),
                new("route-risks", "Assess route risks", "Identify operational and delivery exposure.", "Assess route, capacity, weather, customs, handoff, and delivery risks in this document. Rank each risk and recommend a mitigation."),
                new("fleet-actions", "Create operations plan", "Turn transport details into next actions.", "Create a prioritized transportation operations plan with owners, deadlines, fleet or carrier dependencies, and escalation points."),
                new("transport-compliance", "Review compliance", "Check safety and regulatory requirements.", "Review the transportation, cargo, vehicle, driver, customs, safety, and regulatory requirements stated in this document and flag missing information.")
            ]
        };

    private sealed record CategoryActionDefinition(
        string Id,
        string Title,
        string Description,
        string Prompt);

    public DocumentWorkflowService(
        DocumentSessionService sessions,
        ITxDocumentEngine engine,
        IOptions<DocumentAutomationOptions> automationOptions)
    {
        _sessions = sessions;
        _engine = engine;
        _automationOptions = automationOptions.Value;
    }

    /// <summary>
    /// Create a new document session with a generated session id.
    /// </summary>
    public DocumentResponse CreateDocument()
    {
        var session = _sessions.Create();
        var state = _engine.CreateEmpty(session.WorkingDocumentPath);

        if (_automationOptions.DefaultPageLayout is not null)
        {
            state = _engine.ApplyOperations(
                session.WorkingDocumentPath,
                state,
                new ApplyOperationsRequest
                {
                    SessionId = session.SessionId,
                    Operations =
                    [
                        new DocumentOperation
                        {
                            Type = SectionCapabilityPack.SetSectionLayout,
                            SectionIndex = 0,
                            PageLayout = _automationOptions.DefaultPageLayout
                        }
                    ]
                });
        }

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

        lock (session.SyncRoot)
        {
            var existingState = _sessions.LoadState(session);
            var state = _engine.LoadFromBase64(
                request.Data,
                session.WorkingDocumentPath,
                existingState,
                request.SourceFormat);
            if (!ReferenceEquals(state, existingState))
            {
                _sessions.SaveState(session, state);
            }
            else
            {
                session.LastAccessUtc = DateTime.UtcNow;
            }
        }

        return new DocumentResponse
        {
            SessionId = session.SessionId
        };
    }

    /// <summary>
    /// Applies the configured page and paragraph style presets to an imported document without
    /// changing its text or recreating its session.
    /// </summary>
    public ApplyDocumentPresetStylesResponse ApplyDocumentPresetStyles(
        ApplyDocumentPresetStylesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(request));
        }

        DocumentSession session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            DocumentState state = _sessions.LoadState(session);
            DocumentPresetStyleEngineResult result = _engine.ApplyPresetStyles(
                session.WorkingDocumentPath,
                state);
            _sessions.SaveState(session, result.State);
            return CreatePresetStyleResponse(session.SessionId, result);
        }
    }

    /// <summary>
    /// Creates a new document directly from semantic Markdown and applies all configured presets.
    /// </summary>
    public CreateDocumentFromMarkdownResponse CreateDocumentFromMarkdown(
        CreateDocumentFromMarkdownRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            throw new ArgumentException("'markdown' is required.", nameof(request));
        }

        const int maxMarkdownCharacters = 2_000_000;
        if (request.Markdown.Length > maxMarkdownCharacters)
        {
            throw new ArgumentException(
                $"'markdown' must not exceed {maxMarkdownCharacters:N0} characters.",
                nameof(request));
        }

        DocumentSession session = _sessions.Create();
        try
        {
            lock (session.SyncRoot)
            {
                DocumentPresetStyleEngineResult result = _engine.LoadMarkdownWithPresetStyles(
                    request.Markdown,
                    session.WorkingDocumentPath);
                _sessions.SaveState(session, result.State);
                return CreateMarkdownResponse(session.SessionId, result);
            }
        }
        catch
        {
            _sessions.Delete(session.SessionId);
            throw;
        }
    }

    private static CreateDocumentFromMarkdownResponse CreateMarkdownResponse(
        string sessionId,
        DocumentPresetStyleEngineResult result)
        => new()
        {
            SessionId = sessionId,
            ParagraphsStyled = result.AppliedStyles.Values.Sum(),
            AppliedStyles = result.AppliedStyles,
            TablesStyled = result.TablesStyled,
            PageLayoutApplied = result.PageLayoutApplied,
            Warnings = result.Warnings.Distinct(StringComparer.Ordinal).ToList()
        };

    private static ApplyDocumentPresetStylesResponse CreatePresetStyleResponse(
        string sessionId,
        DocumentPresetStyleEngineResult result)
        => new()
        {
            SessionId = sessionId,
            ParagraphsStyled = result.AppliedStyles.Values.Sum(),
            AppliedStyles = result.AppliedStyles,
            TablesStyled = result.TablesStyled,
            PageLayoutApplied = result.PageLayoutApplied,
            Warnings = result.Warnings.Distinct(StringComparer.Ordinal).ToList()
        };

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

        var allowed = new[] { "tx", "rtf", "docx", "pdf", "html", "md", "txt" };
        if (!allowed.Contains(request.Format, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("'format' must be one of: tx, rtf, docx, pdf, html, md, txt.", nameof(request));
        }

        var session = _sessions.Get(request.SessionId);
        var normalizedFormat = request.Format.Trim().ToLowerInvariant();

        lock (session.SyncRoot)
        {
            return new DocumentBase64Response
            {
                SessionId = session.SessionId,
                Format = normalizedFormat,
                Base64Document = _engine.GetAsBase64(session.WorkingDocumentPath, normalizedFormat)
            };
        }
    }

    /// <summary>
    /// Apply a sequence of AI-friendly document operations to a session.
    /// </summary>
    public ApplyOperationsResponse ApplyOperations(ApplyOperationsRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Operations is null || request.Operations.Count == 0)
        {
            throw new ArgumentException("'operations' must contain at least one operation.", nameof(request));
        }

        DocumentModelQualityNormalizer.ValidateNewDraftOperations(request);

        DocumentSession session;
        bool createdNewSession = false;
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!request.CreateIfMissing)
            {
                throw new ArgumentException("'sessionId' is required when createIfMissing is false.", nameof(request));
            }

            session = _sessions.Create();
            createdNewSession = true;
        }
        else
        {
            try
            {
                session = _sessions.Get(request.SessionId);
            }
            catch (FileNotFoundException) when (request.CreateIfMissing)
            {
                session = _sessions.Create(request.SessionId);
                createdNewSession = true;
            }
        }

        if (createdNewSession
            && _automationOptions.DefaultPageLayout is not null
            && !request.Operations.Any(operation => string.Equals(
                operation.Type,
                SectionCapabilityPack.SetSectionLayout,
                StringComparison.OrdinalIgnoreCase)))
        {
            request.Operations.Insert(0, new DocumentOperation
            {
                Type = SectionCapabilityPack.SetSectionLayout,
                SectionIndex = 0,
                PageLayout = _automationOptions.DefaultPageLayout
            });
        }

        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            var updatedState = _engine.ApplyOperations(session.WorkingDocumentPath, state, request);
            _sessions.SaveState(session, updatedState);

            return new ApplyOperationsResponse
            {
                SessionId = session.SessionId,
                Results = updatedState.LastOperationResults.ToList()
            };
        }
    }

    /// <summary>
    /// Render a neutral AI-facing document model into a real TX document.
    /// </summary>
    public ApplyOperationsResponse RenderDocumentModel(RenderDocumentModelRequest request)
    {
        var compiled = DocumentModelOperationCompiler.CompileDetailed(request, _automationOptions);
        var response = ApplyOperations(compiled.Request);
        var session = _sessions.Get(response.SessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);

            if (!string.IsNullOrWhiteSpace(request.Document?.Id))
            {
                state.Document.Id = request.Document.Id;
            }

            if (!string.IsNullOrWhiteSpace(request.Document?.Title))
            {
                state.Document.Title = request.Document.Title;
            }

            state.Styles = ExtractStyles(state.Document);
            _sessions.SaveState(session, state, documentChanged: false);
        }
        response.Warnings.AddRange(compiled.Warnings);

        return response;
    }

    /// <summary>
    /// Creates a server-side export artifact that can be transferred as a binary HTTP stream.
    /// </summary>
    public DocumentExportResponse CreateDocumentExport(CreateDocumentExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string normalizedFormat = ValidateExportRequest(request.SessionId, request.Format);
        var session = _sessions.Get(request.SessionId);
        string exportId = Guid.NewGuid().ToString("N");

        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            string fileName = CreateSafeExportFileName(
                request.FileName,
                normalizedFormat,
                state.Document?.Title);
            string exportDirectory = Path.Combine(session.WorkingDirectory, "exports", exportId);
            string exportPath = Path.Combine(exportDirectory, fileName);
            _engine.ExportToFile(session.WorkingDocumentPath, exportPath, normalizedFormat);
            return new DocumentExportResponse
            {
                SessionId = session.SessionId,
                ExportId = exportId,
                Format = normalizedFormat,
                FileName = fileName,
                MimeType = GetMimeType(normalizedFormat),
                ByteCount = new FileInfo(exportPath).Length
            };
        }
    }

    /// <summary>
    /// Converts uploaded content or an existing session without invoking authoring operations.
    /// </summary>
    public DocumentExportResponse ConvertDocument(ConvertDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool hasData = !string.IsNullOrWhiteSpace(request.Data);
        bool hasSession = !string.IsNullOrWhiteSpace(request.SessionId);
        if (hasData == hasSession)
        {
            throw new ArgumentException("Provide exactly one source: data or sessionId.", nameof(request));
        }

        if (!hasData)
        {
            return CreateDocumentExport(new CreateDocumentExportRequest
            {
                SessionId = request.SessionId!,
                Format = request.OutputFormat,
                FileName = request.FileName
            });
        }

        string normalizedFormat = ValidateExportRequest("conversion", request.OutputFormat);
        DocumentSession session = _sessions.Create();
        string exportId = Guid.NewGuid().ToString("N");
        string fileName = CreateSafeExportFileName(request.FileName, normalizedFormat);
        string exportDirectory = Path.Combine(session.WorkingDirectory, "exports", exportId);
        string exportPath = Path.Combine(exportDirectory, fileName);

        try
        {
            lock (session.SyncRoot)
            {
                DocumentState state = _engine.ConvertFromBase64ToFile(
                    request.Data!,
                    request.SourceFormat,
                    session.WorkingDocumentPath,
                    exportPath,
                    normalizedFormat);
                _sessions.SaveState(session, state);
            }
        }
        catch
        {
            _sessions.Delete(session.SessionId);
            throw;
        }

        return new DocumentExportResponse
        {
            SessionId = session.SessionId,
            ExportId = exportId,
            Format = normalizedFormat,
            FileName = fileName,
            MimeType = GetMimeType(normalizedFormat),
            ByteCount = new FileInfo(exportPath).Length
        };
    }

    /// <summary>Resolves an export artifact for streaming.</summary>
    public DocumentExportFile GetDocumentExport(string sessionId, string exportId)
    {
        if (!Guid.TryParseExact(exportId, "N", out _))
        {
            throw new ArgumentException("'exportId' is invalid.", nameof(exportId));
        }

        var session = _sessions.Get(sessionId);
        string exportDirectory = Path.Combine(session.WorkingDirectory, "exports", exportId);
        string? exportPath = Directory.Exists(exportDirectory)
            ? Directory.EnumerateFiles(exportDirectory).SingleOrDefault()
            : null;
        if (exportPath is null)
        {
            throw new FileNotFoundException($"Export '{exportId}' was not found.");
        }

        string extension = Path.GetExtension(exportPath).TrimStart('.').ToLowerInvariant();
        return new DocumentExportFile
        {
            Path = exportPath,
            FileName = Path.GetFileName(exportPath),
            MimeType = GetMimeType(extension)
        };
    }

    private static string ValidateExportRequest(string sessionId, string format)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            throw new ArgumentException("'format' is required.", nameof(format));
        }

        string normalized = format.Trim().ToLowerInvariant();
        return normalized is "tx" or "rtf" or "docx" or "pdf" or "html" or "md" or "txt"
            ? normalized
            : throw new ArgumentException("'format' must be one of: tx, rtf, docx, pdf, html, md, txt.", nameof(format));
    }

    private static string CreateSafeExportFileName(
        string? requestedName,
        string format,
        string? documentTitle = null)
    {
        string name = string.IsNullOrWhiteSpace(requestedName)
            ? string.IsNullOrWhiteSpace(documentTitle)
                ? $"document-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{format}"
                : $"{documentTitle.Trim()}.{format}"
            : Path.GetFileName(requestedName.Trim());
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        return Path.GetExtension(name).Equals($".{format}", StringComparison.OrdinalIgnoreCase)
            ? name
            : Path.ChangeExtension(name, format);
    }

    private static string GetMimeType(string format) => format switch
    {
        "pdf" => "application/pdf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "html" => "text/html; charset=utf-8",
        "md" => "text/markdown; charset=utf-8",
        "rtf" => "application/rtf",
        "txt" => "text/plain; charset=utf-8",
        "tx" => "application/vnd.textcontrol.tx",
        _ => "application/octet-stream"
    };

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
        lock (session.SyncRoot)
        {
            var existingState = _sessions.LoadState(session);
            var state = _engine.FormatText(session.WorkingDocumentPath, request);
            state.Document = existingState.Document;
            state.Styles = existingState.Styles;
            _sessions.SaveState(session, state);

            return new DocumentResponse
            {
                SessionId = session.SessionId
            };
        }
    }

    /// <summary>
    /// Read paragraph texts for an existing session and optional start/end range.
    /// </summary>
    public ParagraphListResponse GetParagraphs(string sessionId, int? start = null, int? end = null)
    {
        var session = _sessions.Get(sessionId);

        lock (session.SyncRoot)
        {
            IReadOnlyList<string> paragraphs = GetCachedContent(session).Paragraphs;
            int sliceStart = Math.Clamp(start ?? 0, 0, paragraphs.Count);
            int sliceEnd = Math.Clamp(end ?? paragraphs.Count, sliceStart, paragraphs.Count);
            return new ParagraphListResponse
            {
                SessionId = session.SessionId,
                Paragraphs = paragraphs.Skip(sliceStart).Take(sliceEnd - sliceStart).ToList()
            };
        }
    }

    /// <summary>
    /// Search for text in the session document.
    /// </summary>
    public SearchTextResponse SearchText(string sessionId, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        var session = _sessions.Get(sessionId);

        lock (session.SyncRoot)
        {
            string query = text ?? string.Empty;
            var matches = string.IsNullOrEmpty(query)
                ? []
                : GetCachedContent(session).Paragraphs
                    .Select((paragraph, index) => new { paragraph, index })
                    .Where(item => ParagraphContains(item.paragraph, query, matchCase, wholeWord))
                    .Select(item => item.index)
                    .ToList();
            return new SearchTextResponse
            {
                SessionId = session.SessionId,
                Matches = matches
            };
        }
    }

    /// <summary>
    /// Search for text in the session document and return index ranges.
    /// </summary>
    public SearchTextRangesResponse SearchTextRanges(string sessionId, string text = "", bool matchCase = false, bool wholeWord = false)
    {
        var session = _sessions.Get(sessionId);

        lock (session.SyncRoot)
        {
            string query = text ?? string.Empty;
            return new SearchTextRangesResponse
            {
                SessionId = session.SessionId,
                // Character positions in tx.Text are not interchangeable with TX selection positions when
                // tables or other structural elements are present. ServerTextControl.Find returns coordinates
                // that can safely be passed back to range-based mutation APIs.
                Matches = string.IsNullOrEmpty(query)
                    ? []
                    : _engine.SearchTextRanges(session.WorkingDocumentPath, query, matchCase, wholeWord).ToList()
            };
        }
    }

    /// <summary>
    /// Get the complete text of the document in the session.
    /// </summary>
    public DocumentTextResponse GetText(string sessionId)
    {
        var session = _sessions.Get(sessionId);

        lock (session.SyncRoot)
        {
            return new DocumentTextResponse
            {
                SessionId = session.SessionId,
                Text = GetCachedContent(session).Text
            };
        }
    }

    /// <summary>
    /// Get the neutral AI-facing document model for a session.
    /// </summary>
    public DocumentModelResponse GetDocumentModel(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            return new DocumentModelResponse
            {
                SessionId = session.SessionId,
                Document = state.Document
            };
        }
    }

    /// <summary>
    /// Get an AI-friendly structural summary for the session document.
    /// </summary>
    public DocumentStructureResponse GetDocumentStructure(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            var document = state.Document ?? new Document();
            var sections = BuildSectionInspections(document);
            DocumentContentSnapshot liveContent = GetCachedContent(session);

            if (sections.Count == 0 || sections.All(section => section.Blocks.Count == 0))
            {
                var paragraphBlocks = liveContent.Paragraphs
                    .Select((text, index) => new DocumentBlockInspection
                    {
                        BlockIndex = index,
                        Type = "paragraph",
                        TextPreview = Preview(text)
                    })
                    .ToList();

                sections =
                [
                    new DocumentSectionInspection
                    {
                        SectionIndex = 0,
                        Id = string.Empty,
                        Blocks = paragraphBlocks
                    }
                ];
            }

            int modeledTableCount = CountBlocks(sections, "table");
            if (modeledTableCount != liveContent.Tables.Count)
            {
                foreach (DocumentSectionInspection section in sections)
                {
                    section.Blocks.RemoveAll(block => block.Type.Equals("table", StringComparison.OrdinalIgnoreCase));
                }

                DocumentSectionInspection targetSection = sections.First();
                foreach ((DocumentTableSnapshot table, int tableIndex) in liveContent.Tables.Select((value, index) => (value, index)))
                {
                    targetSection.Blocks.Add(new DocumentBlockInspection
                    {
                        BlockIndex = targetSection.Blocks.Count,
                        Type = "table",
                        Id = table.Id,
                        TableId = table.Id,
                        RowCount = table.RowCount,
                        ColumnCount = table.ColumnCount,
                        TextPreview = Preview(string.Join(" | ", table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Text)))
                    });
                }
            }

            return new DocumentStructureResponse
            {
                SessionId = session.SessionId,
                DocumentId = document.Id,
                SectionCount = sections.Count,
                ParagraphCount = CountBlocks(sections, "paragraph"),
                TableCount = liveContent.Tables.Count,
                ImageCount = CountBlocks(sections, "image"),
                FieldCount = CountFields(document),
                HeaderFooterCount = sections.Sum(section => (section.Header is null ? 0 : 1) + (section.Footer is null ? 0 : 1)),
                Sections = sections
            };
        }
    }

    /// <summary>
    /// Get styles known to the automation model for the session document.
    /// </summary>
    public DocumentStylesResponse GetDocumentStyles(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            var styles = new Dictionary<string, StyleInspection>(StringComparer.OrdinalIgnoreCase);

            foreach (StyleInspection style in _engine.GetDocumentStyleSnapshots(session.WorkingDocumentPath))
            {
                styles[style.Name] = style;
            }

            foreach (var pair in state.Styles)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    styles.TryAdd(pair.Key.Trim(), new StyleInspection
                    {
                        Name = pair.Key.Trim(),
                        Type = "paragraph",
                        Text = pair.Value
                    });
                }
            }

            foreach (var style in state.Document.Styles)
            {
                if (string.IsNullOrWhiteSpace(style.Name))
                {
                    continue;
                }

                styles.TryAdd(style.Name.Trim(), new StyleInspection
                {
                    Name = style.Name.Trim(),
                    Type = string.IsNullOrWhiteSpace(style.Type) ? "paragraph" : style.Type,
                    Text = style.Text,
                    Paragraph = style.Paragraph
                });
            }

            return new DocumentStylesResponse
            {
                SessionId = session.SessionId,
                Styles = styles.Values.OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
    }

    /// <summary>
    /// Get table summaries, including rows, cells, text previews, and field names.
    /// </summary>
    public KnowledgeExtractionResponse ExtractKnowledgeBlocks(string sessionId, int startBlock = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startBlock);
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var content = GetCachedContent(session);
            List<KnowledgeExtractionBlock> blocks = [];
            string? heading = null;
            var paragraphs = content.ParagraphDetails.Count > 0 ? content.ParagraphDetails : content.Paragraphs.Select(p => new DocumentParagraphSnapshot(p, null)).ToArray();
            for (int i = 0; i < paragraphs.Count; i++)
            {
                var p = paragraphs[i];
                if (p.StyleName?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true) heading = p.Text;
                blocks.Add(new(p.Text, $"Paragraph {i + 1}", heading, null));
            }
            for (int i = 0; i < content.Tables.Count; i++)
            {
                var table = content.Tables[i];
                string? header = table.Rows.Count > 0 ? string.Join(" | ", table.Rows[0].Cells.Select(c => c.Text)) : null;
                foreach (var row in table.Rows)
                    blocks.Add(new(string.Join(" | ", row.Cells.Select(c => c.Text)), $"Table {i + 1}, row {row.RowIndex + 1}", null, row.RowIndex == 0 ? null : header));
            }
            // Fail explicitly instead of silently truncating unusually large paragraphs or rows.
            var page = new List<KnowledgeExtractionBlock>();
            int characters = 0, index = startBlock;
            for (; index < blocks.Count && page.Count < 32; index++)
            {
                var block = blocks[index];
                int size = block.Text.Length + (block.Heading?.Length ?? 0) + (block.TableHeader?.Length ?? 0);
                if (size > 128000) throw new InvalidOperationException("A source block exceeds the extraction limit.");
                if (characters + size > 128000 && page.Count > 0) break;
                characters += size; page.Add(block);
            }
            return new(page, index < blocks.Count ? index : null);
        }
    }

    public DocumentTablesResponse GetDocumentTables(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            List<TableInspection> modelTables = BuildTableInspections(state.Document).ToList();
            IReadOnlyList<DocumentTableSnapshot> liveTables = GetCachedContent(session).Tables;
            List<TableInspection> tables = liveTables
                .Select((table, tableIndex) =>
                {
                    TableInspection? modelTable = modelTables
                        .Where(candidate => candidate.Id.Equals(table.Id, StringComparison.OrdinalIgnoreCase))
                        .Take(2)
                        .ToList() is { Count: 1 } idMatches
                            ? idMatches[0]
                            : modelTables.Count == liveTables.Count
                                ? modelTables[tableIndex]
                                : null;
                    return new TableInspection
                    {
                        Id = table.Id,
                        TableIndex = tableIndex,
                        TableNumber = tableIndex + 1,
                        SectionIndex = modelTable?.SectionIndex ?? -1,
                        BlockIndex = modelTable?.BlockIndex ?? -1,
                        StyleName = modelTable?.StyleName,
                        RowCount = table.RowCount,
                        ColumnCount = table.ColumnCount,
                        Rows = table.Rows.Select(row => new TableRowInspection
                        {
                            RowIndex = row.RowIndex,
                            Id = modelTable is not null && row.RowIndex < modelTable.Rows.Count
                                ? modelTable.Rows[row.RowIndex].Id
                                : $"{table.Id}:r{row.RowIndex + 1}",
                            Cells = row.Cells.Select(cell =>
                            {
                                TableCellInspection? modelCell = modelTable is not null
                                    && cell.RowIndex < modelTable.Rows.Count
                                    && cell.ColumnIndex < modelTable.Rows[cell.RowIndex].Cells.Count
                                        ? modelTable.Rows[cell.RowIndex].Cells[cell.ColumnIndex]
                                        : null;
                                return new TableCellInspection
                                {
                                    RowIndex = cell.RowIndex,
                                    ColumnIndex = cell.ColumnIndex,
                                    Id = modelCell?.Id ?? $"{table.Id}:r{cell.RowIndex + 1}c{cell.ColumnIndex + 1}",
                                    TextPreview = Preview(cell.Text),
                                    CellStyle = modelCell?.CellStyle,
                                    ColumnSpan = modelCell?.ColumnSpan ?? 1,
                                    RowSpan = modelCell?.RowSpan ?? 1,
                                    FieldNames = modelCell?.FieldNames ?? []
                                };
                            }).ToList()
                        }).ToList()
                    };
                })
                .ToList();
            return new DocumentTablesResponse
            {
                SessionId = session.SessionId,
                TableCount = tables.Count,
                Tables = tables
            };
        }
    }

    /// <summary>
    /// Get merge/application fields represented by the automation model.
    /// </summary>
    public DocumentFieldsResponse GetDocumentFields(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            return new DocumentFieldsResponse
            {
                SessionId = session.SessionId,
                Fields = BuildFieldInspections(state.Document).ToList()
            };
        }
    }

    /// <summary>
    /// Get headers and footers represented by the automation model.
    /// </summary>
    public DocumentHeadersFootersResponse GetDocumentHeadersFooters(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            var headersFooters = new List<HeaderFooterLocationInspection>();

            for (var sectionIndex = 0; sectionIndex < state.Document.Sections.Count; sectionIndex++)
            {
                var section = state.Document.Sections[sectionIndex];
                AddHeaderFooter(headersFooters, sectionIndex, section.Id, section.Header);
                AddHeaderFooter(headersFooters, sectionIndex, section.Id, section.Footer);
            }

            return new DocumentHeadersFootersResponse
            {
                SessionId = session.SessionId,
                HeadersFooters = headersFooters
            };
        }
    }

    /// <summary>
    /// Get actual TX Text Control merge fields from the current session document.
    /// </summary>
    public TemplateMergeFieldsResponse GetTemplateMergeFields(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var fields = GetCachedTemplate(session).MergeFields;
            return new TemplateMergeFieldsResponse
            {
                SessionId = session.SessionId,
                FieldCount = fields.Count,
                Fields = fields
            };
        }
    }

    /// <summary>
    /// Get actual TX Text Control merge-block SubTextParts from the current session document.
    /// </summary>
    public TemplateMergeBlocksResponse GetTemplateMergeBlocks(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var blocks = GetCachedTemplate(session).MergeBlocks;
            return new TemplateMergeBlocksResponse
            {
                SessionId = session.SessionId,
                BlockCount = blocks.Count,
                Blocks = blocks
            };
        }
    }

    /// <summary>
    /// Get actual TX Text Control form fields from the current session document.
    /// </summary>
    public TemplateFormFieldsResponse GetTemplateFormFields(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        lock (session.SyncRoot)
        {
            var fields = GetCachedTemplate(session).FormFields;
            return new TemplateFormFieldsResponse
            {
                SessionId = session.SessionId,
                FieldCount = fields.Count,
                Fields = fields
            };
        }
    }

    /// <summary>
    /// Merge JSON data into the current session template using TX Text Control MailMerge.
    /// </summary>
    public MergeTemplateResponse MergeTemplate(MergeTemplateRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(request));
        }

        var session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            var state = _sessions.LoadState(session);
            var merge = _engine.MergeTemplate(session.WorkingDocumentPath, state, request);
            _sessions.SaveState(session, merge.State);

            return new MergeTemplateResponse
            {
                SessionId = session.SessionId,
                MergedFieldCount = merge.FieldsBefore.Count,
                RemainingFieldCount = merge.FieldsAfter.Count,
                MergedFormFieldCount = merge.FormFieldsBefore.Count,
                RemainingFormFieldCount = merge.FormFieldsAfter.Count,
                MergedFieldNames = merge.FieldsBefore.Select(field => field.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RemainingFields = merge.FieldsAfter,
                MergedFormFieldNames = merge.FormFieldsBefore.Select(field => field.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                RemainingFormFields = merge.FormFieldsAfter
            };
        }
    }

    private static Dictionary<string, TxTextControl.McpServer.Models.DocumentModel.TextStyleDefinition> ExtractStyles(
        TxTextControl.McpServer.Models.DocumentModel.Document document)
        => document.Styles
            .Where(style => !string.IsNullOrWhiteSpace(style.Name) && style.Text is not null)
            .ToDictionary(
                style => style.Name.Trim(),
                style => style.Text!,
                StringComparer.OrdinalIgnoreCase);

    private DocumentContentSnapshot GetCachedContent(DocumentSession session)
    {
        if (session.CachedContent is null)
        {
            session.CachedContent = _engine.GetContentSnapshot(session.WorkingDocumentPath);
        }

        return session.CachedContent;
    }

    /// <summary>Classifies a document and returns category-specific, client-renderable actions.</summary>
    public DocumentCategoryResponse ClassifyDocument(ClassifyDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(request));
        }

        var session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            string content = GetCachedContent(session).Text;
            string normalized = Regex.Replace(content.ToLowerInvariant(), @"\s+", " ");
            (string Category, int Score, List<string> Signals) best = ("General", 0, []);
            foreach ((string category, (string Term, int Weight)[] terms) in CategorySignals)
            {
                var signals = new List<string>();
                int score = 0;
                foreach ((string term, int weight) in terms)
                {
                    int occurrences = Regex.Matches(
                        normalized,
                        $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
                        RegexOptions.CultureInvariant).Count;
                    if (occurrences == 0)
                    {
                        continue;
                    }

                    score += weight * Math.Min(occurrences, 3);
                    signals.Add(term);
                }

                if (score > best.Score)
                {
                    best = (category, score, signals);
                }
            }

            string detectedCategory = best.Score >= 4 ? best.Category : "General";
            double confidence = detectedCategory == "General"
                ? 0.4
                : Math.Round(Math.Clamp(0.52 + best.Score / 40d, 0.55, 0.97), 2);
            return new DocumentCategoryResponse
            {
                SessionId = session.SessionId,
                Category = detectedCategory,
                Confidence = confidence,
                Signals = best.Signals.Take(6).ToList(),
                SuggestedActions = CategoryActions[detectedCategory]
                    .Select(action => new DocumentSuggestedAction
                    {
                        Id = action.Id,
                        Title = action.Title,
                        Description = action.Description,
                        Prompt = action.Prompt
                    })
                    .ToList()
            };
        }
    }

    /// <summary>
    /// Returns indexed document paragraphs, optionally focused around query matches.
    /// </summary>
    public DocumentInspectionResponse InspectDocument(InspectDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(request));
        }

        if (request.StartParagraphIndex is < 0)
        {
            throw new ArgumentException("startParagraphIndex must be >= 0.", nameof(request));
        }

        if (request.ParagraphCount is <= 0)
        {
            throw new ArgumentException("paragraphCount must be greater than 0 when provided.", nameof(request));
        }

        int maxCharacters = Math.Clamp(request.MaxCharacters, 1000, 50000);
        int contextParagraphs = Math.Clamp(request.ContextParagraphs, 0, 10);
        var session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            IReadOnlyList<string> paragraphs = GetCachedContent(session).Paragraphs;
            int start = Math.Clamp(request.StartParagraphIndex ?? 0, 0, paragraphs.Count);
            List<int> matches = FindRelevantParagraphs(paragraphs, request.Query, start);
            IEnumerable<int> candidates;
            if (!string.IsNullOrWhiteSpace(request.Query) && matches.Count > 0)
            {
                var focused = new SortedSet<int>();
                foreach (int match in matches)
                {
                    int from = Math.Max(start, match - contextParagraphs);
                    int to = Math.Min(paragraphs.Count - 1, match + contextParagraphs);
                    for (int index = from; index <= to; index++)
                    {
                        focused.Add(index);
                    }
                }

                candidates = focused;
            }
            else
            {
                candidates = Enumerable.Range(start, paragraphs.Count - start);
            }

            if (request.ParagraphCount.HasValue)
            {
                candidates = candidates.Take(request.ParagraphCount.Value);
            }

            List<int> candidateIndexes = candidates.ToList();
            var selected = new List<IndexedParagraphResponse>();
            int characterCount = 0;
            foreach (int index in candidateIndexes)
            {
                string text = paragraphs[index] ?? string.Empty;
                if (selected.Count > 0 && characterCount + text.Length > maxCharacters)
                {
                    break;
                }

                selected.Add(new IndexedParagraphResponse
                {
                    Index = index,
                    Text = text,
                    StyleName = GetParagraphDetails(session).ElementAtOrDefault(index)?.StyleName
                });
                characterCount += text.Length;
            }

            bool truncated = selected.Count < candidateIndexes.Count;
            return new DocumentInspectionResponse
            {
                SessionId = session.SessionId,
                TotalParagraphs = paragraphs.Count,
                Query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim(),
                MatchCount = matches.Count,
                MatchParagraphIndexes = matches,
                Paragraphs = selected,
                ReturnedCharacters = characterCount,
                Truncated = truncated,
                NextParagraphIndex = truncated ? candidateIndexes[selected.Count] : null
            };
        }
    }

    /// <summary>Resolves one section from its visible heading and returns its complete body.</summary>
    public DocumentSectionResponse InspectDocumentSection(InspectDocumentSectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSectionRequest(request.SessionId, request.Heading, request.OccurrenceIndex);

        DocumentSession session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            return ResolveDocumentSection(session, request.Heading, request.OccurrenceIndex).ToResponse(session.SessionId);
        }
    }

    /// <summary>Atomically replaces one heading-resolved section body when its inspected content is unchanged.</summary>
    public DocumentSectionEditResponse ReplaceDocumentSection(ReplaceDocumentSectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSectionRequest(request.SessionId, request.Heading, request.OccurrenceIndex);
        if (string.IsNullOrWhiteSpace(request.ExpectedContentHash))
        {
            throw new ArgumentException("'expectedContentHash' is required. Inspect the section immediately before editing.", nameof(request));
        }

        DocumentSession session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            ResolvedDocumentSection section = ResolveDocumentSection(session, request.Heading, request.OccurrenceIndex);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(section.ContentHash),
                    Encoding.ASCII.GetBytes(request.ExpectedContentHash.Trim().ToUpperInvariant())))
            {
                throw new InvalidOperationException(
                    "The section changed after it was inspected. Inspect it again and use the new contentHash.");
            }

            if (section.BodyStartParagraphIndex > section.BodyEndParagraphIndex)
            {
                throw new InvalidOperationException(
                    "The resolved section has no body paragraph to replace. Use an insertion operation after the heading.");
            }

            DocumentState existingState = _sessions.LoadState(session);
            DocumentEditEngineResult edit = _engine.EditDocument(
                session.WorkingDocumentPath,
                new EditDocumentRequest
                {
                    SessionId = session.SessionId,
                    StartParagraphIndex = section.BodyStartParagraphIndex,
                    EndParagraphIndex = section.BodyEndParagraphIndex,
                    ReplacementText = request.ReplacementText ?? string.Empty
                });
            edit.State.Document = existingState.Document;
            edit.State.Styles = existingState.Styles;
            edit.State.SourceContentHash = null;
            _sessions.SaveState(session, edit.State);

            ResolvedDocumentSection updated = ResolveDocumentSection(session, request.Heading, request.OccurrenceIndex);
            return new DocumentSectionEditResponse
            {
                SessionId = session.SessionId,
                Heading = updated.Heading,
                PreviousContentHash = section.ContentHash,
                ContentHash = updated.ContentHash,
                HeadingParagraphIndex = updated.HeadingParagraphIndex,
                BodyStartParagraphIndex = updated.BodyStartParagraphIndex,
                BodyEndParagraphIndex = updated.BodyEndParagraphIndex,
                EditsApplied = edit.EditsApplied
            };
        }
    }

    /// <summary>
    /// Applies one deterministic text replacement to an existing document.
    /// </summary>
    public DocumentEditResponse EditDocument(EditDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("'sessionId' is required.", nameof(request));
        }

        var session = _sessions.Get(request.SessionId);
        lock (session.SyncRoot)
        {
            DocumentState existingState = _sessions.LoadState(session);
            DocumentEditEngineResult edit = _engine.EditDocument(session.WorkingDocumentPath, request);
            edit.State.Document = existingState.Document;
            edit.State.Styles = existingState.Styles;
            edit.State.SourceContentHash = null;
            _sessions.SaveState(session, edit.State);
            return new DocumentEditResponse
            {
                SessionId = session.SessionId,
                TargetKind = edit.TargetKind,
                Changed = edit.EditsApplied > 0,
                EditsApplied = edit.EditsApplied,
                Revision = edit.State.Revision,
                ReplacedRanges = edit.ReplacedRanges,
                ParagraphIndexes = edit.ParagraphIndexes
            };
        }
    }

    private ResolvedDocumentSection ResolveDocumentSection(
        DocumentSession session,
        string requestedHeading,
        int? occurrenceIndex)
    {
        IReadOnlyList<DocumentParagraphSnapshot> paragraphs = GetParagraphDetails(session);
        string normalizedHeading = NormalizeHeading(requestedHeading);
        List<int> matches = paragraphs
            .Select((paragraph, index) => new { Paragraph = paragraph, Index = index })
            .Where(candidate => HeadingMatches(candidate.Paragraph.Text, normalizedHeading)
                                || IsPartialHeadingMatch(candidate.Paragraph, normalizedHeading))
            .Select(candidate => candidate.Index)
            .ToList();
        if (matches.Count == 0)
        {
            throw new ArgumentException(
                $"No section heading matched '{requestedHeading}'. Inspect the document and retry with the visible heading text.");
        }

        int selectedOccurrence;
        if (occurrenceIndex.HasValue)
        {
            selectedOccurrence = occurrenceIndex.Value;
            if (selectedOccurrence < 0 || selectedOccurrence >= matches.Count)
            {
                throw new ArgumentException(
                    $"occurrenceIndex is out of range. Found {matches.Count} matching heading(s), indexed from 0.");
            }
        }
        else
        {
            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"The heading '{requestedHeading}' is ambiguous at paragraph indexes {string.Join(", ", matches)}. Retry with occurrenceIndex.");
            }

            selectedOccurrence = 0;
        }

        int headingIndex = matches[selectedOccurrence];
        DocumentParagraphSnapshot heading = paragraphs[headingIndex];
        int? headingLevel = GetHeadingLevel(heading.StyleName);
        int nextHeadingIndex = paragraphs.Count;
        for (int index = headingIndex + 1; index < paragraphs.Count; index++)
        {
            DocumentParagraphSnapshot candidate = paragraphs[index];
            int? candidateLevel = GetHeadingLevel(candidate.StyleName);
            bool isBoundary = headingLevel.HasValue
                ? candidateLevel.HasValue && candidateLevel.Value <= headingLevel.Value
                : candidateLevel.HasValue || IsLikelyHeading(candidate.Text);
            if (isBoundary)
            {
                nextHeadingIndex = index;
                break;
            }
        }

        int bodyStart = headingIndex + 1;
        int bodyEnd = nextHeadingIndex - 1;
        IReadOnlyList<IndexedParagraphResponse> body = bodyStart <= bodyEnd
            ? Enumerable.Range(bodyStart, bodyEnd - bodyStart + 1)
                .Select(index => new IndexedParagraphResponse
                {
                    Index = index,
                    Text = paragraphs[index].Text,
                    StyleName = paragraphs[index].StyleName
                })
                .ToArray()
            : [];
        string contentHash = ComputeSectionHash(headingIndex, heading, body);
        return new ResolvedDocumentSection(
            heading.Text,
            heading.StyleName,
            headingIndex,
            bodyStart,
            bodyEnd,
            contentHash,
            body);
    }

    private IReadOnlyList<DocumentParagraphSnapshot> GetParagraphDetails(DocumentSession session)
    {
        DocumentContentSnapshot content = GetCachedContent(session);
        if (content.ParagraphDetails.Count == content.Paragraphs.Count)
        {
            return content.ParagraphDetails;
        }

        return content.Paragraphs
            .Select(text => new DocumentParagraphSnapshot(text ?? string.Empty, null))
            .ToArray();
    }

    private static void ValidateSectionRequest(string sessionId, string heading, int? occurrenceIndex)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("'sessionId' is required.");
        }

        if (string.IsNullOrWhiteSpace(heading))
        {
            throw new ArgumentException("'heading' is required.");
        }

        if (occurrenceIndex is < 0)
        {
            throw new ArgumentException("occurrenceIndex must be >= 0 when provided.");
        }
    }

    private static bool HeadingMatches(string candidate, string normalizedRequestedHeading)
    {
        string normalizedCandidate = NormalizeHeading(candidate);
        return normalizedCandidate.Equals(normalizedRequestedHeading, StringComparison.Ordinal)
               || normalizedCandidate.EndsWith($" {normalizedRequestedHeading}", StringComparison.Ordinal);
    }

    private static bool IsPartialHeadingMatch(
        DocumentParagraphSnapshot candidate,
        string normalizedRequestedHeading)
    {
        if (!GetHeadingLevel(candidate.StyleName).HasValue && !IsLikelyHeading(candidate.Text))
        {
            return false;
        }

        string normalizedCandidate = $" {NormalizeHeading(candidate.Text)} ";
        return normalizedCandidate.Contains($" {normalizedRequestedHeading} ", StringComparison.Ordinal);
    }

    private static string NormalizeHeading(string value)
        => Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();

    private static int? GetHeadingLevel(string? styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName))
        {
            return null;
        }

        string normalized = styleName.Trim();
        if (normalized.Equals("title", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        Match match = Regex.Match(normalized, @"heading\s*(?<level>\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["level"].Value, out int level))
        {
            return level;
        }

        return normalized.Contains("heading", StringComparison.OrdinalIgnoreCase) ? 1 : null;
    }

    private static bool IsLikelyHeading(string value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Length is 0 or > 120 || text.EndsWith('.') || text.EndsWith('?') || text.EndsWith('!'))
        {
            return false;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || words.Length > 14)
        {
            return false;
        }

        bool numbered = Regex.IsMatch(text, @"^(section|article)?\s*\d+(?:\.\d+)*[.)]?\s+", RegexOptions.IgnoreCase);
        bool upperCase = text.Any(char.IsLetter)
                         && text.Where(char.IsLetter).All(char.IsUpper);
        return numbered || upperCase;
    }

    private static string ComputeSectionHash(
        int headingIndex,
        DocumentParagraphSnapshot heading,
        IReadOnlyList<IndexedParagraphResponse> body)
    {
        var content = new StringBuilder()
            .Append(headingIndex).Append('\u001f')
            .Append(heading.StyleName).Append('\u001f')
            .Append(heading.Text).Append('\u001e');
        foreach (IndexedParagraphResponse paragraph in body)
        {
            content.Append(paragraph.Index).Append('\u001f')
                .Append(paragraph.StyleName).Append('\u001f')
                .Append(paragraph.Text).Append('\u001e');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }

    private sealed record ResolvedDocumentSection(
        string Heading,
        string? HeadingStyleName,
        int HeadingParagraphIndex,
        int BodyStartParagraphIndex,
        int BodyEndParagraphIndex,
        string ContentHash,
        IReadOnlyList<IndexedParagraphResponse> Paragraphs)
    {
        public DocumentSectionResponse ToResponse(string sessionId) => new()
        {
            SessionId = sessionId,
            Heading = Heading,
            HeadingStyleName = HeadingStyleName,
            HeadingParagraphIndex = HeadingParagraphIndex,
            BodyStartParagraphIndex = BodyStartParagraphIndex,
            BodyEndParagraphIndex = BodyEndParagraphIndex,
            ContentHash = ContentHash,
            Paragraphs = Paragraphs
        };
    }

    private TemplateContentSnapshot GetCachedTemplate(DocumentSession session)
    {
        if (session.CachedTemplate is null)
        {
            session.CachedTemplate = _engine.GetTemplateContentSnapshot(session.WorkingDocumentPath);
        }

        return session.CachedTemplate;
    }

    private static bool ParagraphContains(
        string paragraph,
        string query,
        bool matchCase,
        bool wholeWord)
    {
        StringComparison comparison = matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int searchStart = 0;
        while (searchStart <= paragraph.Length - query.Length)
        {
            int found = paragraph.IndexOf(query, searchStart, comparison);
            if (found < 0)
            {
                return false;
            }

            if (!wholeWord || IsWholeWordMatch(paragraph, found, query.Length))
            {
                return true;
            }

            searchStart = found + Math.Max(1, query.Length);
        }

        return false;
    }

    private static List<SearchTextRange> FindTextRanges(
        string content,
        string query,
        bool matchCase,
        bool wholeWord)
    {
        List<SearchTextRange> matches = [];
        if (query.Length == 0 || content.Length < query.Length)
        {
            return matches;
        }

        StringComparison comparison = matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        int searchStart = 0;
        while (searchStart <= content.Length - query.Length)
        {
            int found = content.IndexOf(query, searchStart, comparison);
            if (found < 0)
            {
                break;
            }

            if (!wholeWord || IsWholeWordMatch(content, found, query.Length))
            {
                matches.Add(new SearchTextRange
                {
                    Start = found,
                    Length = query.Length
                });
            }

            searchStart = found + Math.Max(1, query.Length);
        }

        return matches;
    }

    private static bool IsWholeWordMatch(string content, int start, int length)
    {
        bool startsAtBoundary = start == 0 || !IsWordCharacter(content[start - 1]);
        int end = start + length;
        bool endsAtBoundary = end == content.Length || !IsWordCharacter(content[end]);
        return startsAtBoundary && endsAtBoundary;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static List<DocumentSectionInspection> BuildSectionInspections(Document document)
        => document.Sections
            .Select((section, sectionIndex) => new DocumentSectionInspection
            {
                SectionIndex = sectionIndex,
                Id = section.Id,
                StyleName = section.StyleName,
                PageLayout = section.PageLayout,
                Header = BuildHeaderFooterInspection(section.Header),
                Footer = BuildHeaderFooterInspection(section.Footer),
                Blocks = section.Blocks
                    .Select((block, blockIndex) => BuildBlockInspection(block, blockIndex))
                    .ToList()
            })
            .ToList();

    private static HeaderFooterInspection? BuildHeaderFooterInspection(HeaderFooter? headerFooter)
    {
        if (headerFooter is null)
        {
            return null;
        }

        return new HeaderFooterInspection
        {
            Type = headerFooter.Type,
            TextPreview = Preview(BlocksToText(headerFooter.Blocks)),
            Blocks = headerFooter.Blocks
                .Select((block, blockIndex) => BuildBlockInspection(block, blockIndex))
                .ToList()
        };
    }

    private static DocumentBlockInspection BuildBlockInspection(DocumentBlock block, int blockIndex)
    {
        var type = string.IsNullOrWhiteSpace(block.Type) ? ResolveBlockType(block) : block.Type;
        return new DocumentBlockInspection
        {
            BlockIndex = blockIndex,
            Type = type,
            Id = block.Paragraph?.Id ?? block.Table?.Id ?? block.Image?.Id ?? block.Field?.Id,
            StyleName = block.Paragraph?.StyleName ?? block.Table?.StyleName,
            TextPreview = Preview(BlockToText(block)),
            TableId = block.Table?.Id,
            RowCount = block.Table?.Rows.Count,
            ColumnCount = block.Table?.Rows.Count > 0 ? block.Table.Rows.Max(row => row.Cells.Count) : null,
            FieldName = block.Field?.Name,
            ImageAltText = block.Image?.AltText
        };
    }

    private static IEnumerable<TableInspection> BuildTableInspections(Document document)
    {
        for (var sectionIndex = 0; sectionIndex < document.Sections.Count; sectionIndex++)
        {
            var section = document.Sections[sectionIndex];
            for (var blockIndex = 0; blockIndex < section.Blocks.Count; blockIndex++)
            {
                var table = section.Blocks[blockIndex].Table;
                if (table is null)
                {
                    continue;
                }

                yield return new TableInspection
                {
                    Id = table.Id,
                    TableIndex = 0,
                    TableNumber = 1,
                    SectionIndex = sectionIndex,
                    BlockIndex = blockIndex,
                    StyleName = table.StyleName,
                    RowCount = table.Rows.Count,
                    ColumnCount = table.Rows.Count == 0 ? 0 : table.Rows.Max(row => row.Cells.Count),
                    Rows = table.Rows.Select((row, rowIndex) => new TableRowInspection
                    {
                        RowIndex = rowIndex,
                        Id = row.Id,
                        Cells = row.Cells.Select((cell, columnIndex) => new TableCellInspection
                        {
                            RowIndex = rowIndex,
                            ColumnIndex = columnIndex,
                            Id = cell.Id,
                            TextPreview = Preview(BlocksToText(cell.Blocks)),
                            CellStyle = cell.CellStyle,
                            ColumnSpan = cell.ColumnSpan,
                            RowSpan = cell.RowSpan,
                            FieldNames = cell.Blocks
                                .SelectMany(GetFields)
                                .Select(field => field.Name)
                                .Where(name => !string.IsNullOrWhiteSpace(name))
                                .ToList()
                        }).ToList()
                    }).ToList()
                };
            }
        }
    }

    private static IEnumerable<FieldInspection> BuildFieldInspections(Document document)
    {
        foreach (var section in document.Sections.Select((value, index) => new { value, index }))
        {
            foreach (var field in BuildFieldInspections(section.value.Blocks, $"sections[{section.index}].blocks"))
            {
                yield return field;
            }

            if (section.value.Header is not null)
            {
                foreach (var field in BuildFieldInspections(section.value.Header.Blocks, $"sections[{section.index}].header.blocks"))
                {
                    yield return field;
                }
            }

            if (section.value.Footer is not null)
            {
                foreach (var field in BuildFieldInspections(section.value.Footer.Blocks, $"sections[{section.index}].footer.blocks"))
                {
                    yield return field;
                }
            }
        }
    }

    private static IEnumerable<FieldInspection> BuildFieldInspections(IReadOnlyList<DocumentBlock> blocks, string location)
    {
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            if (block.Field is not null)
            {
                yield return ToFieldInspection(block.Field, $"{location}[{blockIndex}]");
            }

            if (block.Table is null)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < block.Table.Rows.Count; rowIndex++)
            {
                var row = block.Table.Rows[rowIndex];
                for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
                {
                    var cellLocation = $"{location}[{blockIndex}].table.rows[{rowIndex}].cells[{columnIndex}].blocks";
                    foreach (var field in BuildFieldInspections(row.Cells[columnIndex].Blocks, cellLocation))
                    {
                        yield return field;
                    }
                }
            }
        }
    }

    private static FieldInspection ToFieldInspection(Field field, string location)
        => new()
        {
            Id = field.Id,
            Type = field.Type,
            Name = field.Name,
            Value = field.Value,
            Location = location,
            Properties = new Dictionary<string, string>(field.Properties)
        };

    private static void AddHeaderFooter(
        List<HeaderFooterLocationInspection> headersFooters,
        int sectionIndex,
        string sectionId,
        HeaderFooter? headerFooter)
    {
        if (headerFooter is null)
        {
            return;
        }

        headersFooters.Add(new HeaderFooterLocationInspection
        {
            SectionIndex = sectionIndex,
            SectionId = sectionId,
            Type = headerFooter.Type,
            TextPreview = Preview(BlocksToText(headerFooter.Blocks)),
            Blocks = headerFooter.Blocks
                .Select((block, blockIndex) => BuildBlockInspection(block, blockIndex))
                .ToList()
        });
    }

    private static int CountBlocks(IEnumerable<DocumentSectionInspection> sections, string type)
        => sections.Sum(section => section.Blocks.Count(block => string.Equals(block.Type, type, StringComparison.OrdinalIgnoreCase)));

    private static int CountFields(Document document)
        => BuildFieldInspections(document).Count();

    private static IEnumerable<Field> GetFields(DocumentBlock block)
    {
        if (block.Field is not null)
        {
            yield return block.Field;
        }

        if (block.Table is null)
        {
            yield break;
        }

        foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells))
        {
            foreach (var field in cell.Blocks.SelectMany(GetFields))
            {
                yield return field;
            }
        }
    }

    private static string ResolveBlockType(DocumentBlock block)
    {
        if (block.Paragraph is not null)
        {
            return "paragraph";
        }

        if (block.Table is not null)
        {
            return "table";
        }

        if (block.Image is not null)
        {
            return "image";
        }

        if (block.Field is not null)
        {
            return "field";
        }

        return "unknown";
    }

    private static string BlocksToText(IReadOnlyList<DocumentBlock> blocks)
        => string.Join(" ", blocks.Select(BlockToText).Where(text => !string.IsNullOrWhiteSpace(text)));

    private static string BlockToText(DocumentBlock block)
    {
        if (block.Paragraph is not null)
        {
            return string.Concat(block.Paragraph.Runs.Select(run => run.Text ?? string.Empty));
        }

        if (block.Field is not null)
        {
            return block.Field.Value ?? block.Field.Name;
        }

        if (block.Table is not null)
        {
            return string.Join(
                " ",
                block.Table.Rows
                    .SelectMany(row => row.Cells)
                    .Select(cell => BlocksToText(cell.Blocks))
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        return block.Image?.AltText ?? string.Empty;
    }

    private static List<int> FindRelevantParagraphs(
        IReadOnlyList<string> paragraphs,
        string? query,
        int startIndex)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string normalizedQuery = query.Trim();
        string[] ignoredWords =
        [
            "what", "which", "where", "when", "who", "why", "how", "does", "this", "that",
            "with", "from", "given", "document", "contract", "please", "tell", "find", "under"
        ];
        string[] terms = normalizedQuery
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim('?', '!', '.', ',', ':', ';', '\'', '"', '(', ')'))
            .Where(term => term.Length >= 3 && !ignoredWords.Contains(term, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scored = new List<(int Index, int Score)>();
        for (int index = startIndex; index < paragraphs.Count; index++)
        {
            string text = paragraphs[index] ?? string.Empty;
            int score = text.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
            score += terms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (score > 0)
            {
                scored.Add((index, score));
            }
        }

        if (scored.Count == 0)
        {
            return [];
        }

        int bestScore = scored.Max(item => item.Score);
        return scored
            .Where(item => item.Score == bestScore || item.Score >= Math.Max(1, bestScore - 1))
            .Select(item => item.Index)
            .ToList();
    }

    private static string? Preview(string? text, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
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
