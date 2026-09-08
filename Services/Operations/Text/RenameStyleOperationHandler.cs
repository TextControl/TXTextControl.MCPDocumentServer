using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class RenameStyleOperationHandler : IDocumentOperationHandler
{
    public string Type => BasicTextCapabilityPack.RenameStyle;
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.RenameStyle,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Renames a native paragraph style while preserving every paragraph linked to it.",
        Intent = "Use only when the user explicitly asks to rename a named style.",
        RequiredProperties = ["type", "styleName", "newStyleName"],
        ModelEffects = ["Renames the style and updates linked paragraph style references."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.StyleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation.NewStyleName);
        var tx = context.TextControl;
        ParagraphStyle style = DocumentOperationFormatter.FindParagraphStyle(tx, operation.StyleName)
            ?? throw new InvalidOperationException($"Style '{operation.StyleName}' was not found.");
        if (style.Name is "[Normal]")
        {
            throw new InvalidOperationException("The built-in [Normal] style cannot be renamed.");
        }
        if (DocumentOperationFormatter.FindParagraphStyle(tx, operation.NewStyleName) is not null)
        {
            throw new InvalidOperationException($"Style '{operation.NewStyleName}' already exists.");
        }

        string oldName = style.Name;
        string newName = operation.NewStyleName.Trim();
        int[] linkedParagraphs = tx.Paragraphs.Cast<TXTextControl.Paragraph>()
            .Select((paragraph, paragraphIndex) => (paragraph, paragraphIndex))
            .Where(item => string.Equals(item.paragraph.FormattingStyle, oldName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.paragraphIndex)
            .ToArray();

        style.Name = newName;
        style.Apply();
        foreach (int paragraphIndex in linkedParagraphs)
        {
            tx.Paragraphs[paragraphIndex + 1].FormattingStyle = newName;
        }

        context.Styles.Remove(oldName);
        context.Styles[newName] = DocumentStyleUtilities.ToDefinition(style);
        foreach (var modelStyle in context.Document.Styles.Where(candidate =>
                     string.Equals(candidate.Name, oldName, StringComparison.OrdinalIgnoreCase)))
        {
            modelStyle.Name = newName;
            if (modelStyle.Text is not null)
            {
                modelStyle.Text.Name = newName;
            }
        }
        foreach (var paragraph in ParagraphTargetUtilities.EnumerateModelParagraphs(context.Document))
        {
            if (string.Equals(paragraph.StyleName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                paragraph.StyleName = newName;
            }
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Renamed style '{oldName}' to '{newName}' and retained {linkedParagraphs.Length} linked paragraphs.",
            TargetType = "style",
            TargetId = newName,
            Metadata = new Dictionary<string, object?>
            {
                ["oldStyleName"] = oldName,
                ["styleName"] = newName,
                ["linkedParagraphCount"] = linkedParagraphs.Length
            }
        };
    }
}
