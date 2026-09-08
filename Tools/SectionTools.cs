using System;
using System.ComponentModel;
using ModelContextProtocol.Server;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services;

namespace TxTextControl.McpServer.Tools;

/// <summary>Heading-aware tools for reading and editing document sections.</summary>
[McpServerToolType]
public sealed class SectionTools
{
    private readonly DocumentWorkflowService _workflow;

    /// <summary>Initializes section tools for the document workflow.</summary>
    public SectionTools(DocumentWorkflowService workflow)
    {
        _workflow = workflow;
    }

    /// <summary>Finds a complete section from its visible heading.</summary>
    [McpServerTool(
        Name = "inspect_document_section",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DocumentSectionResponse)),
     Description("Resolves and reads a complete section in an existing document by its visible heading. Required: sessionId and heading. Heading numbering and case may be omitted. Returns the preserved heading, its style, exact body paragraph range, complete body text, and contentHash. Use this for requests referring to a named part such as 'No Warranty', 'Payment', or 'Termination'. Inspect any definitions or related clauses needed to draft the replacement separately. Before changing the section, pass this tool's exact heading and contentHash to replace_document_section.")]
    public object InspectDocumentSection(InspectDocumentSectionRequest request)
    {
        try
        {
            return _workflow.InspectDocumentSection(request);
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    /// <summary>Replaces a previously inspected section body while preserving its heading.</summary>
    [McpServerTool(
        Name = "replace_document_section",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DocumentSectionEditResponse)),
     Description("Replaces only the body of a named section in an existing document; it never recreates the document and preserves the heading and surrounding content. Required: sessionId, heading, expectedContentHash from the immediately preceding inspect_document_section result, and replacementText containing only the new body. If the section changed, the call fails and must be inspected again. Use this instead of paragraph indexes when the user identifies a clause or part by heading. After success, call inspect_document_section again only when the user needs verification details.")]
    public object ReplaceDocumentSection(ReplaceDocumentSectionRequest request)
    {
        try
        {
            return _workflow.ReplaceDocumentSection(request);
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }
}
