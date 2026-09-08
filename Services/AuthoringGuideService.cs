using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services;

public sealed class AuthoringGuideService
{
    private readonly DocumentOperationRegistry _registry;
    private readonly DocumentAutomationOptions _options;
    private readonly Lazy<IReadOnlyList<AuthoringRecipe>> _recipes;

    public AuthoringGuideService(
        DocumentOperationRegistry registry,
        IOptions<DocumentAutomationOptions> options)
    {
        _registry = registry;
        _options = options.Value;
        _recipes = new Lazy<IReadOnlyList<AuthoringRecipe>>(BuildRecipes);
    }

    public AuthoringGuideResponse Build()
    {
        var operationSchemas = _registry.GetOperationDescriptors();
        return new AuthoringGuideResponse
        {
            Summary = "Authoritative routing and authoring reference. Classify the request as inspect, edit, convert, or create; run only that workflow unless the user explicitly combines intents.",
            RecommendedWorkflow =
            [
                "QUESTION: load_document once for an upload, then call inspect_document with the same sessionId and the user's question as query. Keep each model-visible chunk at or below 12000 characters and follow nextParagraphIndex only when truncated content is needed. Never replace paging with get_text. Answer from returned indexed paragraphs. Do not mutate or export.",
                "CATEGORY/ACTIONS: call classify_document with the active sessionId to obtain a broad document category and reusable suggested actions. It is deterministic, reads the live document, and does not invoke an LLM.",
                "EDIT: load_document once for an upload. For a named clause or part, call inspect_document_section then replace_document_section with its contentHash. For exact text or indexes, call inspect_document then edit_document. Pass expectedText with character ranges, and confirm success only when the mutation result reports a change. Never recreate the document for an edit.",
                "PARAGRAPH FORMAT: call format_paragraph for alignment, paragraph spacing, line spacing, or a named paragraph style. For a browser selection pass selectedText as matchText and browser start as nearTextPosition; never translate a browser offset into paragraphIndex.",
                "TABLE FORMAT: call format_table for selected cells, a selected table header, a row, a column, or a complete table. For an editor selection pass selectedText as matchText, browser start as nearTextPosition, and browser selection length as selectionLength. Use scope selectedCells for selected cells and scope header for the header of the selected table. Never translate browser offsets into table/row/column indexes.",
                "TABLE INSPECTION/ROWS: call get_document_tables to answer table-count/structure questions and before resolving first/second/third table references. It reads the live TX document and returns tableCount, tableNumber, tableId, dimensions, and cells. Use add_table_rows with that tableNumber/id and count or row data; never guess ids.",
                "CONVERT: call convert_document only with either uploaded data plus sourceFormat or an existing sessionId, and outputFormat. Do not inspect, generate, rewrite, style, or use a recipe.",
                "CREATE: use create_document_from_markdown for ordinary fixed-content documents expressible with headings, paragraphs, lists, emphasis, and basic tables. Use a recipe for reusable templates/merge semantics, or create_document for explicit styling and advanced structures.",
                "For typical requests, use MCP server instructions, tool descriptions, and schemas directly; do not load this full guide preemptively.",
                "For a reusable template with merge fields, repeating blocks, or form fields, call list_document_recipes and execute a matching server-owned recipe with create_document_from_recipe.",
                "Use get_authoring_guide only for custom documents that cannot be planned from the tool schema or a server-owned recipe.",
                "For ordinary new documents, send complete raw Markdown to create_document_from_markdown. It imports and styles the document in one server-owned pass; never Base64-encode the Markdown or wrap the entire document in a code fence.",
                "For advanced custom documents that Markdown cannot represent, use create_document once with the complete semantic model. It applies configured defaults when presentation properties are omitted.",
                "For follow-up prompts that say change, modify, update, edit, adjust, make, increase, decrease, replace, add to it, or refer to the current/same/that document, reuse the existing sessionId. Do not create a new document unless the user explicitly asks for a new document.",
                "For an imported document that should use configured preset/default styles, call apply_document_preset_styles once with its sessionId. Do not inspect it or emit per-paragraph formatting operations first.",
                "After create_document, inspect warnings. Use apply_operations only when warnings indicate that rich model content needs a precise follow-up.",
                "Use apply_operations for incremental edits, template-specific actions, table formatting, merge blocks, form fields, search/replace, and layout changes.",
                "FIELD EDIT: use insert_merge_field to replace existing names/placeholders with real MERGEFIELD ApplicationFields. Prefer matchText plus replaceAll/occurrenceIndex, or an MCP-inspected start/length/expectedText range. For a browser selection use selectedText as both matchText and fieldText and browser start as nearTextPosition; browser table offsets are not exact server coordinates. Never emulate a field with ordinary {{name}} text.",
                "Set page size and margins before inserting wide tables because TX Text Control tables do not automatically reflow after page size changes.",
                "Use inspection tools after complex edits to retrieve generated table ids, field names, merge blocks, headers, footers, and paragraph indices.",
                "Prefer create_document_export with format docx, pdf, rtf, tx, html, md, or txt as the final step; use get_as_base64 only for compatibility clients."
            ],
            ToolMap = BuildToolMap(),
            DocumentModelContract = BuildDocumentModelContract(),
            OperationSchemas = operationSchemas,
            StyleRoles = _options.StyleRoles,
            DefaultParagraphStyleName = _options.DefaultParagraphStyleName,
            DefaultPageLayout = _options.DefaultPageLayout,
            StylePresets = _options.StylePresets,
            TableStylePresets = _options.TableStylePresets,
            StylePolicy = BuildStylePolicy(),
            SessionPolicy = BuildSessionPolicy(),
            ValueSets = BuildValueSets(),
            Recipes = _recipes.Value.ToList(),
            BestPractices =
            [
                "Set page size and margins before creating wide tables because tables do not automatically adapt after layout changes.",
                "Style omission policy: if the user prompt does not explicitly mention styling, fonts, colors, sizes, borders, spacing, or named styles/presets, omit all style-related properties and let the server defaults apply.",
                "Markdown-first creation policy: use one H1 title, H2/H3 hierarchy, and valid Markdown tables for ordinary fixed-content documents, then export the returned session separately when requested.",
                "For create_document, document.title uses styleRoles.title; paragraph.role values title, heading1, heading2, and body map to the corresponding server roles; unstyled paragraphs use body; and unstyled tables use the first configured table preset.",
                "Always encode tables as rows containing cells. Never represent a table row as a pipe-delimited paragraph. The server repairs this common weak-model mistake when it can do so without ambiguity, but correct structure is faster and more reliable.",
                "For apply_operations document creation, the first unstyled body paragraph uses styleRoles.title, later unstyled paragraphs use styleRoles.body, and append_table applies the first configured tableStylePresets entry unless a table styleName is explicitly supplied.",
                "For create_document, simple table cellStyle and uniform whole-cell run styles are rendered. Use apply_operations for precise table header/cell formatting, fields or form fields in cells, merge blocks, images in cells, spans, or mixed inline styles.",
                "Always inspect create_document warnings. A warning means the client should use follow-up operations for exact output.",
                "Session continuity rule: for follow-up edit requests, keep the current sessionId and call inspection tools before applying operations. Creating a new session loses the document the user asked to change.",
                "For a clause or part named by heading, use inspect_document_section and replace_document_section instead of guessing paragraph indexes. Preserve the heading and pass the exact contentHash to prevent stale edits.",
                "For restyling an imported document with server defaults, use apply_document_preset_styles. It preserves content, maps imported heading hierarchy to configured roles, and applies the default page layout.",
                "Use configured style role names for common content when you need an explicit override: styleRoles.title, styleRoles.heading1, styleRoles.heading2, and styleRoles.body.",
                "Use explicit styleName only when the user asks for a specific style; otherwise rely on configured defaults for body text and tables.",
                "When user instructions conflict with configured defaults, user instructions win. Apply explicit format operations after default presets.",
                "For tables, create the table first, inspect or use the returned tableId, then apply table presets, column widths, header formatting, cell backgrounds, and borders.",
                "For editor table selections, use format_table directly: scope selectedCells changes all selected cells, while scope header resolves the selected table and changes its header row. The server resolves authoritative TX table coordinates from the selected text and near-position hint.",
                "For templates, insert merge fields and form fields with stable fieldName values, inspect them, then merge JSON using merge_template.",
                "Use insert_merge_field and insert_form_field for model-friendly positional field insertion in the body, paragraphs, table cells, headers, or footers. Use create_merge_block for an existing table row, character range, or paragraph range.",
                "Use focused update_merge_field/update_form_field tools for existing field values and names. Clear field markup only on an explicit request, normally with keepText=true.",
                "After any field mutation, verify actual markup with get_template_merge_fields, get_template_form_fields, or get_template_merge_blocks before claiming success.",
                "Use tableId values returned by apply_operations/get_document_tables. New tables may omit tableId and let the server choose one.",
                "Use get_document_tables for table counts and ordinal references even on uploaded RTF/DOCX/PDF documents; its tableCount and ordered Tables collection come from the live TX document, not the optional neutral model.",
                "Avoid raw text offsets when a semantic operation exists. Prefer format_paragraph, format_table, format_text_occurrences, replace_text, paragraphIndex, tableId/rowIndex/columnIndex, or model-first rendering."
            ],
            Troubleshooting =
            [
                "If an operation fails as disabled, call get_authoring_guide and check enabled operationSchemas and capabilityPacks.",
                "If table formatting targets the wrong table, call get_document_tables and use the returned tableId.",
                "If merge data does not fill, call get_template_merge_fields and get_template_merge_blocks to retrieve actual TX field and block names.",
                "If a placeholder remains ordinary text such as {{name}}, replace it with insert_merge_field using matchText; plain placeholder text is not merge-field markup.",
                "If an exported layout looks too wide, set_section_layout first and then recreate or resize table columns.",
                "If a style is not applied, inspect get_document_styles and get_document_structure. For model-first documents, omitted paragraph styles should resolve to the configured body role.",
                "If table data appears as text separated by vertical bars, the model emitted paragraphs instead of table rows. Use document.table.rows[].cells[]; create_document can recover only unambiguous adjacent rows."
            ]
        };
    }

