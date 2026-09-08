using System;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Tools;

[McpServerToolType]
public sealed class OperationTools
{
    private readonly DocumentWorkflowService _workflow;
    private readonly DocumentOperationRegistry _registry;
    private readonly DocumentAutomationOptions _options;
    private readonly AutomationSettingsService _settings;
    private readonly AuthoringGuideService _authoringGuide;

    public OperationTools(
        DocumentWorkflowService workflow,
        DocumentOperationRegistry registry,
        IOptions<DocumentAutomationOptions> options,
        AutomationSettingsService settings,
        AuthoringGuideService authoringGuide)
    {
        _workflow = workflow;
        _registry = registry;
        _options = options.Value;
        _settings = settings;
        _authoringGuide = authoringGuide;
    }

    [McpServerTool, Description("Advanced mutation tool for structural or formatting changes to an existing session after inspect_document. Prefer edit_document for ordinary text replacement and insert_table for adding a simple table. Every operations item requires its type discriminator. Reuse sessionId. Do not use createIfMissing to assemble a complete draft; use create_document or a recipe. Omit styling properties not explicitly requested so server presets remain in effect.")]
    public object ApplyOperations(ApplyOperationsRequest request)
    {
        try
        {
            return _workflow.ApplyOperations(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool(
        Name = "create_document_from_markdown",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(CreateDocumentFromMarkdownResponse)),
     Description("Primary creation tool for ordinary documents whose complete fixed content can be expressed as Markdown: reports, letters, proposals, agendas, articles, and invoices with concrete line items. Required: request.markdown containing the complete document, with one H1 title, H2/H3 hierarchy, lists, emphasis, and valid Markdown tables as appropriate. Pass raw Markdown text, not Base64 and not an outer code fence. The server creates a new session and atomically imports the Markdown, maps its hierarchy to configured Title/Heading1/Heading2/Body styles, applies the default page layout and table preset, and returns sessionId. If an output format was requested, call create_document_export next with that sessionId. Do not use this for reusable templates, merge fields, repeating blocks, form fields, headers/footers, images, or explicit fonts/colors/sizes; use a matching recipe or create_document for those advanced semantics.")]
    public object CreateDocumentFromMarkdown(CreateDocumentFromMarkdownRequest request)
    {
        try
        {
            return _workflow.CreateDocumentFromMarkdown(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool(
        Name = "apply_document_preset_styles",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyDocumentPresetStylesResponse)),
     Description("Applies the MCP server's configured default page layout, paragraph style presets, and table preset to an already loaded document. Use this after load_document when the user asks to apply, normalize, polish, or restyle the document with preset/default styles. It preserves all document text and the existing sessionId, maps imported Markdown H1/H2/H3 hierarchy to Title/Heading1/Heading2 and other paragraphs to Body, styles native tables, and requires only request.sessionId. Do not recreate the document and do not ask the model to emit per-paragraph or per-cell formatting operations.")]
    public object ApplyDocumentPresetStyles(ApplyDocumentPresetStylesRequest request)
    {
        try
        {
            return _workflow.ApplyDocumentPresetStyles(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool(Name = "create_document", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)), Description("Advanced complete-document creation tool using the semantic Document model. Prefer create_document_from_markdown for ordinary fixed-content documents. Use this tool when the user requests explicit fonts, colors, sizes, advanced layout, headers/footers, images, fields, or structures Markdown cannot represent. Omit every style and page property the user did not specify: the server applies its configured defaults to those elements. Use document.title, semantic paragraph roles, and real table rows/cells. Never assemble a full draft through create_empty_document plus apply_operations.")]
    public object RenderDocumentModel(RenderDocumentModelRequest request)
    {
        try
        {
            return _workflow.RenderDocumentModel(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns a compact overview of enabled automation capabilities, operation names, configured style names, and output formats. Use get_authoring_guide only when complete schemas, examples, and troubleshooting guidance are required.")]
    public object GetDocumentAutomationCapabilities()
    {
        try
        {
            return new
            {
                capabilityPacks = _registry.GetCapabilityPacks(),
                enabledCapabilityPacks = _settings.GetEnabledCapabilityPacks(),
                enabledOperations = _settings.GetEnabledOperations(),
                operations = _registry.GetCapabilities(),
                defaultParagraphStyleName = _options.DefaultParagraphStyleName,
                styleRoles = _options.StyleRoles,
                defaultPageLayout = _options.DefaultPageLayout,
                stylePresetNames = _options.StylePresets.ConvertAll(style => style.Name),
                tableStylePresetNames = _options.TableStylePresets.ConvertAll(style => style.Name),
                outputFormats = new[] { "tx", "rtf", "docx", "pdf", "html", "md", "txt" },
                discovery = new
                {
                    ordinaryDocumentCreation = "create_document_from_markdown",
                    reusableTemplates = "list_document_recipes",
                    customAuthoring = "get_authoring_guide",
                    importedDocumentStyling = "apply_document_preset_styles"
                }
            };
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns a complete self-contained authoring guide for advanced document construction. Includes workflows, schemas, examples, presets, valid values, and troubleshooting. This is a heavyweight response; ordinary fixed-content documents should use create_document_from_markdown directly.")]
    public object GetAuthoringGuide()
    {
        try
        {
            return _authoringGuide.Build();
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Lists compact server-owned templates and advanced document recipes by name and goal. Use this when the user requests reusable merge fields, repeating blocks, form fields, or another template workflow. For an ordinary document with concrete fixed content, use create_document_from_markdown instead.")]
    public object ListDocumentRecipes()
    {
        try
        {
            return new
            {
                recipes = _authoringGuide.GetRecipes().Select(recipe => new DocumentRecipeSummary
                {
                    Name = recipe.Name,
                    Goal = recipe.Goal
                })
            };
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Creates a new reusable template or advanced document by executing a server-owned recipe returned by list_document_recipes. Prefer this for merge fields, repeating blocks, form fields, and established template semantics. For ordinary fixed content, use create_document_from_markdown. If mergeFields or repeatingBlocks are returned, populate them with merge_template before export.")]
    public object CreateDocumentFromRecipe(CreateDocumentFromRecipeRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            AuthoringRecipe recipe = _authoringGuide.GetRecipe(request.RecipeName);
            if (!recipe.ExampleRequest.TryGetValue("request", out object? recipeRequest)
                || recipeRequest is null)
            {
                throw new InvalidOperationException($"Document recipe '{recipe.Name}' has no executable request.");
            }

            JsonElement payload = JsonSerializer.SerializeToElement(recipeRequest);
            ApplyOperationsResponse response;
            ApplyOperationsRequest? operationsRequest = null;
            if (recipe.PreferredTool.Equals("create_document", StringComparison.OrdinalIgnoreCase))
            {
                RenderDocumentModelRequest modelRequest = payload.Deserialize<RenderDocumentModelRequest>()
                    ?? throw new InvalidOperationException($"Document recipe '{recipe.Name}' has an invalid model request.");
                modelRequest.SessionId = null;
                modelRequest.CreateIfMissing = true;
                response = _workflow.RenderDocumentModel(modelRequest);
            }
            else if (recipe.PreferredTool.Equals("apply_operations", StringComparison.OrdinalIgnoreCase))
            {
                operationsRequest = payload.Deserialize<ApplyOperationsRequest>()
                    ?? throw new InvalidOperationException($"Document recipe '{recipe.Name}' has an invalid operation request.");
                operationsRequest.SessionId = null;
                operationsRequest.CreateIfMissing = true;
                response = _workflow.ApplyOperations(operationsRequest);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Document recipe '{recipe.Name}' uses unsupported tool '{recipe.PreferredTool}'.");
            }

            IReadOnlyList<DocumentOperation> operations = operationsRequest?.Operations ?? [];
            return new DocumentRecipeResponse
            {
                SessionId = response.SessionId,
                RecipeName = recipe.Name,
                OperationCount = response.Results.Count,
                MergeFields = ReadNames(operations, "append_merge_field", operation => operation.FieldName),
                RepeatingBlocks = ReadNames(operations, "append_merge_block", operation => operation.BlockName),
                FormFields = ReadNames(operations, "append_form_field", operation => operation.FieldName),
                Warnings = response.Warnings
            };
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns the neutral AI-facing document model for a session, including Document, Section, Paragraph, Run, Table, Image, Style, HeaderFooter, and Field structures captured by supported operations.")]
    public object GetDocumentModel(string sessionId)
    {
        try
        {
            return _workflow.GetDocumentModel(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    private static string[] ReadNames(
        IEnumerable<DocumentOperation> operations,
        string operationType,
        Func<DocumentOperation, string?> selector) =>
        operations
            .Where(operation => operation.Type.Equals(operationType, StringComparison.OrdinalIgnoreCase))
            .Select(selector)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    [McpServerTool, Description("Returns a compact structure summary for a session document: sections, block types, paragraph previews, authoritative live table count/dimensions, image alt text, field names, and header/footer presence. Use get_document_tables for ordered table details and ordinal table edits.")]
    public object GetDocumentStructure(string sessionId)
    {
        try
        {
            return _workflow.GetDocumentStructure(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns authoritative native TX paragraph styles for a session, including exact name, base/following style, character and paragraph definitions, built-in status, and live paragraph usage count. Prefer list_document_styles for the focused style workflow.")]
    public object GetDocumentStyles(string sessionId)
    {
        try
        {
            return _workflow.GetDocumentStyles(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Authoritative live-table inspection. Returns tableCount and all tables in document order with zero-based tableIndex, one-based tableNumber, actual TX table id, row/column counts, and cell text previews. Use this to answer how many tables exist and before requests such as 'the second table'. For row additions, pass the returned id or tableNumber to add_table_rows; do not infer tables from the neutral document model.")]
    public object GetDocumentTables(string sessionId)
    {
        try
        {
            return _workflow.GetDocumentTables(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns merge/application fields represented by the automation model, including field ids, names, values, properties, and model locations.")]
    public object GetDocumentFields(string sessionId)
    {
        try
        {
            return _workflow.GetDocumentFields(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns headers and footers represented by the automation model for each section, including type, text preview, and contained blocks.")]
    public object GetDocumentHeadersFooters(string sessionId)
    {
        try
        {
            return _workflow.GetDocumentHeadersFooters(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns actual TX Text Control MERGEFIELD ApplicationFields from the current session document, including whether each field is in the body, header, or footer. Use this to verify insertion and inspect a template before MailMerge.")]
    public object GetTemplateMergeFields(string sessionId)
    {
        try
        {
            return _workflow.GetTemplateMergeFields(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns actual TX Text Control merge-block SubTextParts from the current session document. Merge blocks are named txmb_<blockName>.")]
    public object GetTemplateMergeBlocks(string sessionId)
    {
        try
        {
            return _workflow.GetTemplateMergeBlocks(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns actual TX Text Control form fields from the current session document, including text, selection/dropdown, checkbox, date, and whether each field is in the body, header, or footer. Use this to verify insertion.")]
    public object GetTemplateFormFields(string sessionId)
    {
        try
        {
            return _workflow.GetTemplateFormFields(sessionId);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Merges JSON data into the current session template using TX Text Control MailMerge.MergeJsonData. Build templates with insert_merge_field, create_merge_block, and insert_form_field (or their batched append_* operations), then verify the real fields before merging. Set formFieldMergeType to preselect to keep form fields editable or replace to flatten them.")]
    public object MergeTemplate(MergeTemplateRequest request)
    {
        try
        {
            return _workflow.MergeTemplate(request);
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }
}
