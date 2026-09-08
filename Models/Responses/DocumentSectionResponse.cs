using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

/// <summary>A heading-resolved document section.</summary>
public sealed class DocumentSectionResponse
{
    /// <summary>Document session containing the section.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Heading text as stored in the document.</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>Formatting style applied to the heading paragraph.</summary>
    public string? HeadingStyleName { get; set; }

    /// <summary>Zero-based paragraph index of the heading.</summary>
    public int HeadingParagraphIndex { get; set; }

    /// <summary>First zero-based paragraph index in the section body.</summary>
    public int BodyStartParagraphIndex { get; set; }

    /// <summary>Last zero-based paragraph index in the section body.</summary>
    public int BodyEndParagraphIndex { get; set; }

    /// <summary>Hash used to reject edits against stale section content.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Resolved body paragraphs with their document indexes and styles.</summary>
    public IReadOnlyList<IndexedParagraphResponse> Paragraphs { get; set; } = [];
}

/// <summary>Result of replacing one resolved section body.</summary>
public sealed class DocumentSectionEditResponse
{
    /// <summary>Document session containing the edited section.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Heading of the edited section.</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>Hash supplied by the caller for the previous content.</summary>
    public string PreviousContentHash { get; set; } = string.Empty;

    /// <summary>Hash of the section after replacement.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Zero-based paragraph index of the preserved heading.</summary>
    public int HeadingParagraphIndex { get; set; }

    /// <summary>First zero-based paragraph index in the resulting body.</summary>
    public int BodyStartParagraphIndex { get; set; }

    /// <summary>Last zero-based paragraph index in the resulting body.</summary>
    public int BodyEndParagraphIndex { get; set; }

    /// <summary>Number of section bodies replaced.</summary>
    public int EditsApplied { get; set; }
}
