using System;
using System.Collections.Generic;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.DocumentModel;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class DocumentOperationContext
{
    private readonly ServerTextControl? _textControl;

    public DocumentOperationContext(
        ServerTextControl textControl,
        Document document,
        IDictionary<string, TextStyleDefinition> styles,
        string? defaultParagraphStyleName = null)
    {
        _textControl = textControl;
        Document = document;
        Styles = styles;
        DefaultParagraphStyleName = string.IsNullOrWhiteSpace(defaultParagraphStyleName)
            ? "Body"
            : defaultParagraphStyleName.Trim();
    }

    public DocumentOperationContext(
        Document document,
        IDictionary<string, TextStyleDefinition> styles,
        string? defaultParagraphStyleName = null)
    {
        Document = document;
        Styles = styles;
        DefaultParagraphStyleName = string.IsNullOrWhiteSpace(defaultParagraphStyleName)
            ? "Body"
            : defaultParagraphStyleName.Trim();
    }

    public ServerTextControl TextControl
        => _textControl ?? throw new InvalidOperationException("A TX Text Control instance is required for this operation.");

    public bool TryGetTextControl(out ServerTextControl textControl)
    {
        textControl = _textControl!;
        return textControl is not null;
    }

    public Document Document { get; }
    public IDictionary<string, TextStyleDefinition> Styles { get; }
    public string DefaultParagraphStyleName { get; }
    public int? InlineDocumentEndInsertionIndex { get; set; }
    public int? DocumentEndInsertionIndex { get; set; }
    public int DocumentPositionCorrection { get; set; }
    public bool HasOpenParagraph { get; set; }
    public int CurrentSectionIndex { get; set; }

    public DocumentModel.Section GetMainSection()
    {
        if (Document.Sections.Count == 0)
        {
            Document.Sections.Add(new DocumentModel.Section
            {
                Id = Guid.NewGuid().ToString("N")
            });
        }

        return Document.Sections[0];
    }

    public DocumentModel.Section GetSection(int sectionIndex)
    {
        if (sectionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectionIndex), "sectionIndex must be >= 0.");
        }

        while (Document.Sections.Count <= sectionIndex)
        {
            Document.Sections.Add(new DocumentModel.Section
            {
                Id = Guid.NewGuid().ToString("N")
            });
        }

        return Document.Sections[sectionIndex];
    }

    public DocumentModel.Section GetCurrentSection()
        => GetSection(CurrentSectionIndex);

    public TextStyleDefinition GetStyle(string styleName)
    {
        if (string.IsNullOrWhiteSpace(styleName))
        {
            throw new ArgumentException("styleName is required.");
        }

        if (!Styles.TryGetValue(styleName.Trim(), out var style))
        {
            throw new InvalidOperationException($"Style '{styleName}' is not defined.");
        }

        return style;
    }

    public TextStyleDefinition GetDefaultTextStyle()
    {
        if (Styles.TryGetValue(DefaultParagraphStyleName, out var preset))
        {
            return preset;
        }

        return new TextStyleDefinition
        {
            FontName = "Arial",
            FontSize = 10,
            FontSizeUnit = "pt",
            Bold = false,
            Italic = false,
            Underline = false,
            ColorHex = "#000000"
        };
    }

    public string? GetDefaultParagraphStyleName()
        => Styles.ContainsKey(DefaultParagraphStyleName) ? DefaultParagraphStyleName : null;
}
