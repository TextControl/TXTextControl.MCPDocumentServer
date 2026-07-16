using System;
using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using TxTextControl.McpServer.Models.Requests;
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

    [McpServerTool, Description("Applies an ordered list of semantic document operations to a session. Session continuity rule: for follow-up prompts that ask to change, modify, update, edit, adjust, make, increase, decrease, replace, or refer to the current/same/that document, reuse the existing sessionId and inspect the current document first; do not create a new document unless the user explicitly asks for one. If request.sessionId is omitted and request.createIfMissing is true, a new document session is created. Call get_authoring_guide first for operation-specific schemas, examples, valid enum values, style presets, table presets, recipes, and stylePolicy. Style policy: if the user prompt does not explicitly request styling, omit styleName, style, paragraph, cellStyle, and tableStyleName; server defaults apply. Use this for incremental edits, table formatting, fields, merge blocks, form fields, sections, headers/footers, images, search/replace, and layout changes.")]
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

    [McpServerTool, Description("Renders a neutral AI-facing Document model into a real TX Text Control session document. Use this for new drafts, not for follow-up edits to an existing document unless a sessionId is supplied intentionally. Session continuity rule: when the user asks to change, modify, update, edit, adjust, make, increase, decrease, replace, or refers to the current/same/that document, reuse the existing sessionId and prefer apply_operations after inspection; do not create a new document unless explicitly requested. Style policy: if the user prompt does not explicitly request styling, omit styleName, style, paragraphStyle, cellStyle, tableStyleName, fonts, colors, sizes, borders, spacing, and alignment. Configured defaults are applied automatically: document.title uses the title style role, unstyled paragraphs and headers/footers use the body style role, and unstyled tables receive the first configured table style preset. Simple whole-cell table cellStyle and uniform cell run styles are rendered; richer table-cell content may return warnings. Always inspect warnings. Use apply_operations for precise table header/cell formatting, fields in cells, form fields, merge blocks, or targeted edits. Call get_authoring_guide first for the document model contract and full examples.")]
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

    [McpServerTool, Description("Returns the configured document automation capabilities, enabled operation types, enabled capability packs, configured style and table presets, operation schemas, and the complete external-AI authoring guide. Use this before planning operation payloads.")]
    public object GetDocumentAutomationCapabilities()
    {
        try
        {
            var guide = _authoringGuide.Build();
            return new
            {
                capabilityPacks = _registry.GetCapabilityPacks(),
                enabledCapabilityPacks = _settings.GetEnabledCapabilityPacks(),
                enabledOperations = _settings.GetEnabledOperations(),
                operations = _registry.GetCapabilities(),
                operationSchemas = guide.OperationSchemas,
                documentModelContract = guide.DocumentModelContract,
                defaultParagraphStyleName = _options.DefaultParagraphStyleName,
                styleRoles = _options.StyleRoles,
                stylePresetNames = _options.StylePresets.ConvertAll(style => style.Name),
                tableStylePresetNames = _options.TableStylePresets.ConvertAll(style => style.Name),
                stylePresets = _options.StylePresets,
                tableStylePresets = _options.TableStylePresets,
                stylePolicy = guide.StylePolicy,
                sessionPolicy = guide.SessionPolicy,
                valueSets = guide.ValueSets,
                recipes = guide.Recipes,
                recommendedWorkflow = guide.RecommendedWorkflow,
                bestPractices = guide.BestPractices,
                troubleshooting = guide.Troubleshooting,
                authoringGuide = guide
            };
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex);
        }
    }

    [McpServerTool, Description("Returns a complete self-contained authoring guide for external AI clients. Includes recommended workflows, operation-specific schemas and examples, style/table preset definitions, valid enum values, document model contract, recipes for common document types, best practices, and troubleshooting. Call this first when the AI only has MCP access.")]
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

    [McpServerTool, Description("Returns a compact structure summary for a session document: sections, block types, paragraph previews, table ids/dimensions, image alt text, field names, and header/footer presence. Use this before planning targeted edits.")]
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

    [McpServerTool, Description("Returns styles known to the document automation model for a session, including text and paragraph style definitions captured by supported operations.")]
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

    [McpServerTool, Description("Returns table summaries for a session document, including table ids, row and column counts, cell text previews, cell styles, spans, and merge field names inside cells.")]
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

    [McpServerTool, Description("Returns actual TX Text Control MERGEFIELD ApplicationFields from the current session document. Use this to inspect a template before MailMerge.")]
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

    [McpServerTool, Description("Returns actual TX Text Control form fields from the current session document, including text, selection/dropdown, checkbox, and date fields.")]
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

    [McpServerTool, Description("Merges JSON data into the current session template using TX Text Control MailMerge.MergeJsonData. Build templates with append_merge_field, append_merge_block, and append_form_field first. Set formFieldMergeType to preselect to keep form fields editable or replace to flatten them.")]
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
