using System;
using System.Collections.Generic;
using System.Globalization;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class SetSectionLayoutOperationHandler : IDocumentOperationHandler
{
    public string Type => SectionCapabilityPack.SetSectionLayout;
    public string CapabilityPack => SectionCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = SectionCapabilityPack.SetSectionLayout,
        CapabilityPack = SectionCapabilityPack.PackName,
        Description = "Sets page size, orientation, and margins for a TX Text Control section.",
        Intent = "Use before or after authoring content when the document needs a specific paper format such as A4, Letter, landscape, or custom margins.",
        RequiredProperties = ["type"],
        OptionalProperties = ["sectionIndex", "pageSize", "orientation", "pageWidth", "pageHeight", "unit", "marginLeft", "marginRight", "marginTop", "marginBottom", "pageLayout"],
        Properties = new()
        {
            ["sectionIndex"] = "Zero-based section index. Defaults to 0; current document model supports section 0.",
            ["pageSize"] = "Named page size: A4, Letter, Legal, A5, A3, Executive. Ignored when pageWidth/pageHeight are supplied.",
            ["orientation"] = "portrait or landscape. Defaults to portrait.",
            ["pageWidth/pageHeight"] = "Custom page size in the supplied unit.",
            ["unit"] = "Unit for custom page size and margins: pt, in, cm, mm, twip/twips. Defaults to pt.",
            ["marginLeft/marginRight/marginTop/marginBottom"] = "Optional margins in the supplied unit.",
            ["pageLayout"] = "Optional nested PageLayoutDefinition carrying the same page size/margin properties."
        },
        Example = new()
        {
            ["type"] = SectionCapabilityPack.SetSectionLayout,
            ["pageSize"] = "A4",
            ["orientation"] = "portrait",
            ["unit"] = "cm",
            ["marginLeft"] = 2.0,
            ["marginRight"] = 2.0,
            ["marginTop"] = 2.5,
            ["marginBottom"] = 2.5
        },
        ModelEffects = ["Updates document.sections[sectionIndex].pageLayout."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var sectionIndex = operation.SectionIndex ?? 0;
        if (sectionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operation.SectionIndex), "sectionIndex must be >= 0.");
        }

        var layout = ResolveLayout(operation);
        var size = ResolvePageSize(layout);
        var effectiveSize = size;
        if (IsLandscape(layout.Orientation))
        {
            effectiveSize = (WidthTwips: Math.Max(size.WidthTwips, size.HeightTwips), HeightTwips: Math.Min(size.WidthTwips, size.HeightTwips));
        }
        else
        {
            effectiveSize = (WidthTwips: Math.Min(size.WidthTwips, size.HeightTwips), HeightTwips: Math.Max(size.WidthTwips, size.HeightTwips));
        }

        var margins = ResolveMargins(layout);

        if (context.TryGetTextControl(out var tx))
        {
            if (sectionIndex >= tx.Sections.Count)
            {
                throw new InvalidOperationException($"TX Text Control section {sectionIndex} was not found. Insert a section break before formatting this section.");
            }

            var section = tx.Sections[sectionIndex + 1]
                ?? throw new InvalidOperationException($"TX Text Control section {sectionIndex} was not found.");
            var format = section.Format;

            format.Landscape = IsLandscape(layout.Orientation);
            format.PageSize = new PageSize(ToTxSectionUnit(size.WidthTwips), ToTxSectionUnit(size.HeightTwips));
            if (margins.HasValue)
            {
                format.PageMargins = new PageMargins(
                    ToTxSectionUnit(margins.Value.LeftTwips),
                    ToTxSectionUnit(margins.Value.TopTwips),
                    ToTxSectionUnit(margins.Value.RightTwips),
                    ToTxSectionUnit(margins.Value.BottomTwips));
            }

            section.Format = format;
        }

        var modelSection = context.GetSection(sectionIndex);
        modelSection.PageLayout = ToModelLayout(layout, effectiveSize, margins);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Set section {sectionIndex} page layout to {effectiveSize.WidthTwips}x{effectiveSize.HeightTwips} twips.",
            TargetType = "section",
            TargetId = sectionIndex.ToString(CultureInfo.InvariantCulture),
            Location = $"sections[{sectionIndex}].pageLayout",
            Metadata = new Dictionary<string, object?>
            {
                ["sectionIndex"] = sectionIndex,
                ["pageSize"] = modelSection.PageLayout.PageSize,
                ["orientation"] = modelSection.PageLayout.Orientation,
                ["widthTwips"] = effectiveSize.WidthTwips,
                ["heightTwips"] = effectiveSize.HeightTwips,
                ["marginLeftTwips"] = margins?.LeftTwips,
                ["marginRightTwips"] = margins?.RightTwips,
                ["marginTopTwips"] = margins?.TopTwips,
                ["marginBottomTwips"] = margins?.BottomTwips
            }
        };
    }

    private static PageLayoutDefinition ResolveLayout(DocumentOperation operation)
    {
        var nested = operation.PageLayout;
        return new PageLayoutDefinition
        {
            PageSize = operation.PageSize ?? nested?.PageSize,
            Orientation = operation.Orientation ?? nested?.Orientation,
            PageWidth = operation.PageWidth ?? nested?.PageWidth,
            PageHeight = operation.PageHeight ?? nested?.PageHeight,
            Unit = operation.Unit ?? nested?.Unit,
            MarginLeft = operation.MarginLeft ?? nested?.MarginLeft,
            MarginRight = operation.MarginRight ?? nested?.MarginRight,
            MarginTop = operation.MarginTop ?? nested?.MarginTop,
            MarginBottom = operation.MarginBottom ?? nested?.MarginBottom
        };
    }

    private static (int WidthTwips, int HeightTwips) ResolvePageSize(PageLayoutDefinition layout)
    {
        var unit = NormalizeUnit(layout.Unit);
        if (layout.PageWidth.HasValue || layout.PageHeight.HasValue)
        {
            if (!layout.PageWidth.HasValue || !layout.PageHeight.HasValue)
            {
                throw new ArgumentException("Both pageWidth and pageHeight are required for custom page sizes.");
            }

            return (ToTwips(layout.PageWidth.Value, unit, nameof(layout.PageWidth)), ToTwips(layout.PageHeight.Value, unit, nameof(layout.PageHeight)));
        }

        return ResolveNamedPageSize(layout.PageSize);
    }

    private static (int WidthTwips, int HeightTwips) ResolveNamedPageSize(string? pageSize)
    {
        var normalized = string.IsNullOrWhiteSpace(pageSize) ? "letter" : pageSize.Trim().ToLowerInvariant();
        return normalized switch
        {
            "a3" => (ToTwips(297, "mm", "A3 width"), ToTwips(420, "mm", "A3 height")),
            "a4" => (ToTwips(210, "mm", "A4 width"), ToTwips(297, "mm", "A4 height")),
            "a5" => (ToTwips(148, "mm", "A5 width"), ToTwips(210, "mm", "A5 height")),
            "letter" or "usletter" or "us_letter" => (ToTwips(8.5f, "in", "Letter width"), ToTwips(11f, "in", "Letter height")),
            "legal" or "uslegal" or "us_legal" => (ToTwips(8.5f, "in", "Legal width"), ToTwips(14f, "in", "Legal height")),
            "executive" => (ToTwips(7.25f, "in", "Executive width"), ToTwips(10.5f, "in", "Executive height")),
            _ => throw new ArgumentException("pageSize must be one of: A3, A4, A5, Letter, Legal, Executive, or specify pageWidth/pageHeight.")
        };
    }

    private static (int LeftTwips, int TopTwips, int RightTwips, int BottomTwips)? ResolveMargins(PageLayoutDefinition layout)
    {
        if (!layout.MarginLeft.HasValue
            && !layout.MarginRight.HasValue
            && !layout.MarginTop.HasValue
            && !layout.MarginBottom.HasValue)
        {
            return null;
        }

        var unit = NormalizeUnit(layout.Unit);
        return (
            ToTwips(layout.MarginLeft ?? 72f, unit, nameof(layout.MarginLeft)),
            ToTwips(layout.MarginTop ?? 72f, unit, nameof(layout.MarginTop)),
            ToTwips(layout.MarginRight ?? 72f, unit, nameof(layout.MarginRight)),
            ToTwips(layout.MarginBottom ?? 72f, unit, nameof(layout.MarginBottom)));
    }

    private static PageLayoutDefinition ToModelLayout(
        PageLayoutDefinition layout,
        (int WidthTwips, int HeightTwips) size,
        (int LeftTwips, int TopTwips, int RightTwips, int BottomTwips)? margins)
    {
        var orientation = IsLandscape(layout.Orientation) ? "landscape" : "portrait";
        return new PageLayoutDefinition
        {
            PageSize = string.IsNullOrWhiteSpace(layout.PageSize) ? "custom" : layout.PageSize.Trim(),
            Orientation = orientation,
            PageWidth = TwipsToPoints(size.WidthTwips),
            PageHeight = TwipsToPoints(size.HeightTwips),
            Unit = "pt",
            MarginLeft = margins.HasValue ? TwipsToPoints(margins.Value.LeftTwips) : null,
            MarginRight = margins.HasValue ? TwipsToPoints(margins.Value.RightTwips) : null,
            MarginTop = margins.HasValue ? TwipsToPoints(margins.Value.TopTwips) : null,
            MarginBottom = margins.HasValue ? TwipsToPoints(margins.Value.BottomTwips) : null
        };
    }

    private static bool IsLandscape(string? orientation)
    {
        if (string.IsNullOrWhiteSpace(orientation))
        {
            return false;
        }

        return orientation.Trim().ToLowerInvariant() switch
        {
            "portrait" => false,
            "landscape" => true,
            _ => throw new ArgumentException("orientation must be 'portrait' or 'landscape'.")
        };
    }

    private static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return "pt";
        }

        return unit.Trim().ToLowerInvariant() switch
        {
            "pt" or "point" or "points" => "pt",
            "in" or "inch" or "inches" => "in",
            "cm" or "centimeter" or "centimeters" => "cm",
            "mm" or "millimeter" or "millimeters" => "mm",
            "twip" or "twips" => "twips",
            _ => throw new ArgumentException("unit must be one of: pt, in, cm, mm, twips.")
        };
    }

    private static int ToTwips(float value, string unit, string name)
    {
        if (value < 0)
        {
            throw new ArgumentException($"{name} must be >= 0.");
        }

        var points = unit switch
        {
            "pt" => value,
            "in" => value * 72f,
            "cm" => value * 72f / 2.54f,
            "mm" => value * 72f / 25.4f,
            "twips" => value / 20f,
            _ => value
        };

        return (int)Math.Round(points * 20f);
    }

    private static float TwipsToPoints(int twips)
        => (float)Math.Round(twips / 20f, 2);

    private static int ToTxSectionUnit(int twips)
        => (int)Math.Round(twips / 14.4f);
}
