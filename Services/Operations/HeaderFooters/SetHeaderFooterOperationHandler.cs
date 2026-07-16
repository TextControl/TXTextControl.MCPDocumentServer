using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;
using TxFields = TXTextControl.DocumentServer.Fields;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class SetHeaderFooterOperationHandler : IDocumentOperationHandler
{
    public string Type => HeaderFooterCapabilityPack.SetHeaderFooter;
    public string CapabilityPack => HeaderFooterCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = HeaderFooterCapabilityPack.SetHeaderFooter,
        CapabilityPack = HeaderFooterCapabilityPack.PackName,
        Description = "Creates or replaces a document header or footer with text/runs, optional fields, and optional page number.",
        Intent = "Use for page headers, footers, invoice metadata, date fields, merge fields, page numbers, and repeated document information.",
        RequiredProperties = ["type", "headerFooterType", "text or runs"],
        OptionalProperties = ["style", "styleName", "runs", "typeName", "fieldName", "fieldText", "date", "dateFormat", "parameters", "includePageNumber", "sectionIndex"],
        Properties = new()
        {
            ["headerFooterType"] = "One of: header, footer, firstPageHeader, firstPageFooter, evenHeader, evenFooter.",
            ["text"] = "Header/footer text when runs are not supplied.",
            ["runs"] = "Optional ordered array of text runs; rendered before the optional page number.",
            ["style"] = "Optional inline TextStyleDefinition applied to the header/footer text. Omit unless the user explicitly asks for header/footer styling.",
            ["styleName"] = "Optional named style applied to the header/footer text. Omit when the prompt contains no explicit style instruction; the configured body default is applied automatically.",
            ["typeName"] = "Optional field type. Use DATE for a TX DocumentServer DateField. Defaults to MERGEFIELD when fieldName is supplied.",
            ["fieldName"] = "Optional MERGEFIELD name appended after the text/runs.",
            ["fieldText"] = "Optional visible placeholder text for the MERGEFIELD. Defaults to fieldName.",
            ["date"] = "Optional ISO date value for a DATE field. Defaults to today.",
            ["dateFormat"] = "Optional TX DateField format string. Defaults to d.",
            ["parameters"] = "Optional full ApplicationField parameters. If omitted, [fieldName] is used.",
            ["includePageNumber"] = "When true, appends a TX PageNumberField after the text.",
            ["sectionIndex"] = "Reserved zero-based section index. Defaults to 0; current implementation supports only 0."
        },
        Example = new()
        {
            ["type"] = HeaderFooterCapabilityPack.SetHeaderFooter,
            ["headerFooterType"] = "footer",
            ["text"] = "Date: ",
            ["typeName"] = "DATE",
            ["dateFormat"] = "d"
        },
        ModelEffects = ["Updates document.sections[sectionIndex].header or footer with paragraph and optional field content."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var type = ResolveHeaderFooterType(operation.HeaderFooterType);
        var sectionIndex = operation.SectionIndex ?? 0;
        if (sectionIndex != 0)
        {
            throw new NotSupportedException("Only sectionIndex 0 is supported for header/footer operations.");
        }

        var runs = NormalizeRuns(operation);
        var text = string.Concat(runs.Select(run => run.Text));
        var fieldKind = ResolveFieldKind(operation);
        var fieldName = fieldKind == HeaderFooterFieldKind.MergeField
            ? AppendMergeFieldOperationHandler.RequireFieldName(operation.FieldName)
            : string.IsNullOrWhiteSpace(operation.FieldName) ? "Date" : operation.FieldName!.Trim();
        var fieldParameters = fieldKind == HeaderFooterFieldKind.None
            ? []
            : AppendMergeFieldOperationHandler.NormalizeParameters(operation.Parameters, fieldName);
        var fieldText = fieldKind == HeaderFooterFieldKind.None
            ? null
            : string.IsNullOrWhiteSpace(operation.FieldText) ? fieldName : operation.FieldText!.Trim();
        var date = fieldKind == HeaderFooterFieldKind.DateField
            ? ResolveDate(operation.Date)
            : default;
        var dateFormat = string.IsNullOrWhiteSpace(operation.DateFormat) ? "d" : operation.DateFormat!.Trim();
        var textWithPlaceholders = text
                                   + (fieldKind == HeaderFooterFieldKind.DateField ? "{DATE}" : string.Empty)
                                   + (fieldKind == HeaderFooterFieldKind.MergeField ? $"{{MERGEFIELD {fieldName}}}" : string.Empty)
                                   + (operation.IncludePageNumber ? "{PAGE}" : string.Empty);

        if (context.TryGetTextControl(out var tx))
        {
            var collection = tx.HeadersAndFooters;
            if (collection.GetItem(type) is not null)
            {
                collection.Remove(type);
            }

            if (!collection.Add(type))
            {
                throw new InvalidOperationException($"TX Text Control could not add {type}.");
            }

            var headerFooter = collection.GetItem(type)
                ?? throw new InvalidOperationException($"TX Text Control added {type}, but it could not be found.");

            var contentText = text;
            if (!string.IsNullOrEmpty(contentText))
            {
                headerFooter.Selection.Text = contentText;
                ApplyTextStyle(context, headerFooter, runs, operation, text);
            }

            if (fieldKind == HeaderFooterFieldKind.DateField)
            {
                headerFooter.Selection = new Selection(text.Length, 0);
                var field = new TxFields.DateField
                {
                    Date = date,
                    Format = dateFormat,
                    Text = date.ToString(dateFormat)
                };

                if (!headerFooter.ApplicationFields.Add(field.ApplicationField))
                {
                    throw new InvalidOperationException("TX Text Control could not insert footer/header DATE field.");
                }
            }
            else if (fieldKind == HeaderFooterFieldKind.MergeField)
            {
                headerFooter.Selection = new Selection(text.Length, 0);
                var field = new ApplicationField(
                    ApplicationFieldFormat.MSWord,
                    "MERGEFIELD",
                    fieldText!,
                    fieldParameters.ToArray());

                if (!headerFooter.ApplicationFields.Add(field))
                {
                    throw new InvalidOperationException($"TX Text Control could not insert footer/header merge field '{fieldName}'.");
                }
            }

            if (operation.IncludePageNumber)
            {
                headerFooter.Selection.Text = string.Empty;
                headerFooter.PageNumberFields.Add(new PageNumberField(1, NumberFormat.ArabicNumbers));
            }
        }

        ApplyToModel(context, type, operation, runs, textWithPlaceholders, fieldKind, fieldName, fieldText, fieldParameters, date, dateFormat);
        var model = type is HeaderFooterType.Header or HeaderFooterType.FirstPageHeader or HeaderFooterType.EvenHeader
            ? context.GetMainSection().Header
            : context.GetMainSection().Footer;
        var modelType = ToModelType(type);
        var paragraphId = model?.Blocks.FirstOrDefault()?.Paragraph?.Id;

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Set {type} content.",
            TargetType = "headerFooter",
            TargetId = $"{sectionIndex}:{modelType}",
            Location = $"sections[{sectionIndex}].{modelType}",
            Metadata = new Dictionary<string, object?>
            {
                ["sectionIndex"] = sectionIndex,
                ["headerFooterType"] = modelType,
                ["paragraphId"] = paragraphId,
                ["includePageNumber"] = operation.IncludePageNumber,
                ["fieldName"] = fieldKind == HeaderFooterFieldKind.None ? null : fieldName,
                ["typeName"] = fieldKind == HeaderFooterFieldKind.DateField ? "DATE" : fieldKind == HeaderFooterFieldKind.MergeField ? "MERGEFIELD" : null
            }
        };
    }

    private static HeaderFooterType ResolveHeaderFooterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("headerFooterType is required.");
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "header" => HeaderFooterType.Header,
            "footer" => HeaderFooterType.Footer,
            "firstpageheader" or "first_page_header" or "first-page-header" => HeaderFooterType.FirstPageHeader,
            "firstpagefooter" or "first_page_footer" or "first-page-footer" => HeaderFooterType.FirstPageFooter,
            "evenheader" or "even_header" or "even-header" => HeaderFooterType.EvenHeader,
            "evenfooter" or "even_footer" or "even-footer" => HeaderFooterType.EvenFooter,
            _ => throw new ArgumentException("headerFooterType must be one of: header, footer, firstPageHeader, firstPageFooter, evenHeader, evenFooter.")
        };
    }

    private static List<DocumentModel.Run> NormalizeRuns(DocumentOperation operation)
    {
        if (operation.Runs.Count > 0)
        {
            return operation.Runs.Select(run => new DocumentModel.Run
            {
                Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id,
                Text = run.Text ?? string.Empty,
                StyleName = string.IsNullOrWhiteSpace(run.StyleName) ? null : run.StyleName.Trim(),
                Style = run.Style
            }).ToList();
        }

        return
        [
            new DocumentModel.Run
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = operation.Text ?? string.Empty,
                StyleName = string.IsNullOrWhiteSpace(operation.StyleName) ? null : operation.StyleName.Trim(),
                Style = operation.Style
            }
        ];
    }

    private static void ApplyTextStyle(
        DocumentOperationContext context,
        TXTextControl.HeaderFooter headerFooter,
        IReadOnlyList<DocumentModel.Run> runs,
        DocumentOperation operation,
        string text)
    {
        if (!string.IsNullOrWhiteSpace(operation.StyleName))
        {
            var style = context.GetStyle(operation.StyleName);
            style.Name = operation.StyleName.Trim();
            headerFooter.Selection.Start = 0;
            headerFooter.Selection.Length = text.Length;
            DocumentOperationFormatter.ApplyStyle(headerFooter.Selection, style);
            headerFooter.Selection.Start = text.Length;
            headerFooter.Selection.Length = 0;
            return;
        }

        if (operation.Style is not null)
        {
            headerFooter.Selection.Start = 0;
            headerFooter.Selection.Length = text.Length;
            DocumentOperationFormatter.ApplyStyle(headerFooter.Selection, operation.Style);
            headerFooter.Selection.Start = text.Length;
            headerFooter.Selection.Length = 0;
            return;
        }

        var hasRunStyle = runs.Any(run =>
            run.Style is not null || !string.IsNullOrWhiteSpace(run.StyleName));
        if (!hasRunStyle)
        {
            headerFooter.Selection.Start = 0;
            headerFooter.Selection.Length = text.Length;
            DocumentOperationFormatter.ApplyStyle(headerFooter.Selection, context.GetDefaultTextStyle());
            headerFooter.Selection.Start = text.Length;
            headerFooter.Selection.Length = 0;
            return;
        }

        var offset = 0;
        foreach (var run in runs)
        {
            var runStyle = run.Style ?? (!string.IsNullOrWhiteSpace(run.StyleName) ? context.GetStyle(run.StyleName) : null);
            if (runStyle is not null && run.Text?.Length > 0)
            {
                headerFooter.Selection.Start = offset;
                headerFooter.Selection.Length = run.Text.Length;
                DocumentOperationFormatter.ApplyStyle(headerFooter.Selection, runStyle);
            }

            offset += run.Text?.Length ?? 0;
        }

        headerFooter.Selection.Start = text.Length;
        headerFooter.Selection.Length = 0;
    }

    private static void ApplyToModel(
        DocumentOperationContext context,
        HeaderFooterType type,
        DocumentOperation operation,
        List<DocumentModel.Run> runs,
        string textWithPlaceholders,
        HeaderFooterFieldKind fieldKind,
        string? fieldName,
        string? fieldText,
        IReadOnlyList<string> fieldParameters,
        DateTime date,
        string dateFormat)
    {
        var section = context.GetMainSection();
        var styleName = string.IsNullOrWhiteSpace(operation.StyleName)
            ? context.GetDefaultParagraphStyleName()
            : operation.StyleName.Trim();
        var paragraph = new DocumentModel.Paragraph
        {
            Id = Guid.NewGuid().ToString("N"),
            StyleName = styleName,
            Runs = runs
        };

        if (operation.IncludePageNumber || fieldKind != HeaderFooterFieldKind.None)
        {
            paragraph.Runs =
            [
                new DocumentModel.Run
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Text = textWithPlaceholders,
                    Style = operation.Style,
                    StyleName = styleName
                }
            ];
        }

        var blocks = new List<DocumentModel.DocumentBlock>
        {
            new()
            {
                Type = "paragraph",
                Paragraph = paragraph
            }
        };

        if (fieldKind != HeaderFooterFieldKind.None)
        {
            blocks.Add(new DocumentModel.DocumentBlock
            {
                Type = "field",
                Field = new DocumentModel.Field
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "merge",
                    Name = fieldName,
                    Value = fieldKind == HeaderFooterFieldKind.DateField ? date.ToString(dateFormat) : fieldText,
                    Properties = new Dictionary<string, string>
                    {
                        ["format"] = "MSWord",
                        ["typeName"] = fieldKind == HeaderFooterFieldKind.DateField ? "DATE" : "MERGEFIELD",
                        ["parameters"] = string.Join("|", fieldParameters),
                        ["date"] = fieldKind == HeaderFooterFieldKind.DateField ? date.ToString("O") : string.Empty,
                        ["dateFormat"] = fieldKind == HeaderFooterFieldKind.DateField ? dateFormat : string.Empty
                    }
                }
            });
        }

        var model = new DocumentModel.HeaderFooter
        {
            Type = ToModelType(type),
            Blocks = blocks
        };

        if (type is HeaderFooterType.Header or HeaderFooterType.FirstPageHeader or HeaderFooterType.EvenHeader)
        {
            section.Header = model;
        }
        else
        {
            section.Footer = model;
        }
    }

    private static string ToModelType(HeaderFooterType type)
        => type switch
        {
            HeaderFooterType.Header => "header",
            HeaderFooterType.Footer => "footer",
            HeaderFooterType.FirstPageHeader => "firstPageHeader",
            HeaderFooterType.FirstPageFooter => "firstPageFooter",
            HeaderFooterType.EvenHeader => "evenHeader",
            HeaderFooterType.EvenFooter => "evenFooter",
            _ => type.ToString()
        };

    private static HeaderFooterFieldKind ResolveFieldKind(DocumentOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.TypeName))
        {
            return string.IsNullOrWhiteSpace(operation.FieldName)
                ? HeaderFooterFieldKind.None
                : HeaderFooterFieldKind.MergeField;
        }

        return operation.TypeName.Trim().ToLowerInvariant() switch
        {
            "date" or "datefield" => HeaderFooterFieldKind.DateField,
            "mergefield" or "merge_field" or "merge-field" => HeaderFooterFieldKind.MergeField,
            _ => throw new ArgumentException("typeName must be DATE or MERGEFIELD for header/footer fields.")
        };
    }

    private static DateTime ResolveDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.Today;
        }

        return DateTime.TryParse(value.Trim(), out var parsed)
            ? parsed
            : throw new ArgumentException("date must be a valid date value.");
    }

    private enum HeaderFooterFieldKind
    {
        None,
        DateField,
        MergeField
    }
}
