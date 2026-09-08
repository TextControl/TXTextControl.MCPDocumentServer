using System;
using System.Drawing;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal static class DocumentOperationFormatter
{
    public static ParagraphStyle EnsureParagraphStyle(
        ServerTextControl textControl,
        TextStyleDefinition style,
        string? basedOn = null,
        string? followingStyle = null)
    {
        if (string.IsNullOrWhiteSpace(style.Name))
        {
            throw new ArgumentException("style.name is required.");
        }

        var name = style.Name.Trim();
        var paragraphStyle = textControl.ParagraphStyles
            .Cast<ParagraphStyle>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (paragraphStyle is null)
        {
            ParagraphStyle? baseStyle = string.IsNullOrWhiteSpace(basedOn)
                ? null
                : FindParagraphStyle(textControl, basedOn);
            if (!string.IsNullOrWhiteSpace(basedOn) && baseStyle is null)
            {
                throw new InvalidOperationException($"Base style '{basedOn}' was not found.");
            }

            paragraphStyle = baseStyle is null
                ? new ParagraphStyle(name)
                : new ParagraphStyle(name, baseStyle.Name);
            ApplyStyle(paragraphStyle, style);
            ApplyParagraphStyle(paragraphStyle.ParagraphFormat, style.Paragraph);
            if (!string.IsNullOrWhiteSpace(followingStyle))
            {
                paragraphStyle.FollowingStyle = ResolveParagraphStyleName(textControl, followingStyle);
            }
            textControl.ParagraphStyles.Add(paragraphStyle);
            return paragraphStyle;
        }

        ApplyStyle(paragraphStyle, style);
        ApplyParagraphStyle(paragraphStyle.ParagraphFormat, style.Paragraph);
        if (!string.IsNullOrWhiteSpace(followingStyle))
        {
            paragraphStyle.FollowingStyle = ResolveParagraphStyleName(textControl, followingStyle);
        }

        // TX returns an editable copy for an existing style. Apply commits the
        // changes to the document and propagates them to linked paragraphs.
        paragraphStyle.Apply();
        return paragraphStyle;
    }

    public static ParagraphStyle ReplaceParagraphStyle(ServerTextControl textControl, TextStyleDefinition style)
        => EnsureParagraphStyle(textControl, style);

    public static void ApplyStyle(Selection selection, TextStyleDefinition style)
    {
        if (!string.IsNullOrWhiteSpace(style.FontName))
        {
            selection.FontName = style.FontName;
        }

        if (style.FontSize.HasValue)
        {
            if (style.FontSize.Value <= 0)
            {
                throw new ArgumentException("fontSize must be greater than 0.");
            }

            selection.FontSize = (int)Math.Round(ToPoints(style.FontSize.Value, style.FontSizeUnit) * 20f);
        }

        if (style.Bold.HasValue)
        {
            selection.Bold = style.Bold.Value;
        }

        if (style.Italic.HasValue)
        {
            selection.Italic = style.Italic.Value;
        }

        if (style.Underline.HasValue)
        {
            selection.Underline = style.Underline.Value ? FontUnderlineStyle.Single : FontUnderlineStyle.None;
        }

        if (style.Strikeout.HasValue)
        {
            selection.Strikeout = style.Strikeout.Value;
        }

        if (!string.IsNullOrWhiteSpace(style.ColorHex))
        {
            selection.ForeColor = ParseHexColor(style.ColorHex);
        }

        if (!string.IsNullOrWhiteSpace(style.BackgroundColorHex))
        {
            selection.TextBackColor = ParseHexColor(style.BackgroundColorHex);
        }

        ApplyExtendedCharacterStyle(selection, style);
    }

    public static void ApplyCellStyle(
        ServerTextControl textControl,
        TXTextControl.TableCell cell,
        CellStyleDefinition style)
    {
        var cellFormat = cell.CellFormat;
        if (!string.IsNullOrWhiteSpace(style.BackgroundColorHex))
        {
            cellFormat.BackColor = ParseHexColor(style.BackgroundColorHex);
        }

        if (style.Border is not null)
        {
            ApplyBorderStyle(cellFormat, style.Border);
        }

        if (style.PaddingLeft.HasValue)
        {
            cellFormat.LeftTextDistance = ToCellTwips(style.PaddingLeft.Value, style.PaddingUnit);
        }

        if (style.PaddingRight.HasValue)
        {
            cellFormat.RightTextDistance = ToCellTwips(style.PaddingRight.Value, style.PaddingUnit);
        }

        if (style.PaddingTop.HasValue)
        {
            cellFormat.TopTextDistance = ToCellTwips(style.PaddingTop.Value, style.PaddingUnit);
        }

        if (style.PaddingBottom.HasValue)
        {
            cellFormat.BottomTextDistance = ToCellTwips(style.PaddingBottom.Value, style.PaddingUnit);
        }

        if (!string.IsNullOrWhiteSpace(style.VerticalAlignment))
        {
            cellFormat.VerticalAlignment = ResolveVerticalAlignment(style.VerticalAlignment);
        }

        cell.CellFormat = cellFormat;

        if (!string.IsNullOrWhiteSpace(style.HorizontalAlignment))
        {
            cell.Select();
            var selection = textControl.Selection;
            var paragraphFormat = selection.ParagraphFormat;
            paragraphFormat.Alignment = ResolveAlignment(style.HorizontalAlignment);
            selection.ParagraphFormat = paragraphFormat;
            textControl.Selection = selection;
        }
    }

    public static void ApplyParagraphStyle(TXTextControl.Paragraph paragraph, ParagraphStyleDefinition style)
    {
        var format = paragraph.Format;
        ApplyParagraphStyle(format, style);
        paragraph.Format = format;
    }

    public static void ApplyParagraphStyle(ParagraphFormat format, ParagraphStyleDefinition? style)
    {
        if (style is null)
        {
            return;
        }

        if (style.SpaceBefore.HasValue)
        {
            format.TopDistance = ToParagraphTwips(style.SpaceBefore.Value, style.Unit);
        }

        if (style.SpaceAfter.HasValue)
        {
            format.BottomDistance = ToParagraphTwips(style.SpaceAfter.Value, style.Unit);
        }

        if (!string.IsNullOrWhiteSpace(style.Alignment))
        {
            format.Alignment = ResolveAlignment(style.Alignment);
        }

        if (style.LineSpacing.HasValue)
        {
            if (style.LineSpacing.Value <= 0)
            {
                throw new ArgumentException("paragraph.lineSpacing must be greater than 0.");
            }

            float percentage = style.LineSpacing.Value <= 10
                ? style.LineSpacing.Value * 100
                : style.LineSpacing.Value;
            format.LineSpacing = (int)Math.Round(percentage);
        }

        if (style.AbsoluteLineSpacing.HasValue)
        {
            if (style.AbsoluteLineSpacing.Value <= 0)
            {
                throw new ArgumentException("paragraph.absoluteLineSpacing must be greater than 0.");
            }
            format.AbsoluteLineSpacing = ToParagraphTwips(style.AbsoluteLineSpacing.Value, style.Unit);
        }

        if (style.LeftIndent.HasValue)
        {
            format.LeftIndent = ToParagraphTwips(style.LeftIndent.Value, style.Unit);
        }
        if (style.RightIndent.HasValue)
        {
            format.RightIndent = ToParagraphTwips(style.RightIndent.Value, style.Unit);
        }
        if (style.HangingIndent.HasValue)
        {
            format.HangingIndent = ToSignedTwips(style.HangingIndent.Value, style.Unit);
        }
        if (!string.IsNullOrWhiteSpace(style.BackgroundColorHex))
        {
            format.BackColor = ParseHexColor(style.BackgroundColorHex);
        }
        if (style.KeepLinesTogether.HasValue)
        {
            format.KeepLinesTogether = style.KeepLinesTogether.Value;
        }
        if (style.KeepWithNext.HasValue)
        {
            format.KeepWithNext = style.KeepWithNext.Value;
        }
        if (style.PageBreakBefore.HasValue)
        {
            format.PageBreakBefore = style.PageBreakBefore.Value;
        }
        if (style.WidowOrphanLines.HasValue)
        {
            if (style.WidowOrphanLines.Value < 0)
            {
                throw new ArgumentException("paragraph.widowOrphanLines must be >= 0.");
            }
            format.WidowOrphanLines = style.WidowOrphanLines.Value;
        }
    }

    private static void ApplyBorderStyle(TableCellFormat cellFormat, CellBorderDefinition border)
    {
        ApplyBorderSide(
            border.Left,
            border.Width,
            border.ColorHex,
            width => cellFormat.LeftBorderWidth = width,
            color => cellFormat.LeftBorderColor = color);
        ApplyBorderSide(
            border.Top,
            border.Width,
            border.ColorHex,
            width => cellFormat.TopBorderWidth = width,
            color => cellFormat.TopBorderColor = color);
        ApplyBorderSide(
            border.Right,
            border.Width,
            border.ColorHex,
            width => cellFormat.RightBorderWidth = width,
            color => cellFormat.RightBorderColor = color);
        ApplyBorderSide(
            border.Bottom,
            border.Width,
            border.ColorHex,
            width => cellFormat.BottomBorderWidth = width,
            color => cellFormat.BottomBorderColor = color);
    }

    private static void ApplyBorderSide(
        CellBorderSideDefinition? side,
        int? defaultWidth,
        string? defaultColorHex,
        Action<int> setWidth,
        Action<Color> setColor)
    {
        var width = side?.Width ?? defaultWidth;
        if (width.HasValue)
        {
            if (width.Value < 0)
            {
                throw new ArgumentException("cellStyle.border width values must be >= 0.");
            }

            setWidth(width.Value);
        }

        var colorHex = side?.ColorHex ?? defaultColorHex;
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            setColor(ParseHexColor(colorHex));
        }
    }

    private static void ApplyStyle(FormattingStyle formattingStyle, TextStyleDefinition style)
    {
        if (!string.IsNullOrWhiteSpace(style.FontName))
        {
            formattingStyle.FontName = style.FontName;
        }

        if (style.FontSize.HasValue)
        {
            if (style.FontSize.Value <= 0)
            {
                throw new ArgumentException("fontSize must be greater than 0.");
            }

            formattingStyle.FontSize = (int)Math.Round(ToPoints(style.FontSize.Value, style.FontSizeUnit) * 20f);
        }

        if (style.Bold.HasValue)
        {
            formattingStyle.Bold = style.Bold.Value;
        }

        if (style.Italic.HasValue)
        {
            formattingStyle.Italic = style.Italic.Value;
        }

        if (style.Underline.HasValue)
        {
            formattingStyle.Underline = style.Underline.Value ? FontUnderlineStyle.Single : FontUnderlineStyle.None;
        }

        if (style.Strikeout.HasValue)
        {
            formattingStyle.Strikeout = style.Strikeout.Value;
        }

        if (!string.IsNullOrWhiteSpace(style.ColorHex))
        {
            formattingStyle.ForeColor = ParseHexColor(style.ColorHex);
        }

        if (!string.IsNullOrWhiteSpace(style.BackgroundColorHex))
        {
            formattingStyle.TextBackColor = ParseHexColor(style.BackgroundColorHex);
        }

        ApplyExtendedCharacterStyle(formattingStyle, style);
    }

    private static void ApplyExtendedCharacterStyle(FormattingStyle target, TextStyleDefinition style)
    {
        if (style.CharacterSpacing.HasValue)
        {
            target.CharacterSpacing = ToSignedTwips(style.CharacterSpacing.Value, style.FontSizeUnit);
        }
        if (style.CharacterScaling.HasValue)
        {
            if (style.CharacterScaling.Value <= 0)
            {
                throw new ArgumentException("characterScaling must be greater than 0.");
            }
            target.CharacterScaling = style.CharacterScaling.Value;
        }
        if (style.Baseline.HasValue)
        {
            target.AutoBaseline = AutoBaseline.None;
            target.Baseline = ToSignedTwips(style.Baseline.Value, style.FontSizeUnit);
        }
        if (!string.IsNullOrWhiteSpace(style.Capitals))
        {
            target.Capitals = ResolveCapitals(style.Capitals);
        }
    }

    private static void ApplyExtendedCharacterStyle(Selection target, TextStyleDefinition style)
    {
        if (style.CharacterSpacing.HasValue)
        {
            target.CharacterSpacing = ToSignedTwips(style.CharacterSpacing.Value, style.FontSizeUnit);
        }
        if (style.CharacterScaling.HasValue)
        {
            if (style.CharacterScaling.Value <= 0)
            {
                throw new ArgumentException("characterScaling must be greater than 0.");
            }
            target.CharacterScaling = style.CharacterScaling.Value;
        }
        if (style.Baseline.HasValue)
        {
            target.AutoBaseline = AutoBaseline.None;
            target.Baseline = ToSignedTwips(style.Baseline.Value, style.FontSizeUnit);
        }
        if (!string.IsNullOrWhiteSpace(style.Capitals))
        {
            target.Capitals = ResolveCapitals(style.Capitals);
        }
    }

    public static ParagraphStyle? FindParagraphStyle(ServerTextControl textControl, string styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName))
        {
            return null;
        }

        string requested = styleName.Trim();
        ParagraphStyle? exact = textControl.ParagraphStyles.GetItem(requested);
        if (exact is not null)
        {
            return exact;
        }

        string normalized = NormalizeStyleName(requested);
        var matches = textControl.ParagraphStyles
            .Cast<ParagraphStyle>()
            .Where(style => NormalizeStyleName(style.Name) == normalized)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public static string ResolveParagraphStyleName(ServerTextControl textControl, string styleName)
        => FindParagraphStyle(textControl, styleName)?.Name
           ?? throw new InvalidOperationException($"Style '{styleName}' was not found.");

    private static string NormalizeStyleName(string value)
        => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static float ToPoints(float value, string? unit)
    {
        var normalized = string.IsNullOrWhiteSpace(unit) ? "pt" : unit.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pt" or "point" or "points" => value,
            "px" or "pixel" or "pixels" => value * 72f / 96f,
            _ => throw new ArgumentException("fontSizeUnit must be 'pt' or 'px'.")
        };
    }

    private static Color ParseHexColor(string value)
    {
        var hex = value.Trim();
        if (!hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = "#" + hex;
        }

        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("colorHex must be a valid hex color, for example #FF0000.", ex);
        }
    }

    private static HorizontalAlignment ResolveAlignment(string alignment)
        => alignment.Trim().ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            "center" or "centered" => HorizontalAlignment.Center,
            "justify" or "justified" => HorizontalAlignment.Justify,
            _ => throw new ArgumentException("alignment must be 'left', 'right', 'center', or 'justify'.")
        };

    private static VerticalAlignment ResolveVerticalAlignment(string alignment)
        => alignment.Trim().ToLowerInvariant() switch
        {
            "top" => VerticalAlignment.Top,
            "center" or "centered" or "middle" => VerticalAlignment.Center,
            "bottom" => VerticalAlignment.Bottom,
            _ => throw new ArgumentException("verticalAlignment must be 'top', 'center', or 'bottom'.")
        };

    private static Capitals ResolveCapitals(string capitals)
        => capitals.Trim().ToLowerInvariant() switch
        {
            "none" or "normal" => Capitals.None,
            "capitals" or "allcaps" or "all-caps" => Capitals.Capitals,
            "smallcapitals" or "smallcaps" or "small-caps" => Capitals.SmallCapitals,
            "petitecapitals" or "petitecaps" or "petite-caps" => Capitals.PetiteCapitals,
            _ => throw new ArgumentException("capitals must be none, capitals, smallCapitals, or petiteCapitals.")
        };

    private static int ToCellTwips(float value, string? unit)
    {
        if (value < 0)
        {
            throw new ArgumentException("Cell padding values must be >= 0.");
        }

        var normalized = string.IsNullOrWhiteSpace(unit) ? "pt" : unit.Trim().ToLowerInvariant();
        var points = normalized switch
        {
            "pt" or "point" or "points" => value,
            "px" or "pixel" or "pixels" => value * 72f / 96f,
            "in" or "inch" or "inches" => value * 72f,
            "cm" or "centimeter" or "centimeters" => value * 72f / 2.54f,
            "mm" or "millimeter" or "millimeters" => value * 72f / 25.4f,
            "twip" or "twips" => value / 20f,
            _ => throw new ArgumentException("paddingUnit must be pt, px, in, cm, mm, or twips.")
        };

        return (int)Math.Round(points * 20f);
    }

    private static int ToParagraphTwips(float value, string? unit)
    {
        if (value < 0)
        {
            throw new ArgumentException("Paragraph spacing values must be >= 0.");
        }

        var normalized = string.IsNullOrWhiteSpace(unit) ? "pt" : unit.Trim().ToLowerInvariant();
        var points = normalized switch
        {
            "pt" or "point" or "points" => value,
            "px" or "pixel" or "pixels" => value * 72f / 96f,
            _ => throw new ArgumentException("paragraph.unit must be 'pt' or 'px'.")
        };

        return (int)Math.Round(points * 20f);
    }

    private static int ToSignedTwips(float value, string? unit)
    {
        var normalized = string.IsNullOrWhiteSpace(unit) ? "pt" : unit.Trim().ToLowerInvariant();
        float points = normalized switch
        {
            "pt" or "point" or "points" => value,
            "px" or "pixel" or "pixels" => value * 72f / 96f,
            _ => throw new ArgumentException("The unit must be 'pt' or 'px'.")
        };
        return (int)Math.Round(points * 20f);
    }
}
