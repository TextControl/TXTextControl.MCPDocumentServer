using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.DocumentModel;
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

        DocumentSession session;
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!request.CreateIfMissing)
            {
                throw new ArgumentException("'sessionId' is required when createIfMissing is false.", nameof(request));
            }

            session = _sessions.Create();
            var createdState = _engine.CreateEmpty(session.WorkingDocumentPath);
            _sessions.SaveState(session, createdState);
        }
        else
        {
            session = _sessions.Get(request.SessionId);
        }

        var state = _sessions.LoadState(session);
        var updatedState = _engine.ApplyOperations(session.WorkingDocumentPath, state, request);
        _sessions.SaveState(session, updatedState);

        return new ApplyOperationsResponse
        {
            SessionId = session.SessionId,
            Results = updatedState.LastOperationResults.ToList()
        };
    }

    /// <summary>
    /// Render a neutral AI-facing document model into a real TX document.
    /// </summary>
    public ApplyOperationsResponse RenderDocumentModel(RenderDocumentModelRequest request)
    {
        var operations = DocumentModelOperationCompiler.Compile(request);
        var response = ApplyOperations(operations);
        var session = _sessions.Get(response.SessionId);
        var state = _sessions.LoadState(session);

        state.Document = request.Document!;
        state.Styles = ExtractStyles(request.Document!);
        _sessions.SaveState(session, state);

        return response;
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
    /// Get the neutral AI-facing document model for a session.
    /// </summary>
    public DocumentModelResponse GetDocumentModel(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var state = _sessions.LoadState(session);

        return new DocumentModelResponse
        {
            SessionId = session.SessionId,
            Document = state.Document
        };
    }

    /// <summary>
    /// Get an AI-friendly structural summary for the session document.
    /// </summary>
    public DocumentStructureResponse GetDocumentStructure(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var state = _sessions.LoadState(session);
        var document = state.Document ?? new Document();
        var sections = BuildSectionInspections(document);

        if (sections.Count == 0 || sections.All(section => section.Blocks.Count == 0))
        {
            var paragraphBlocks = _engine.GetParagraphs(session.WorkingDocumentPath)
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

        return new DocumentStructureResponse
        {
            SessionId = session.SessionId,
            DocumentId = document.Id,
            SectionCount = sections.Count,
            ParagraphCount = CountBlocks(sections, "paragraph"),
            TableCount = CountBlocks(sections, "table"),
            ImageCount = CountBlocks(sections, "image"),
            FieldCount = CountFields(document),
            HeaderFooterCount = sections.Sum(section => (section.Header is null ? 0 : 1) + (section.Footer is null ? 0 : 1)),
            Sections = sections
        };
    }

    /// <summary>
    /// Get styles known to the automation model for the session document.
    /// </summary>
    public DocumentStylesResponse GetDocumentStyles(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var state = _sessions.LoadState(session);
        var styles = new Dictionary<string, StyleInspection>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in state.Styles)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                styles[pair.Key.Trim()] = new StyleInspection
                {
                    Name = pair.Key.Trim(),
                    Type = "paragraph",
                    Text = pair.Value
                };
            }
        }

        foreach (var style in state.Document.Styles)
        {
            if (string.IsNullOrWhiteSpace(style.Name))
            {
                continue;
            }

            styles[style.Name.Trim()] = new StyleInspection
            {
                Name = style.Name.Trim(),
                Type = string.IsNullOrWhiteSpace(style.Type) ? "paragraph" : style.Type,
                Text = style.Text,
                Paragraph = style.Paragraph
            };
        }

        return new DocumentStylesResponse
        {
            SessionId = session.SessionId,
            Styles = styles.Values.OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    /// <summary>
    /// Get table summaries, including rows, cells, text previews, and field names.
    /// </summary>
    public DocumentTablesResponse GetDocumentTables(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var state = _sessions.LoadState(session);

        return new DocumentTablesResponse
        {
            SessionId = session.SessionId,
            Tables = BuildTableInspections(state.Document).ToList()
        };
    }

    /// <summary>
    /// Get merge/application fields represented by the automation model.
    /// </summary>
    public DocumentFieldsResponse GetDocumentFields(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var state = _sessions.LoadState(session);

        return new DocumentFieldsResponse
        {
            SessionId = session.SessionId,
            Fields = BuildFieldInspections(state.Document).ToList()
        };
    }

    /// <summary>
    /// Get headers and footers represented by the automation model.
    /// </summary>
    public DocumentHeadersFootersResponse GetDocumentHeadersFooters(string sessionId)
    {
        var session = _sessions.Get(sessionId);
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

    /// <summary>
    /// Get actual TX Text Control merge fields from the current session document.
    /// </summary>
    public TemplateMergeFieldsResponse GetTemplateMergeFields(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var fields = _engine.GetTemplateMergeFields(session.WorkingDocumentPath);

        return new TemplateMergeFieldsResponse
        {
            SessionId = session.SessionId,
            FieldCount = fields.Count,
            Fields = fields
        };
    }

    /// <summary>
    /// Get actual TX Text Control merge-block SubTextParts from the current session document.
    /// </summary>
    public TemplateMergeBlocksResponse GetTemplateMergeBlocks(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var blocks = _engine.GetTemplateMergeBlocks(session.WorkingDocumentPath);

        return new TemplateMergeBlocksResponse
        {
            SessionId = session.SessionId,
            BlockCount = blocks.Count,
            Blocks = blocks
        };
    }

    /// <summary>
    /// Get actual TX Text Control form fields from the current session document.
    /// </summary>
    public TemplateFormFieldsResponse GetTemplateFormFields(string sessionId)
    {
        var session = _sessions.Get(sessionId);
        var fields = _engine.GetTemplateFormFields(session.WorkingDocumentPath);

        return new TemplateFormFieldsResponse
        {
            SessionId = session.SessionId,
            FieldCount = fields.Count,
            Fields = fields
        };
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
        var state = _sessions.LoadState(session);
        var templateFields = _engine.GetTemplateMergeFields(session.WorkingDocumentPath);
        var templateFormFields = _engine.GetTemplateFormFields(session.WorkingDocumentPath);
        var updatedState = _engine.MergeTemplate(session.WorkingDocumentPath, state, request);
        _sessions.SaveState(session, updatedState);
        var remainingFields = _engine.GetTemplateMergeFields(session.WorkingDocumentPath);
        var remainingFormFields = _engine.GetTemplateFormFields(session.WorkingDocumentPath);

        return new MergeTemplateResponse
        {
            SessionId = session.SessionId,
            MergedFieldCount = templateFields.Count,
            RemainingFieldCount = remainingFields.Count,
            MergedFormFieldCount = templateFormFields.Count,
            RemainingFormFieldCount = remainingFormFields.Count,
            MergedFieldNames = templateFields.Select(field => field.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RemainingFields = remainingFields,
            MergedFormFieldNames = templateFormFields.Select(field => field.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RemainingFormFields = remainingFormFields
        };
    }

    private static Dictionary<string, TxTextControl.McpServer.Models.DocumentModel.TextStyleDefinition> ExtractStyles(
        TxTextControl.McpServer.Models.DocumentModel.Document document)
        => document.Styles
            .Where(style => !string.IsNullOrWhiteSpace(style.Name) && style.Text is not null)
            .ToDictionary(
                style => style.Name.Trim(),
                style => style.Text!,
                StringComparer.OrdinalIgnoreCase);

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