    public IReadOnlyList<AuthoringRecipe> GetRecipes()
        => _recipes.Value;

    public AuthoringRecipe GetRecipe(string recipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        return _recipes.Value.FirstOrDefault(recipe =>
                recipe.Name.Equals(recipeName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown document recipe '{recipeName}'.");
    }

    private static StylePolicyResponse BuildStylePolicy()
        => new()
        {
            Summary = "If the user prompt does not explicitly request styling, omit style-related properties. Do not invent style names, inline styles, paragraph formatting, cell formatting, table style names, colors, fonts, sizes, spacing, borders, or alignment. The server applies configured defaults automatically. Send style information only when the user explicitly requests it or names a configured style/preset.",
            PropertiesToOmitUnlessExplicitlyRequested =
            [
                "styleName",
                "style",
                "runs[].styleName",
                "runs[].style",
                "paragraph",
                "paragraphStyle",
                "cellStyle",
                "tableStyleName",
                "fontName",
                "fontSize",
                "fontSizeUnit",
                "bold",
                "italic",
                "underline",
                "colorHex",
                "backgroundColorHex",
                "border",
                "alignment",
                "spaceBefore",
                "spaceAfter",
                "lineSpacing"
            ],
            AutomaticDefaults =
            [
                "document.title is rendered with styleRoles.title.",
                "Sections without pageLayout use the configured defaultPageLayout.",
                "paragraph.role values title, heading1, heading2, and body resolve through styleRoles without exposing deployment-specific style names to the model.",
                "Unstyled paragraphs and headers/footers use styleRoles.body or DefaultParagraphStyleName.",
                "The first unstyled body paragraph in apply_operations document creation uses styleRoles.title.",
                "Unstyled tables in create_document and append_table use the first configured tableStylePresets entry.",
                "Supported professional document profiles fill unspecified hierarchy, spacing, table widths, and numeric alignment while preserving explicit presentation properties.",
                "Explicit user style instructions override configured defaults."
            ],
            ExplicitStyleTriggers =
            [
                "The user mentions a font, font size, color, background, border, spacing, alignment, bold, italic, underline, or a named style/preset.",
                "The user asks to change, format, style, highlight, color, resize, align, or restyle content.",
                "The user explicitly identifies a style such as Title, Heading, Body, or a table preset by name."
            ]
        };

    private static SessionPolicyResponse BuildSessionPolicy()
        => new()
        {
            Summary = "Follow-up edit prompts must reuse the existing sessionId and inspect the current document before applying changes. Do not create a new document/session when the user asks to change the current, same, previous, or that document unless the user explicitly asks for a new document.",
            FollowUpEditTriggers =
            [
                "change",
                "modify",
                "update",
                "edit",
                "adjust",
                "make",
                "increase",
                "decrease",
                "replace",
                "add to it",
                "remove from it",
                "current document",
                "same document",
                "that document",
                "previous document"
            ],
            RecommendedInspectionToolsBeforeEditing =
            [
                "inspect_document",
                "inspect_document_section",
                "get_document_structure",
                "get_document_tables",
                "get_document_model",
                "get_document_styles",
                "get_document_headers_footers"
            ]
        };

    private static AuthoringToolMap BuildToolMap()
        => new()
        {
            Inspect =
            [
                "inspect_document",
                "inspect_document_section",
                "get_document_structure",
                "get_document_model",
                "get_document_styles",
                "get_document_tables",
                "get_document_fields",
                "get_document_headers_footers",
                "get_template_merge_fields",
                "get_template_merge_blocks",
                "get_template_form_fields",
                "get_text",
                "get_paragraphs",
                "search_text",
                "search_text_ranges"
            ]
        };

    private static DocumentModelContractResponse BuildDocumentModelContract()
        => new()
        {
            SupportedBlockTypes = ["paragraph", "table", "image", "field"],
            RenderableFieldTypes = ["merge", "form"],
            MinimalDocumentShape = new Dictionary<string, object?>
            {
                ["document"] = new Dictionary<string, object?>
                {
                    ["title"] = "Document title",
                    ["sections"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["blocks"] = new object[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "paragraph",
                                    ["paragraph"] = new Dictionary<string, object?>
                                    {
                                        ["role"] = "body",
                                        ["text"] = "Body text"
                                    }
                                }
                            }
                        }
                    }
                }
            },
            Notes =
            [
                "Every block requires a type matching the populated payload property.",
                "Paragraphs may use text for plain content or ordered runs for inline content. Use semantic role title, heading1, heading2, or body for hierarchy; role names resolve through configured styleRoles.",
                "paragraphStyle.lineSpacing accepts a multiplier such as 1.08 (preferred) or a percentage such as 108 for compatibility.",
                "When the prompt has no style instructions, omit styleName, inline style, paragraphStyle, cellStyle, table style, and page layout fields; create_document applies configured defaults.",
                "Tables must store every logical row in rows and every value in cells. Do not put pipe-delimited table data in paragraph text.",
                "Table cells may contain paragraph blocks and simple cellStyle. Uniform whole-cell run styles are rendered. Mixed inline styles, fields, form fields, images, spans, and complex rich content in cells require apply_operations and may produce warnings.",
                "Table cells contain blocks, so paragraphs, merge fields, and form fields can be inserted into cells.",
                "Images can use source as a server path, base64 string, or data URI depending on the rendering path.",
                "Field type 'merge' maps to MERGEFIELD ApplicationFields; field type 'form' maps to TX Text Control form fields where supported."
            ]
        };

