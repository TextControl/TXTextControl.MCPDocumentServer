using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class ApplyStyleToParagraphOperationHandler : IDocumentOperationHandler
{
    public string Type => "apply_style_to_paragraph";
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.ApplyStyleToParagraph,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Applies an existing paragraph style to paragraphs resolved by index, matching text, or an explicit all-paragraphs target.",
        Intent = "Use for follow-up style corrections on existing paragraphs.",
        RequiredProperties = ["type", "styleName"],
        OptionalProperties = ["paragraphIndex", "startParagraphIndex", "endParagraphIndex", "matchText", "occurrenceIndex", "nearTextPosition", "replaceAll", "allParagraphs"],
        Properties = new()
        {
            ["styleName"] = "Existing style name.",
            ["paragraphIndex"] = "Zero-based paragraph index in document order.",
            ["startParagraphIndex"] = "First zero-based paragraph in an inclusive paragraph range.",
            ["endParagraphIndex"] = "Last zero-based paragraph in an inclusive paragraph range.",
            ["matchText"] = "Text inside the target paragraph.",
            ["nearTextPosition"] = "Non-authoritative browser-position hint used to choose the closest match.",
            ["allParagraphs"] = "Apply the style to every paragraph."
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
        if (string.IsNullOrWhiteSpace(operation.StyleName))
        {
            throw new ArgumentException("styleName is required for apply_style_to_paragraph.");
        }

        var tx = context.TextControl;
        var targetIndexes = ParagraphTargetUtilities.ResolveTargetIndexes(tx, operation, allowImplicitAll: false);

        ParagraphStyle? nativeStyle = DocumentOperationFormatter.FindParagraphStyle(tx, operation.StyleName);
        TextStyleDefinition style;
        if (nativeStyle is null)
        {
            style = context.GetStyle(operation.StyleName);
            style.Name = operation.StyleName.Trim();
            nativeStyle = DocumentOperationFormatter.EnsureParagraphStyle(tx, style);
        }
        else
        {
            style = DocumentStyleUtilities.ToDefinition(nativeStyle);
        }
        string appliedStyleName = nativeStyle.Name;
        foreach (int paragraphIndex in targetIndexes)
        {
            TXTextControl.Paragraph paragraph = tx.Paragraphs[paragraphIndex + 1];
            paragraph.FormattingStyle = appliedStyleName;
        }

        var paragraphBlocks = ParagraphTargetUtilities
            .EnumerateModelParagraphs(context.Document)
            .ToList();
        foreach (int paragraphIndex in targetIndexes)
        {
            var paragraphBlock = paragraphBlocks.ElementAtOrDefault(paragraphIndex);
            if (paragraphBlock is null)
            {
                continue;
            }

            paragraphBlock.StyleName = appliedStyleName;
            foreach (var run in paragraphBlock.Runs)
            {
                run.StyleName = appliedStyleName;
            }

            paragraphBlock.ParagraphStyle = style.Paragraph;
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = targetIndexes.Count == 1
                ? $"Applied style '{appliedStyleName}' to paragraph {targetIndexes[0]}."
                : $"Applied style '{appliedStyleName}' to {targetIndexes.Count} paragraphs.",
            TargetType = targetIndexes.Count == 1 ? "paragraph" : "paragraphs",
            Location = targetIndexes.Count == 1 ? $"paragraphs[{targetIndexes[0]}]" : "paragraphs[*]",
            Metadata = new Dictionary<string, object?>
            {
                ["paragraphIndex"] = operation.ParagraphIndex,
                ["paragraphIndexes"] = targetIndexes,
                ["matchText"] = operation.MatchText,
                ["styleName"] = appliedStyleName
            }
        };
    }
}
