using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class DefineStyleOperationHandler : IDocumentOperationHandler
{
    public string Type => "define_style";
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.DefineStyle,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Creates a paragraph style or updates an existing named style in place so paragraphs linked to it immediately inherit the change.",
        Intent = "Use when the user explicitly asks to create, define, or change a named style, for example 'change Heading 1 to red'. Unspecified style properties are preserved. Do not apply direct formatting to every paragraph using the style.",
        RequiredProperties = ["type", "style"],
        OptionalProperties = ["basedOn", "followingStyle"],
        Properties = new()
        {
            ["style.name"] = "Unique style name.",
            ["style.fontName"] = "Optional font family, for example Arial. Send only when explicitly requested.",
            ["style.fontSize"] = "Optional font size. Send only when explicitly requested.",
            ["style.fontSizeUnit"] = "Optional size unit: pt or px. Defaults to pt.",
            ["style.bold"] = "Optional bold flag. Send only when explicitly requested.",
            ["style.italic"] = "Optional italic flag. Send only when explicitly requested.",
            ["style.underline"] = "Optional underline flag. Send only when explicitly requested.",
            ["style.colorHex"] = "Optional text color such as #1f2937. Send only when explicitly requested.",
            ["style.paragraph"] = "Optional paragraph-level properties including alignment, spacing, indents, pagination, and background.",
            ["basedOn"] = "Optional base style for a newly created style.",
            ["followingStyle"] = "Optional style used by a following paragraph."
        },
        Example = new()
        {
            ["type"] = BasicTextCapabilityPack.DefineStyle,
            ["style"] = new Dictionary<string, object?>
            {
                ["name"] = "Heading",
                ["fontName"] = "Arial",
                ["fontSize"] = 20,
                ["fontSizeUnit"] = "pt",
                ["bold"] = true
            }
        },
        ModelEffects = ["Adds or updates the native TX paragraph style and document.styles entry; linked paragraphs inherit the update."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (operation.Style is null)
        {
            throw new ArgumentException("style is required for define_style.");
        }

        if (string.IsNullOrWhiteSpace(operation.Style.Name))
        {
            throw new ArgumentException("style.name is required for define_style.");
        }

        var requestedName = operation.Style.Name.Trim();
        operation.Style.Name = requestedName;
        var name = requestedName;
        TextStyleDefinition storedStyle = operation.Style;

        if (context.TryGetTextControl(out var tx))
        {
            ParagraphStyle? existingStyle = DocumentOperationFormatter.FindParagraphStyle(tx, requestedName);
            if (existingStyle is not null)
            {
                operation.Style.Name = existingStyle.Name;
            }
            if (existingStyle is not null && !string.IsNullOrWhiteSpace(operation.BasedOn))
            {
                string currentBase = existingStyle.BaseStyle?.Name ?? string.Empty;
                string requestedBase = DocumentOperationFormatter.ResolveParagraphStyleName(tx, operation.BasedOn);
                if (!string.Equals(currentBase, requestedBase, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Style '{existingStyle.Name}' already exists and its base style cannot be changed in place. " +
                        "Create a new style with the requested base style instead.");
                }
            }

            ParagraphStyle nativeStyle = DocumentOperationFormatter.EnsureParagraphStyle(
                tx,
                operation.Style,
                operation.BasedOn,
                operation.FollowingStyle);
            name = nativeStyle.Name;
            storedStyle = DocumentStyleUtilities.ToDefinition(nativeStyle);
        }

        storedStyle.Name = name;
        context.Styles[name] = storedStyle;

        var existing = context.Document.Styles.FirstOrDefault(style =>
            string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            context.Document.Styles.Add(new Style
            {
                Name = name,
                Type = "paragraph",
                BasedOn = operation.BasedOn,
                Text = storedStyle,
                Paragraph = storedStyle.Paragraph
            });
        }
        else
        {
            existing.BasedOn = operation.BasedOn ?? existing.BasedOn;
            existing.Text = storedStyle;
            existing.Paragraph = storedStyle.Paragraph;
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Created or updated style '{name}'; paragraphs linked to it inherit the change.",
            TargetType = "style",
            TargetId = name,
            Location = $"styles['{name}']",
            Metadata = new Dictionary<string, object?>
            {
                ["styleName"] = name,
                ["styleType"] = "paragraph"
            }
        };
    }
}
