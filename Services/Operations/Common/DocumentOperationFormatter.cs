using System;
using System.Drawing;
using TxTextControl.McpServer.Models.DocumentModel;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal static class DocumentOperationFormatter
{
    public static void EnsureParagraphStyle(ServerTextControl textControl, TextStyleDefinition style)
    {
        if (string.IsNullOrWhiteSpace(style.Name))
        {
            throw new ArgumentException("style.name is required.");
        }

        var name = style.Name.Trim();
        var paragraphStyle = textControl.ParagraphStyles.GetItem(name);
        if (paragraphStyle is null)
        {
            paragraphStyle = new ParagraphStyle(name);
            ApplyStyle(paragraphStyle, style);
            textControl.ParagraphStyles.Add(paragraphStyle);
            return;
        }

        ApplyStyle(paragraphStyle, style);
    }

    public static void ReplaceParagraphStyle(ServerTextControl textControl, TextStyleDefinition style)
    {
        if (string.IsNullOrWhiteSpace(style.Name))
        {
            throw new ArgumentException("style.name is required.");
        }

        var name = style.Name.Trim();
        if (textControl.ParagraphStyles.GetItem(name) is not null)
        {
            textControl.ParagraphStyles.Remove(name);
        }

        var paragraphStyle = new ParagraphStyle(name);
        ApplyStyle(paragraphStyle, style);
        textControl.ParagraphStyles.Add(paragraphStyle);
    }

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

        if (!string.IsNullOrWhiteSpace(style.ColorHex))
        {
            selection.ForeColor = ParseHexColor(style.ColorHex);
        }
    }

    public static void ApplyCellStyle(TXTextControl.TableCell cell, CellStyleDefinition style)
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

        cell.CellFormat = cellFormat;
    }

    public static void ApplyParagraphStyle(TXTextControl.Paragraph paragraph, ParagraphStyleDefinition style)
    {
        var format = paragraph.Format;
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

        paragraph.Format = format;
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

        if (!string.IsNullOrWhiteSpace(style.ColorHex))
        {
            formattingStyle.ForeColor = ParseHexColor(style.ColorHex);
        }
    }

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
            _ => throw new ArgumentException("paragraph.alignment must be 'left' or 'right'.")
        };

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
}
