using System;
using System.Drawing;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal static class DocumentStyleUtilities
{
    public static StyleInspection Inspect(ParagraphStyle style, int usageCount)
    {
        return new StyleInspection
        {
            Name = style.Name,
            Type = "paragraph",
            BasedOn = style.BaseStyle?.Name,
            FollowingStyle = string.IsNullOrWhiteSpace(style.FollowingStyle) ? null : style.FollowingStyle,
            UsageCount = usageCount,
            IsBuiltIn = style.Name.StartsWith("[", StringComparison.Ordinal) && style.Name.EndsWith("]", StringComparison.Ordinal),
            Text = ToDefinition(style),
            Paragraph = ToParagraphDefinition(style.ParagraphFormat)
        };
    }

    public static TextStyleDefinition ToDefinition(ParagraphStyle style) => new()
    {
        Name = style.Name,
        FontName = style.FontName,
        FontSize = style.FontSize / 20f,
        FontSizeUnit = "pt",
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline != FontUnderlineStyle.None,
        Strikeout = style.Strikeout,
        ColorHex = ToHex(style.ForeColor),
        BackgroundColorHex = ToHex(style.TextBackColor),
        CharacterSpacing = style.CharacterSpacing / 20f,
        CharacterScaling = style.CharacterScaling,
        Baseline = style.AutoBaseline == AutoBaseline.None ? style.Baseline / 20f : null,
        Capitals = style.Capitals switch
        {
            Capitals.Capitals => "capitals",
            Capitals.SmallCapitals => "smallCapitals",
            Capitals.PetiteCapitals => "petiteCapitals",
            _ => "none"
        },
        Paragraph = ToParagraphDefinition(style.ParagraphFormat)
    };

    public static ParagraphStyleDefinition ToParagraphDefinition(ParagraphFormat format) => new()
    {
        Alignment = format.Alignment.ToString().ToLowerInvariant(),
        SpaceBefore = format.TopDistance / 20f,
        SpaceAfter = format.BottomDistance / 20f,
        LineSpacing = format.LineSpacing / 100f,
        AbsoluteLineSpacing = format.AbsoluteLineSpacing > 0 ? format.AbsoluteLineSpacing / 20f : null,
        LeftIndent = format.LeftIndent / 20f,
        RightIndent = format.RightIndent / 20f,
        HangingIndent = format.HangingIndent / 20f,
        BackgroundColorHex = ToHex(format.BackColor),
        KeepLinesTogether = format.KeepLinesTogether,
        KeepWithNext = format.KeepWithNext,
        PageBreakBefore = format.PageBreakBefore,
        WidowOrphanLines = format.WidowOrphanLines,
        Unit = "pt"
    };

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string NormalizeName(string value)
        => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
