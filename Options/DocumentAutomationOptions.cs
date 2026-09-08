using System.Collections.Generic;
using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Options;

public sealed class DocumentAutomationOptions
{
    public const string SectionName = "DocumentAutomation";

    public List<string>? EnabledCapabilityPacks { get; set; }
    public List<string>? EnabledOperations { get; set; }

    public List<TextStyleDefinition> StylePresets { get; set; } = [];

    public string DefaultParagraphStyleName { get; set; } = "Body";

    public StyleRoleDefinition StyleRoles { get; set; } = new();

    public PageLayoutDefinition? DefaultPageLayout { get; set; }

    public List<TableStylePresetDefinition> TableStylePresets { get; set; } = [];
}