    private static Dictionary<string, IReadOnlyList<string>> BuildValueSets()
        => new()
        {
            ["outputFormats"] = ["tx", "rtf", "docx", "pdf", "html", "md", "txt"],
            ["fontSizeUnits"] = ["pt", "px"],
            ["layoutUnits"] = ["pt", "in", "cm", "mm", "twip", "twips"],
            ["pageSizes"] = ["A3", "A4", "A5", "Letter", "Legal", "Executive"],
            ["orientation"] = ["portrait", "landscape"],
            ["paragraphAlignment"] = ["left", "right", "center", "justify"],
            ["paragraphRoles"] = ["title", "heading1", "heading2", "body"],
            ["cellHorizontalAlignment"] = ["left", "right", "center", "justify"],
            ["cellVerticalAlignment"] = ["top", "center", "bottom"],
            ["tablePlacement"] = ["end", "before", "after"],
            ["fieldPlacement"] = ["end", "start", "replace"],
            ["formFieldTypes"] = ["text", "selection", "dropdown", "combobox", "checkbox", "check", "date"],
            ["headerFooterTypes"] = ["header", "footer", "firstPageHeader", "firstPageFooter", "evenHeader", "evenFooter"],
            ["sectionBreakKinds"] = ["beginAtNewPage", "newPage", "beginAtNewLine", "continuous"],
            ["imageFormats"] = ["bmp", "tif", "wmf", "png", "jpg", "jpeg", "gif", "emf", "svg"],
            ["imageTargets"] = ["body", "header", "footer", "firstPageHeader", "firstPageFooter", "evenHeader", "evenFooter"],
            ["imageInsertionModes"] = ["aboveText", "belowText", "displaceText", "displaceCompleteLines"],
            ["imageAlignment"] = ["left", "right", "center", "centered"],
            ["imageSaveMode"] = ["data", "reference"],
            ["formFieldMergeTypes"] = ["none", "preselect", "replace"]
        };

