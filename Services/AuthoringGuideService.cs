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

    public AuthoringGuideService(
        DocumentOperationRegistry registry,
        IOptions<DocumentAutomationOptions> options)
    {
        _registry = registry;
        _options = options.Value;
    }

    public AuthoringGuideResponse Build()
    {
        var operationSchemas = _registry.GetOperationDescriptors();
        return new AuthoringGuideResponse
        {
            Summary = "Self-contained guide for AI clients that only have MCP access. Start here, then create documents with render_document_model or apply_operations, inspect results, and export with get_as_base64.",
            RecommendedWorkflow =
            [
                "Call get_authoring_guide before planning a document.",
                "For new documents, prefer render_document_model when the requested document can be expressed as sections, paragraphs, runs, tables, images, headers, footers, and fields.",
                "Use apply_operations for incremental edits, template-specific actions, table formatting, merge blocks, form fields, search/replace, and layout changes.",
                "Set page size and margins before inserting wide tables because TX Text Control tables do not automatically reflow after page size changes.",
                "Use inspection tools after complex edits to retrieve generated table ids, field names, merge blocks, headers, footers, and paragraph indices.",
                "Call get_as_base64 with format docx, pdf, tx, html, or md as the final step."
            ],
            ToolMap = BuildToolMap(),
            DocumentModelContract = BuildDocumentModelContract(),
            OperationSchemas = operationSchemas,
            StyleRoles = _options.StyleRoles,
            DefaultParagraphStyleName = _options.DefaultParagraphStyleName,
            StylePresets = _options.StylePresets,
            TableStylePresets = _options.TableStylePresets,
            ValueSets = BuildValueSets(),
            Recipes = BuildRecipes(),
            BestPractices =
            [
                "Set page size and margins before creating wide tables because tables do not automatically adapt after layout changes.",
                "Use configured style role names for common content: styleRoles.title, styleRoles.heading1, styleRoles.heading2, and styleRoles.body.",
                "Use explicit styleName only when the user asks for a specific style; otherwise rely on defaultParagraphStyleName for body text.",
                "When user instructions conflict with configured defaults, user instructions win. Apply explicit format operations after default presets.",
                "For tables, create the table first, inspect or use the returned tableId, then apply table presets, column widths, header formatting, cell backgrounds, and borders.",
                "For templates, insert merge fields and form fields with stable fieldName values, inspect them, then merge JSON using merge_template.",
                "Use tableId values returned by apply_operations/get_document_tables. New tables may omit tableId and let the server choose one.",
                "Avoid raw text offsets when a semantic operation exists. Prefer format_text_occurrences, replace_text, paragraphIndex, tableId/rowIndex/columnIndex, or model-first rendering."
            ],
            Troubleshooting =
            [
                "If an operation fails as disabled, call get_authoring_guide and check enabled operationSchemas and capabilityPacks.",
                "If table formatting targets the wrong table, call get_document_tables and use the returned tableId.",
                "If merge data does not fill, call get_template_merge_fields and get_template_merge_blocks to retrieve actual TX field and block names.",
                "If an exported layout looks too wide, set_section_layout first and then recreate or resize table columns.",
                "If a style is not applied, inspect get_document_styles and make sure the paragraph or run uses the intended styleName."
            ]
        };
    }

    private static AuthoringToolMap BuildToolMap()
        => new()
        {
            Inspect =
            [
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
                    ["styles"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = "Title",
                            ["type"] = "paragraph",
                            ["text"] = new Dictionary<string, object?>
                            {
                                ["fontName"] = "Arial",
                                ["fontSize"] = 24,
                                ["fontSizeUnit"] = "pt",
                                ["bold"] = true
                            }
                        }
                    },
                    ["sections"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["pageLayout"] = new Dictionary<string, object?>
                            {
                                ["pageSize"] = "Letter",
                                ["orientation"] = "portrait",
                                ["unit"] = "in",
                                ["marginLeft"] = 1,
                                ["marginRight"] = 1,
                                ["marginTop"] = 1,
                                ["marginBottom"] = 1
                            },
                            ["blocks"] = new object[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "paragraph",
                                    ["paragraph"] = new Dictionary<string, object?>
                                    {
                                        ["styleName"] = "Title",
                                        ["runs"] = new object[]
                                        {
                                            new Dictionary<string, object?> { ["text"] = "Document title" }
                                        }
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
                "Paragraphs contain ordered runs; runs may use styleName or inline style.",
                "Table cells contain blocks, so paragraphs, merge fields, and form fields can be inserted into cells.",
                "Images can use source as a server path, base64 string, or data URI depending on the rendering path.",
                "Field type 'merge' maps to MERGEFIELD ApplicationFields; field type 'form' maps to TX Text Control form fields where supported."
            ]
        };

    private static Dictionary<string, IReadOnlyList<string>> BuildValueSets()
        => new()
        {
            ["outputFormats"] = ["tx", "docx", "pdf", "html", "md"],
            ["fontSizeUnits"] = ["pt", "px"],
            ["layoutUnits"] = ["pt", "in", "cm", "mm", "twip", "twips"],
            ["pageSizes"] = ["A3", "A4", "A5", "Letter", "Legal", "Executive"],
            ["orientation"] = ["portrait", "landscape"],
            ["paragraphAlignment"] = ["left", "right", "center", "justify"],
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
            PreferredTool = "render_document_model",
            ToolSequence = ["get_authoring_guide", "render_document_model", "get_document_structure", "get_as_base64"],
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
            FollowUp = ["Call get_as_base64 with { sessionId, format: 'docx' }."]
        };

    private AuthoringRecipe BuildTableDocumentRecipe()
        => new()
        {
            Name = "table-with-default-preset",
            Goal = "Create a document with a page layout, table, default table preset, and later header override.",
            PreferredTool = "apply_operations",
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_document_tables", "apply_operations", "get_as_base64"],
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
            Goal = "Create an invoice template with merge fields and a repeating line-items merge block.",
            PreferredTool = "apply_operations",
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_template_merge_fields", "get_template_merge_blocks", "merge_template", "get_as_base64"],
            ExampleRequest = new Dictionary<string, object?>
            {
                ["request"] = new Dictionary<string, object?>
                {
                    ["createIfMissing"] = true,
                    ["operations"] = new object[]
                    {
                        new Dictionary<string, object?> { ["type"] = "set_section_layout", ["pageSize"] = "Letter", ["unit"] = "in", ["marginLeft"] = 1, ["marginRight"] = 1, ["marginTop"] = 1, ["marginBottom"] = 1 },
                        new Dictionary<string, object?> { ["type"] = "append_paragraph", ["styleName"] = "Title", ["text"] = "Payment Invoice" },
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
                        new Dictionary<string, object?> { ["type"] = "append_merge_block", ["blockName"] = "lineItems", ["tableId"] = "11", ["rowIndex"] = 1 }
                    }
                }
            },
            FollowUp =
            [
                "Use get_template_merge_fields and get_template_merge_blocks to build JSON with names like InvoiceNumber and lineItems.",
                "Call merge_template with jsonData containing a lineItems array."
            ]
        };

    private AuthoringRecipe BuildPatientFormRecipe()
        => new()
        {
            Name = "fillable-patient-form",
            Goal = "Create a fillable patient intake form with form fields in table cells.",
            PreferredTool = "apply_operations",
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_template_form_fields", "get_as_base64"],
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
            ToolSequence = ["get_authoring_guide", "apply_operations", "get_document_headers_footers", "get_as_base64"],
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
