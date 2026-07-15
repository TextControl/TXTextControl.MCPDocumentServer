using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Services.Admin;

namespace TxTextControl.McpServer.Pages.Admin;

public sealed class StylesModel : PageModel
{
    private readonly AutomationSettingsService _settings;
    private readonly SupportedFontService _fonts;

    public StylesModel(
        AutomationSettingsService settings,
        SupportedFontService fonts)
    {
        _settings = settings;
        _fonts = fonts;
    }

    [BindProperty]
    public string DefaultParagraphStyleName { get; set; } = "Body";

    [BindProperty]
    public StyleRoleInput StyleRoles { get; set; } = new();

    [BindProperty]
    public List<ParagraphPresetInput> ParagraphPresets { get; set; } = [];

    [BindProperty]
    public List<TablePresetInput> TablePresets { get; set; } = [];

    public IReadOnlyList<string> SupportedFonts { get; private set; } = [];

    public string? StatusMessage { get; private set; }

    public void OnGet()
    {
        LoadViewModel();
    }

    public IActionResult OnPost()
    {
        var paragraphPresets = ParagraphPresets
            .Where(preset => !preset.Delete && !string.IsNullOrWhiteSpace(preset.Name))
            .Select(preset => preset.ToDefinition())
            .ToList();

        var tablePresets = TablePresets
            .Where(preset => !preset.Delete && !string.IsNullOrWhiteSpace(preset.Name))
            .Select(preset => preset.ToDefinition())
            .ToList();

        _settings.SaveStylePresets(DefaultParagraphStyleName, StyleRoles.ToDefinition(), paragraphPresets, tablePresets);
        StatusMessage = "Style presets saved.";
        LoadViewModel();
        return Page();
    }