    private List<AuthoringRecipe> BuildRecipes()
        =>
        [
            BuildStyledDocumentRecipe(),
            BuildTableDocumentRecipe(),
            BuildInvoiceTemplateRecipe(),
            BuildPatientFormRecipe(),
            BuildHeaderImageRecipe()
        ];

    private AuthoringRecipe BuildStyledDocumentRecipe()
        => new()
        {
            Name = "styled-document-docx",
            Goal = "Create a styled document with title, headings, body paragraphs, and inline emphasis.",
            PreferredTool = "create_document",
            ToolSequence = ["create_document", "create_document_export"],
            ExampleRequest = new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["createIfMissing"] = true,
                    ["document"] = new Dictionary<string, object?>
                    {
                        ["styles"] = new object[]
                        {
                            new Dictionary<string, object?> { ["name"] = "Title", ["type"] = "paragraph", ["text"] = new Dictionary<string, object?> { ["fontName"] = "Arial", ["fontSize"] = 30, ["fontSizeUnit"] = "pt", ["bold"] = true } },
                            new Dictionary<string, object?> { ["name"] = "Body", ["type"] = "paragraph", ["text"] = new Dictionary<string, object?> { ["fontName"] = "Arial", ["fontSize"] = 12, ["fontSizeUnit"] = "pt" } }
                        },
                        ["sections"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["blocks"] = new object[]
                                {
                                    ParagraphBlock("Title", "Quarterly Report"),
                                    ParagraphBlock("Body", "Revenue grew across enterprise accounts and partner channels."),
                                    new Dictionary<string, object?>
                                    {
                                        ["type"] = "paragraph",
                                        ["paragraph"] = new Dictionary<string, object?>
                                        {
                                            ["styleName"] = "Body",
                                            ["runs"] = new object[]
                                            {
                                                new Dictionary<string, object?> { ["text"] = "The " },
                                                new Dictionary<string, object?> { ["text"] = "and", ["style"] = new Dictionary<string, object?> { ["bold"] = true, ["colorHex"] = "#d92d20" } },
                                                new Dictionary<string, object?> { ["text"] = " marker can be emphasized inline." }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            FollowUp = ["Call create_document_export with { sessionId, format: 'docx' }."]
        };

    private AuthoringRecipe BuildTableDocumentRecipe()
        => new()
        {
            Name = "table-with-default-preset",
            Goal = "Create a document with a page layout, table, default table preset, and later header override.",
            PreferredTool = "apply_operations",
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_document_tables", "apply_operations", "create_document_export"],
            ExampleRequest = new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["createIfMissing"] = true,
                    ["operations"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "set_section_layout", ["pageSize"] = "Letter", ["orientation"] = "portrait", ["unit"] = "in", ["marginLeft"] = 1, ["marginRight"] = 1, ["marginTop"] = 1, ["marginBottom"] = 1 },
                        new Dictionary<string, object?> { ["type"] = "append_paragraph", ["styleName"] = "Title", ["text"] = "Sales by Country" },
                        new Dictionary<string, object?> { ["type"] = "append_table", ["tableId"] = "10", ["rows"] = new object[] { new[] { "Country", "Sales", "Qty" }, new[] { "Germany", "$842,000", "1280" }, new[] { "USA", "$1,240,000", "1985" } } },
                        new Dictionary<string, object?> { ["type"] = "apply_table_style_preset", ["tableId"] = "10", ["styleName"] = "Professional Blue" },
                        new Dictionary<string, object?> { ["type"] = "format_table_header_row", ["tableId"] = "10", ["style"] = new Dictionary<string, object?> { ["colorHex"] = "#ffffff", ["bold"] = true }, ["cellStyle"] = new Dictionary<string, object?> { ["backgroundColorHex"] = "#d0006f" } }
                    }
                }
            },
            FollowUp = ["Use get_document_tables to retrieve table ids before follow-up table edits."]
        };

    private AuthoringRecipe BuildInvoiceTemplateRecipe()
        => new()
        {
            Name = "invoice-template-mail-merge",
            Goal = "Create a polished invoice, optionally populated with supplied values, using merge fields and a repeating line-items block.",
            PreferredTool = "apply_operations",
            ToolSequence = ["create_document_from_recipe", "merge_template", "create_document_export"],
            ExampleRequest = new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["createIfMissing"] = true,
                    ["operations"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "set_section_layout", ["pageSize"] = "Letter", ["unit"] = "in", ["marginLeft"] = 1, ["marginRight"] = 1, ["marginTop"] = 1, ["marginBottom"] = 1 },
                        new Dictionary<string, object?> { ["type"] = "append_paragraph", ["styleName"] = _options.StyleRoles.Title, ["text"] = "INVOICE" },
                        new Dictionary<string, object?> { ["type"] = "append_table", ["tableId"] = "10", ["rows"] = new object[] { new[] { "Invoice Number", "Invoice Date", "Due Date", "Customer" }, new[] { "", "", "", "" } } },
                        MergeFieldCell("InvoiceNumber", "10", 1, 0),
                        MergeFieldCell("InvoiceDate", "10", 1, 1),
                        MergeFieldCell("DueDate", "10", 1, 2),
                        MergeFieldCell("CustomerName", "10", 1, 3),
                        new Dictionary<string, object?> { ["type"] = "append_table", ["tableId"] = "11", ["rows"] = new object[] { new[] { "Item", "Description", "Qty", "Unit Price", "Line Total" }, new[] { "", "", "", "", "" } } },
                        MergeFieldCell("ItemName", "11", 1, 0),
                        MergeFieldCell("Description", "11", 1, 1),
                        MergeFieldCell("Quantity", "11", 1, 2),
                        MergeFieldCell("UnitPrice", "11", 1, 3),
                        MergeFieldCell("LineTotal", "11", 1, 4),
                        new Dictionary<string, object?> { ["type"] = "append_merge_block", ["blockName"] = "lineItems", ["tableId"] = "11", ["rowIndex"] = 1 },
                        new Dictionary<string, object?> { ["type"] = "append_table", ["tableId"] = "12", ["rows"] = new object[] { new[] { "Subtotal", "" }, new[] { "Tax", "" }, new[] { "Total", "" } } },
                        MergeFieldCell("Subtotal", "12", 0, 1),
                        MergeFieldCell("Tax", "12", 1, 1),
                        MergeFieldCell("Total", "12", 2, 1),
                        new Dictionary<string, object?> { ["type"] = "append_paragraph", ["styleName"] = _options.StyleRoles.Heading2, ["text"] = "Payment Terms" },
                        new Dictionary<string, object?> { ["type"] = "append_merge_field", ["fieldName"] = "PaymentTerms", ["fieldText"] = "Payment terms" }
                    }
                }
            },
            FollowUp =
            [
                "Call merge_template with InvoiceNumber, InvoiceDate, DueDate, CustomerName, Subtotal, Tax, Total, PaymentTerms, and a lineItems array.",
                "Each lineItems entry uses ItemName, Description, Quantity, UnitPrice, and LineTotal."
            ]
        };

