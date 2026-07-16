using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class DefineStyleOperationHandler : IDocumentOperationHandler
{
    public string Type => "define_style";
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.DefineStyle,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Defines or replaces a reusable paragraph formatting style.",
        Intent = "Use only when the user explicitly asks to create, define, or change a style. Do not define styles for prompts without style instructions; configured defaults apply automatically.",
        RequiredProperties = ["type", "style"],
        OptionalProperties = [],
        Properties = new()
        {
            ["style.name"] = "Unique style name.",
            ["style.fontName"] = "Optional font family, for example Arial. Send only when explicitly requested.",
            ["style.fontSize"] = "Optional font size. Send only when explicitly requested.",
            ["style.fontSizeUnit"] = "Optional size unit: pt or px. Defaults to pt.",
            ["style.bold"] = "Optional bold flag. Send only when explicitly requested.",
            ["style.italic"] = "Optional italic flag. Send only when explicitly requested.",
            ["style.underline"] = "Optional underline flag. Send only when explicitly requested.",
            ["style.colorHex"] = "Optional text color such as #1f2937. Send only when explicitly requested."
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
        ModelEffects = ["Adds or updates document.styles entry."],
        RequiresTxExecution = false
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

        var name = operation.Style.Name.Trim();
        operation.Style.Name = name;
        context.Styles[name] = operation.Style;

        if (context.TryGetTextControl(out var tx))
        {
            DocumentOperationFormatter.ReplaceParagraphStyle(tx, operation.Style);
        }

        var existing = context.Document.Styles.FirstOrDefault(style =>
            string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            context.Document.Styles.Add(new Style
            {
                Name = name,
                Type = "paragraph",
                Text = operation.Style
            });
        }
        else
        {
            existing.Text = operation.Style;
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Defined style '{name}'.",
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
