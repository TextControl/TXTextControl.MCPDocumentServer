using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

/// <summary>Reports the preset styles applied to an existing document.</summary>
public sealed class ApplyDocumentPresetStylesResponse
{
    /// <summary>Gets or sets the unchanged MCP document session identifier.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of paragraphs styled.</summary>
    public int ParagraphsStyled { get; set; }

    /// <summary>Gets or sets the paragraph count grouped by configured style name.</summary>
    public IReadOnlyDictionary<string, int> AppliedStyles { get; set; } =
        new Dictionary<string, int>();

    /// <summary>Gets or sets the number of imported tables styled with the default table preset.</summary>
    public int TablesStyled { get; set; }

    /// <summary>Gets or sets whether the configured default page layout was applied.</summary>
    public bool PageLayoutApplied { get; set; }

    /// <summary>Gets or sets non-fatal configuration warnings.</summary>
    public List<string> Warnings { get; set; } = [];
}