    private AuthoringRecipe BuildPatientFormRecipe()
        => new()
        {
            Name = "fillable-patient-form",
            Goal = "Create a fillable patient intake form with form fields in table cells.",
            PreferredTool = "apply_operations",
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_template_form_fields", "create_document_export"],
            ExampleRequest = new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["createIfMissing"] = true,
                    ["operations"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "set_header_footer", ["headerFooterType"] = "header", ["text"] = "Patient Intake" },
                        new Dictionary<string, object?> { ["type"] = "append_paragraph", ["styleName"] = "Title", ["text"] = "Patient Intake Form" },
                        new Dictionary<string, object?> { ["type"] = "append_table", ["tableId"] = "10", ["rows"] = new object[] { new[] { "Patient Name", "" }, new[] { "Date of Birth", "" }, new[] { "Emergency Contact", "" } } },
                        FormFieldCell("PatientName", "text", "10", 0, 1),
                        FormFieldCell("DateOfBirth", "date", "10", 1, 1),
                        FormFieldCell("EmergencyContact", "text", "10", 2, 1)
                    }
                }
            },
            FollowUp = ["Export as docx for editable form fields or pdf for a fillable PDF workflow."]
        };

    private AuthoringRecipe BuildHeaderImageRecipe()
        => new()
        {
            Name = "header-logo-and-footer-date",
            Goal = "Add a logo to the header and a date field to the footer.",
            PreferredTool = "apply_operations",
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_document_headers_footers", "create_document_export"],
            ExampleRequest = new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["createIfMissing"] = true,
                    ["operations"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "append_image", ["target"] = "header", ["imagePath"] = "C:\\Images\\logo.svg", ["altText"] = "Company logo", ["horizontalScaling"] = 50, ["verticalScaling"] = 50, ["alignment"] = "right" },
                        new Dictionary<string, object?> { ["type"] = "set_header_footer", ["headerFooterType"] = "footer", ["text"] = "Date: ", ["typeName"] = "DATE", ["dateFormat"] = "d" },
                        new Dictionary<string, object?> { ["type"] = "append_paragraph", ["styleName"] = "Title", ["text"] = "Document with Header Logo" }
                    }
                }
            },
            FollowUp = ["Use supported image formats from valueSets.imageFormats."]
        };

    private static Dictionary<string, object?> ParagraphBlock(string styleName, string text)
        => new()
        {
            ["type"] = "paragraph",
            ["paragraph"] = new Dictionary<string, object?>
            {
                ["styleName"] = styleName,
                ["runs"] = new object[] { new Dictionary<string, object?> { ["text"] = text } }
            }
        };

    private static Dictionary<string, object?> MergeFieldCell(string fieldName, string tableId, int rowIndex, int columnIndex)
        => new()
        {
            ["type"] = "append_merge_field",
            ["fieldName"] = fieldName,
            ["tableId"] = tableId,
            ["rowIndex"] = rowIndex,
            ["columnIndex"] = columnIndex,
            ["placement"] = "replace"
        };

    private static Dictionary<string, object?> FormFieldCell(string fieldName, string formFieldType, string tableId, int rowIndex, int columnIndex)
        => new()
        {
            ["type"] = "append_form_field",
            ["fieldName"] = fieldName,
            ["formFieldType"] = formFieldType,
            ["tableId"] = tableId,
            ["rowIndex"] = rowIndex,
            ["columnIndex"] = columnIndex,
            ["placement"] = "replace"
        };
}
