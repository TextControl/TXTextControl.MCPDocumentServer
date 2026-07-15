using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class BasicTextCapabilityPack : ICapabilityPack
{
    public const string PackName = "BasicText";
    public const string DefineStyle = "define_style";
    public const string AppendParagraph = "append_paragraph";
    public const string ApplyStyleToParagraph = "apply_style_to_paragraph";
    public const string FormatParagraphs = "format_paragraphs";
    public const string FormatTextOccurrences = "format_text_occurrences";
    public const string ReplaceText = "replace_text";

    public string Name => PackName;

    public string Description =>
        "Defines reusable text styles and applies paragraph-level text authoring operations.";

    public IReadOnlyCollection<string> OperationTypes { get; } =
    [
        DefineStyle,
        AppendParagraph,
        ApplyStyleToParagraph,
        FormatParagraphs,
        FormatTextOccurrences,
        ReplaceText
    ];
}
