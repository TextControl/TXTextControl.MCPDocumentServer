using System.Collections.Generic;
using System.Text.Json.Serialization;
using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Models.Responses;

public sealed class AuthoringGuideResponse
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("recommendedWorkflow")]
    public List<string> RecommendedWorkflow { get; set; } = [];

    [JsonPropertyName("toolMap")]
    public AuthoringToolMap ToolMap { get; set; } = new();

    [JsonPropertyName("documentModelContract")]
    public DocumentModelContractResponse DocumentModelContract { get; set; } = new();

    [JsonPropertyName("operationSchemas")]
    public IReadOnlyList<DocumentOperationDescriptor> OperationSchemas { get; set; } = [];

    [JsonPropertyName("styleRoles")]
    public StyleRoleDefinition StyleRoles { get; set; } = new();

    [JsonPropertyName("defaultParagraphStyleName")]
    public string DefaultParagraphStyleName { get; set; } = "Body";

    [JsonPropertyName("defaultPageLayout")]
    public PageLayoutDefinition? DefaultPageLayout { get; set; }

    [JsonPropertyName("stylePresets")]
    public List<TextStyleDefinition> StylePresets { get; set; } = [];

    [JsonPropertyName("tableStylePresets")]
    public List<TableStylePresetDefinition> TableStylePresets { get; set; } = [];

    [JsonPropertyName("stylePolicy")]
    public StylePolicyResponse StylePolicy { get; set; } = new();

    [JsonPropertyName("sessionPolicy")]
    public SessionPolicyResponse SessionPolicy { get; set; } = new();

    [JsonPropertyName("valueSets")]
    public Dictionary<string, IReadOnlyList<string>> ValueSets { get; set; } = new();

    [JsonPropertyName("recipes")]
    public List<AuthoringRecipe> Recipes { get; set; } = [];

    [JsonPropertyName("bestPractices")]
    public List<string> BestPractices { get; set; } = [];

    [JsonPropertyName("troubleshooting")]
    public List<string> Troubleshooting { get; set; } = [];
}

public sealed class StylePolicyResponse
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("omitStylePropertiesWhenPromptHasNoStyleInstructions")]
    public bool OmitStylePropertiesWhenPromptHasNoStyleInstructions { get; set; } = true;

    [JsonPropertyName("propertiesToOmitUnlessExplicitlyRequested")]
    public List<string> PropertiesToOmitUnlessExplicitlyRequested { get; set; } = [];

    [JsonPropertyName("automaticDefaults")]
    public List<string> AutomaticDefaults { get; set; } = [];

    [JsonPropertyName("explicitStyleTriggers")]
    public List<string> ExplicitStyleTriggers { get; set; } = [];
}

public sealed class SessionPolicyResponse
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("reuseSessionForFollowUpEdits")]
    public bool ReuseSessionForFollowUpEdits { get; set; } = true;

    [JsonPropertyName("followUpEditTriggers")]
    public List<string> FollowUpEditTriggers { get; set; } = [];

    [JsonPropertyName("recommendedInspectionToolsBeforeEditing")]
    public List<string> RecommendedInspectionToolsBeforeEditing { get; set; } = [];
}

public sealed class AuthoringToolMap
{
    [JsonPropertyName("discover")]
    public string Discover { get; set; } = "get_authoring_guide";

    [JsonPropertyName("listRecipes")]
    public string ListRecipes { get; set; } = "list_document_recipes";

    [JsonPropertyName("createFromRecipe")]
    public string CreateFromRecipe { get; set; } = "create_document_from_recipe";

    [JsonPropertyName("createModelFirst")]
    public string CreateModelFirst { get; set; } = "create_document";

    [JsonPropertyName("createMarkdown")]
    public string CreateMarkdown { get; set; } = "create_document_from_markdown";

    [JsonPropertyName("inspectPrimary")]
    public string InspectPrimary { get; set; } = "inspect_document";

    [JsonPropertyName("editPrimary")]
    public string EditPrimary { get; set; } = "edit_document";

    [JsonPropertyName("inspectSection")]
    public string InspectSection { get; set; } = "inspect_document_section";

    [JsonPropertyName("replaceSection")]
    public string ReplaceSection { get; set; } = "replace_document_section";

    [JsonPropertyName("convert")]
    public string Convert { get; set; } = "convert_document";

    [JsonPropertyName("editOperationFirst")]
    public string EditOperationFirst { get; set; } = "apply_operations";

    [JsonPropertyName("formatParagraph")]
    public string FormatParagraph { get; set; } = "format_paragraph";

    [JsonPropertyName("formatTable")]
    public string FormatTable { get; set; } = "format_table";

    [JsonPropertyName("addTableRows")]
    public string AddTableRows { get; set; } = "add_table_rows";

    [JsonPropertyName("formatTextOccurrences")]
    public string FormatTextOccurrences { get; set; } = "format_text_occurrences";

    [JsonPropertyName("export")]
    public string Export { get; set; } = "create_document_export";

    [JsonPropertyName("loadExisting")]
    public string LoadExisting { get; set; } = "load_document";

    [JsonPropertyName("styleImported")]
    public string StyleImported { get; set; } = "apply_document_preset_styles";

    [JsonPropertyName("mergeTemplate")]
    public string MergeTemplate { get; set; } = "merge_template";

    [JsonPropertyName("inspect")]
    public List<string> Inspect { get; set; } = [];
}

public sealed class DocumentModelContractResponse
{
    [JsonPropertyName("primaryTool")]
    public string PrimaryTool { get; set; } = "create_document";

    [JsonPropertyName("fallbackTool")]
    public string FallbackTool { get; set; } = "apply_operations";

    [JsonPropertyName("supportedBlockTypes")]
    public List<string> SupportedBlockTypes { get; set; } = [];

    [JsonPropertyName("renderableFieldTypes")]
    public List<string> RenderableFieldTypes { get; set; } = [];

    [JsonPropertyName("minimalDocumentShape")]
    public Dictionary<string, object?> MinimalDocumentShape { get; set; } = new();

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = [];
}

public sealed class AuthoringRecipe
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("preferredTool")]
    public string PreferredTool { get; set; } = string.Empty;

    [JsonPropertyName("toolSequence")]
    public List<string> ToolSequence { get; set; } = [];

    [JsonPropertyName("exampleRequest")]
    public Dictionary<string, object?> ExampleRequest { get; set; } = new();

    [JsonPropertyName("followUp")]
    public List<string> FollowUp { get; set; } = [];
}
