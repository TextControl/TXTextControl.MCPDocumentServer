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

    [JsonPropertyName("stylePresets")]
    public List<TextStyleDefinition> StylePresets { get; set; } = [];

    [JsonPropertyName("tableStylePresets")]
    public List<TableStylePresetDefinition> TableStylePresets { get; set; } = [];

    [JsonPropertyName("valueSets")]
    public Dictionary<string, IReadOnlyList<string>> ValueSets { get; set; } = new();

    [JsonPropertyName("recipes")]
    public List<AuthoringRecipe> Recipes { get; set; } = [];

    [JsonPropertyName("bestPractices")]
    public List<string> BestPractices { get; set; } = [];

    [JsonPropertyName("troubleshooting")]
    public List<string> Troubleshooting { get; set; } = [];
}

public sealed class AuthoringToolMap
{
    [JsonPropertyName("discover")]
    public string Discover { get; set; } = "get_authoring_guide";

    [JsonPropertyName("createModelFirst")]
    public string CreateModelFirst { get; set; } = "render_document_model";

    [JsonPropertyName("editOperationFirst")]
    public string EditOperationFirst { get; set; } = "apply_operations";

    [JsonPropertyName("export")]
    public string Export { get; set; } = "get_as_base64";

    [JsonPropertyName("loadExisting")]
    public string LoadExisting { get; set; } = "load_from_base64";

    [JsonPropertyName("mergeTemplate")]
    public string MergeTemplate { get; set; } = "merge_template";

    [JsonPropertyName("inspect")]
    public List<string> Inspect { get; set; } = [];
}

public sealed class DocumentModelContractResponse
{
    [JsonPropertyName("primaryTool")]
    public string PrimaryTool { get; set; } = "render_document_model";

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