    private void LoadViewModel()
    {
        DefaultParagraphStyleName = _settings.GetDefaultParagraphStyleName();
        ParagraphPresets = _settings.GetStylePresets()
            .Select(ParagraphPresetInput.FromDefinition)
            .ToList();
        ParagraphPresets.Add(new ParagraphPresetInput());

        TablePresets = _settings.GetTableStylePresets()
            .Select(TablePresetInput.FromDefinition)
            .ToList();
        TablePresets.Add(new TablePresetInput());

        SupportedFonts = _fonts.GetSupportedFonts()
            .Concat(ParagraphPresets.Select(preset => preset.FontName))
            .Concat(TablePresets.Select(preset => preset.HeaderFontName))
            .Concat(TablePresets.Select(preset => preset.BodyFontName))
            .Where(font => !string.IsNullOrWhiteSpace(font))
            .Select(font => font!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(font => font, StringComparer.OrdinalIgnoreCase)
            .ToList();
        StyleRoles = StyleRoleInput.FromDefinition(_settings.GetStyleRoles(), DefaultParagraphStyleName);
    }

    public sealed class StyleRoleInput
    {
        public string Title { get; set; } = "Title";
        public string Heading1 { get; set; } = "Heading";
        public string Heading2 { get; set; } = "Heading2";
        public string Body { get; set; } = "Body";

        public static StyleRoleInput FromDefinition(StyleRoleDefinition roles, string defaultBodyStyleName)
            => new()
            {
                Title = string.IsNullOrWhiteSpace(roles.Title) ? "Title" : roles.Title,
                Heading1 = string.IsNullOrWhiteSpace(roles.Heading1) ? "Heading" : roles.Heading1,
                Heading2 = string.IsNullOrWhiteSpace(roles.Heading2) ? "Heading2" : roles.Heading2,
                Body = string.IsNullOrWhiteSpace(roles.Body) ? defaultBodyStyleName : roles.Body
            };

        public StyleRoleDefinition ToDefinition()
            => new()
            {
                Title = Title,
                Heading1 = Heading1,
                Heading2 = Heading2,
                Body = Body
            };
    }

    public sealed class ParagraphPresetInput
    {
        public string Name { get; set; } = string.Empty;
        public string? FontName { get; set; }
        public float? FontSize { get; set; }
        public string FontSizeUnit { get; set; } = "pt";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public string? ColorHex { get; set; }
        public float? SpaceBefore { get; set; }
        public float? SpaceAfter { get; set; }
        public string ParagraphUnit { get; set; } = "pt";
        public string? Alignment { get; set; }
        public bool Delete { get; set; }

        public static ParagraphPresetInput FromDefinition(TextStyleDefinition definition)
            => new()
            {
                Name = definition.Name,
                FontName = definition.FontName,
                FontSize = definition.FontSize,
                FontSizeUnit = definition.FontSizeUnit,
                Bold = definition.Bold ?? false,
                Italic = definition.Italic ?? false,
                Underline = definition.Underline ?? false,
                ColorHex = definition.ColorHex,
                SpaceBefore = definition.Paragraph?.SpaceBefore,
                SpaceAfter = definition.Paragraph?.SpaceAfter,
                ParagraphUnit = definition.Paragraph?.Unit ?? "pt",
                Alignment = definition.Paragraph?.Alignment
            };

        public TextStyleDefinition ToDefinition()
            => new()
            {
                Name = Name,
                FontName = FontName,
                FontSize = FontSize,
                FontSizeUnit = FontSizeUnit,
                Bold = Bold,
                Italic = Italic,
                Underline = Underline,
                ColorHex = ColorHex,
                Paragraph = new ParagraphStyleDefinition
                {
                    SpaceBefore = SpaceBefore,
                    SpaceAfter = SpaceAfter,
                    Unit = ParagraphUnit,
                    Alignment = Alignment
                }
            };
    }

    public sealed class TablePresetInput
    {
        public string Name { get; set; } = string.Empty;
        public int HeaderRowIndex { get; set; }
        public string? HeaderBackground { get; set; }
        public string? HeaderTextColor { get; set; }
        public string? HeaderFontName { get; set; }
        public float? HeaderFontSize { get; set; }
        public bool HeaderBold { get; set; } = true;
        public string? BodyBackground { get; set; }
        public string? BodyTextColor { get; set; }
        public string? BodyFontName { get; set; }
        public float? BodyFontSize { get; set; }
        public string? AlternatingBackground { get; set; }
        public int? BorderWidth { get; set; }
        public string? BorderColor { get; set; }
        public bool Delete { get; set; }

        public static TablePresetInput FromDefinition(TableStylePresetDefinition definition)
            => new()
            {
                Name = definition.Name,
                HeaderRowIndex = definition.HeaderRowIndex,
                HeaderBackground = definition.HeaderCellStyle?.BackgroundColorHex,
                HeaderTextColor = definition.HeaderStyle?.ColorHex,
                HeaderFontName = definition.HeaderStyle?.FontName,
                HeaderFontSize = definition.HeaderStyle?.FontSize,
                HeaderBold = definition.HeaderStyle?.Bold ?? true,
                BodyBackground = definition.BodyCellStyle?.BackgroundColorHex,
                BodyTextColor = definition.BodyStyle?.ColorHex,
                BodyFontName = definition.BodyStyle?.FontName,
                BodyFontSize = definition.BodyStyle?.FontSize,
                AlternatingBackground = definition.AlternatingRowCellStyle?.BackgroundColorHex,
                BorderWidth = definition.HeaderCellStyle?.Border?.Width
                              ?? definition.BodyCellStyle?.Border?.Width
                              ?? definition.AlternatingRowCellStyle?.Border?.Width,
                BorderColor = definition.HeaderCellStyle?.Border?.ColorHex
                              ?? definition.BodyCellStyle?.Border?.ColorHex
                              ?? definition.AlternatingRowCellStyle?.Border?.ColorHex
            };

        public TableStylePresetDefinition ToDefinition()
        {
            var border = new CellBorderDefinition
            {
                Width = BorderWidth,
                ColorHex = BorderColor
            };

            return new TableStylePresetDefinition
            {
                Name = Name,
                HeaderRowIndex = Math.Max(0, HeaderRowIndex),
                HeaderStyle = new TextStyleDefinition
                {
                    FontName = HeaderFontName,
                    FontSize = HeaderFontSize,
                    FontSizeUnit = "pt",
                    Bold = HeaderBold,
                    ColorHex = HeaderTextColor
                },
                HeaderCellStyle = new CellStyleDefinition
                {
                    BackgroundColorHex = HeaderBackground,
                    Border = border
                },
                BodyStyle = new TextStyleDefinition
                {
                    FontName = BodyFontName,
                    FontSize = BodyFontSize,
                    FontSizeUnit = "pt",
                    Bold = false,
                    ColorHex = BodyTextColor
                },
                BodyCellStyle = new CellStyleDefinition
                {
                    BackgroundColorHex = BodyBackground,
                    Border = border
                },
                AlternatingRowCellStyle = new CellStyleDefinition
                {
                    BackgroundColorHex = AlternatingBackground,
                    Border = border
                }
            };
        }
    }
}
