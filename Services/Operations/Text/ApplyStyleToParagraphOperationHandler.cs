using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class ApplyStyleToParagraphOperationHandler : IDocumentOperationHandler
{
    public string Type => "apply_style_to_paragraph";
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.ApplyStyleToParagraph,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Applies an existing paragraph style to a paragraph by zero-based paragraph index.",
        Intent = "Use for follow-up style corrections on existing paragraphs.",
        RequiredProperties = ["type", "styleName", "paragraphIndex"],
        OptionalProperties = [],
        Properties = new()
        {
            ["styleName"] = "Existing style name.",
            ["paragraphIndex"] = "Zero-based paragraph index in document order."
        },
        Example = new()
        {
            ["type"] = BasicTextCapabilityPack.ApplyStyleToParagraph,
            ["styleName"] = "Heading",
            ["paragraphIndex"] = 0
        },
        ModelEffects = ["Updates paragraph.styleName for the selected paragraph when present in the neutral model."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (!operation.ParagraphIndex.HasValue)
        {
            throw new ArgumentException("paragraphIndex is required for apply_style_to_paragraph.");
        }

        if (string.IsNullOrWhiteSpace(operation.StyleName))
        {
            throw new ArgumentException("styleName is required for apply_style_to_paragraph.");
        }

        var paragraphIndex = operation.ParagraphIndex.Value;
        if (paragraphIndex < 0)
        {
            throw new ArgumentException("paragraphIndex must be greater than or equal to 0.");
        }

        var collectionIndex = paragraphIndex + 1;
        var tx = context.TextControl;
        if (collectionIndex < 1 || collectionIndex > tx.Paragraphs.Count)
        {
            throw new ArgumentException("paragraphIndex is out of range.");
        }

        var style = context.GetStyle(operation.StyleName);
        style.Name = operation.StyleName.Trim();
        DocumentOperationFormatter.EnsureParagraphStyle(tx, style);
        tx.Paragraphs[collectionIndex].FormattingStyle = style.Name;
        if (style.Paragraph is not null)
        {
            DocumentOperationFormatter.ApplyParagraphStyle(tx.Paragraphs[collectionIndex], style.Paragraph);
        }

        var paragraphBlock = context.Document.Sections
            .SelectMany(section => section.Blocks)
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .Where(paragraph => paragraph is not null)
            .ElementAtOrDefault(paragraphIndex);

        if (paragraphBlock is not null)
        {
            paragraphBlock.StyleName = operation.StyleName.Trim();
            foreach (var run in paragraphBlock.Runs)
            {
                run.StyleName = operation.StyleName.Trim();
            }

            paragraphBlock.ParagraphStyle = style.Paragraph;
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Applied style '{operation.StyleName}' to paragraph {paragraphIndex}.",
            TargetType = "paragraph",
            TargetId = paragraphBlock?.Id,
            Location = $"paragraphs[{paragraphIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["paragraphIndex"] = paragraphIndex,
                ["paragraphId"] = paragraphBlock?.Id,
                ["styleName"] = operation.StyleName.Trim()
            }
        };
    }
}
