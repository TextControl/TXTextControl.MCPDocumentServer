using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class DeleteStyleOperationHandler : IDocumentOperationHandler
{
    public string Type => BasicTextCapabilityPack.DeleteStyle;
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.DeleteStyle,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Deletes a paragraph style. An in-use style requires replacementStyleName.",
        Intent = "Use only when the user explicitly asks to remove a named style.",
        RequiredProperties = ["type", "styleName"],
        OptionalProperties = ["replacementStyleName"],
        ModelEffects = ["Reassigns linked paragraphs when requested, then removes the style."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.StyleName);
        var tx = context.TextControl;
        ParagraphStyle style = DocumentOperationFormatter.FindParagraphStyle(tx, operation.StyleName)
            ?? throw new InvalidOperationException($"Style '{operation.StyleName}' was not found.");
        if (style.Name is "[Normal]")
        {
            throw new InvalidOperationException("The built-in [Normal] style cannot be deleted.");
        }

        string oldName = style.Name;
        var linked = tx.Paragraphs.Cast<TXTextControl.Paragraph>()
            .Where(paragraph => string.Equals(paragraph.FormattingStyle, oldName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        string? replacementName = null;
        if (linked.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(operation.ReplacementStyleName))
            {
                throw new InvalidOperationException(
                    $"Style '{oldName}' is used by {linked.Count} paragraphs. Provide replacementStyleName before deleting it.");
            }
            ParagraphStyle? replacementStyle = DocumentOperationFormatter.FindParagraphStyle(tx, operation.ReplacementStyleName);
            if (replacementStyle is null)
            {
                var configuredReplacement = context.GetStyle(operation.ReplacementStyleName);
                configuredReplacement.Name = operation.ReplacementStyleName.Trim();
                replacementStyle = DocumentOperationFormatter.EnsureParagraphStyle(tx, configuredReplacement);
            }
            replacementName = replacementStyle.Name;
            if (string.Equals(replacementName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("replacementStyleName must name a different style.");
            }
            foreach (var paragraph in linked)
            {
                paragraph.FormattingStyle = replacementName;
            }
        }

        tx.ParagraphStyles.Remove(oldName);
        context.Styles.Remove(oldName);
        context.Document.Styles.RemoveAll(candidate => string.Equals(candidate.Name, oldName, StringComparison.OrdinalIgnoreCase));
        foreach (var paragraph in ParagraphTargetUtilities.EnumerateModelParagraphs(context.Document))
        {
            if (string.Equals(paragraph.StyleName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                paragraph.StyleName = replacementName;
            }
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = linked.Count == 0
                ? $"Deleted unused style '{oldName}'."
                : $"Reassigned {linked.Count} paragraphs to '{replacementName}' and deleted style '{oldName}'.",
            TargetType = "style",
            TargetId = oldName,
            Metadata = new Dictionary<string, object?>
            {
                ["styleName"] = oldName,
                ["replacementStyleName"] = replacementName,
                ["reassignedParagraphCount"] = linked.Count
            }
        };
    }
}
