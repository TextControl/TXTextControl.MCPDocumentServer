using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services;

public static class DocumentModelOperationCompiler
{
    public static ApplyOperationsRequest Compile(RenderDocumentModelRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Document is null)
        {
            throw new ArgumentException("'document' is required.", nameof(request));
        }

        var operations = new List<DocumentOperation>();
        foreach (var style in request.Document.Styles)
        {
            if (style.Text is not null)
            {
                style.Text.Name = string.IsNullOrWhiteSpace(style.Text.Name) ? style.Name : style.Text.Name;
                operations.Add(new DocumentOperation
                {
                    Type = "define_style",
                    Style = style.Text
                });
            }
        }

        for (var sectionIndex = 0; sectionIndex < request.Document.Sections.Count; sectionIndex++)
        {
            var section = request.Document.Sections[sectionIndex];
            if (sectionIndex > 0)
            {
                operations.Add(new DocumentOperation
                {
                    Type = SectionCapabilityPack.InsertSectionBreak,
                    BreakKind = "beginAtNewPage"
                });
            }

            if (section.PageLayout is not null)
            {
                operations.Add(new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    SectionIndex = sectionIndex,
                    PageLayout = section.PageLayout
                });
            }

            if (section.Header is not null)
            {
                operations.AddRange(CompileHeaderFooter(section.Header, isHeader: true));
            }

            if (section.Footer is not null)
            {
                operations.AddRange(CompileHeaderFooter(section.Footer, isHeader: false));
            }

            foreach (var block in section.Blocks)
            {
                operations.Add(CompileBlock(block));
            }
        }

        if (operations.Count == 0)
        {
            throw new ArgumentException("'document' must contain at least one style or supported content block.", nameof(request));
        }

        return new ApplyOperationsRequest
        {
            SessionId = request.SessionId,
            CreateIfMissing = request.CreateIfMissing,
            Operations = operations
        };
    }

    private static DocumentOperation CompileBlock(DocumentModel.DocumentBlock block)
        => block.Type.Trim().ToLowerInvariant() switch
        {
            "paragraph" => CompileParagraph(block.Paragraph),
            "table" => CompileTable(block.Table),
            "image" => CompileImage(block.Image),
            "field" => CompileField(block.Field),
            _ => throw new NotSupportedException($"Unsupported document block type '{block.Type}'.")
        };

    private static DocumentOperation CompileParagraph(DocumentModel.Paragraph? paragraph)
    {
        if (paragraph is null)
        {
            throw new ArgumentException("paragraph block requires paragraph content.");
        }

        return new DocumentOperation
        {
            Type = "append_paragraph",
            Text = string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty)),
            Runs = paragraph.Runs
                .Select(run => new DocumentModel.Run
                {
                    Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id,
                    Text = run.Text ?? string.Empty,
                    StyleName = string.IsNullOrWhiteSpace(run.StyleName) ? null : run.StyleName.Trim(),
                    Style = run.Style
                })
                .ToList(),
            StyleName = string.IsNullOrWhiteSpace(paragraph.StyleName) ? null : paragraph.StyleName.Trim()
        };
    }

    private static DocumentOperation CompileTable(DocumentModel.Table? table)
    {
        if (table is null)
        {
            throw new ArgumentException("table block requires table content.");
        }

        return new DocumentOperation
        {
            Type = "append_table",
            TableId = string.IsNullOrWhiteSpace(table.Id) ? null : table.Id.Trim(),
            StyleName = string.IsNullOrWhiteSpace(table.StyleName) ? null : table.StyleName.Trim(),
            Rows = table.Rows
                .Select(row => row.Cells
                    .Select(GetCellText)
                    .ToList())
                .ToList()
        };
    }

    private static DocumentOperation CompileImage(DocumentModel.Image? image)
    {
        if (image is null)
        {
            throw new ArgumentException("image block requires image content.");
        }

        var operation = new DocumentOperation
        {
            Type = "append_image",
            AltText = image.AltText,
            Width = image.Width,
            Height = image.Height,
            Unit = image.Unit,
            HorizontalScaling = image.HorizontalScaling,
            VerticalScaling = image.VerticalScaling,
            InsertionMode = image.InsertionMode,
            Alignment = image.Alignment,
            LocationX = image.LocationX,
            LocationY = image.LocationY,
            LocationUnit = image.LocationUnit
        };

        if (image.Source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            operation.ImageBase64 = image.Source;
        }
        else
        {
            operation.ImagePath = image.Source;
        }

        return operation;
    }

    private static IReadOnlyList<DocumentOperation> CompileHeaderFooter(DocumentModel.HeaderFooter headerFooter, bool isHeader)
    {
        var operations = new List<DocumentOperation>();
        var target = string.IsNullOrWhiteSpace(headerFooter.Type)
            ? (isHeader ? "header" : "footer")
            : headerFooter.Type;
        var paragraph = headerFooter.Blocks
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .FirstOrDefault(paragraph => paragraph is not null);

        if (paragraph is not null)
        {
            var text = string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty));
            var includePageNumber = text.Contains("{PAGE}", StringComparison.OrdinalIgnoreCase);
            if (includePageNumber)
            {
                text = text.Replace("{PAGE}", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            operations.Add(new DocumentOperation
            {
                Type = "set_header_footer",
                HeaderFooterType = target,
                Text = text,
                Runs = paragraph.Runs
                    .Select(run => new DocumentModel.Run
                    {
                        Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id,
                        Text = (run.Text ?? string.Empty).Replace("{PAGE}", string.Empty, StringComparison.OrdinalIgnoreCase),
                        StyleName = string.IsNullOrWhiteSpace(run.StyleName) ? null : run.StyleName.Trim(),
                        Style = run.Style
                    })
                    .Where(run => !string.IsNullOrEmpty(run.Text))
                    .ToList(),
                StyleName = string.IsNullOrWhiteSpace(paragraph.StyleName) ? null : paragraph.StyleName.Trim(),
                IncludePageNumber = includePageNumber
            });
        }

        foreach (var imageBlock in headerFooter.Blocks.Where(block => string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase)))
        {
            var operation = CompileImage(imageBlock.Image);
            operation.Target = target;
            operations.Add(operation);
        }

        if (operations.Count == 0)
        {
            throw new NotSupportedException("HeaderFooter rendering currently supports paragraph and image blocks.");
        }

        return operations;
    }

    private static DocumentOperation CompileField(DocumentModel.Field? field)
    {
        if (field is null)
        {
            throw new ArgumentException("field block requires field content.");
        }

        if (!string.Equals(field.Type, "merge", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported field type '{field.Type}'.");
        }

        var parameters = new List<string>();
        if (field.Properties.TryGetValue("parameters", out var serializedParameters)
            && !string.IsNullOrWhiteSpace(serializedParameters))
        {
            parameters.AddRange(serializedParameters
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return new DocumentOperation
        {
            Type = "append_merge_field",
            FieldId = string.IsNullOrWhiteSpace(field.Id) ? null : field.Id.Trim(),
            FieldName = field.Name,
            FieldText = field.Value,
            Parameters = parameters
        };
    }

    private static string GetCellText(DocumentModel.TableCell cell)
        => string.Join(
            "\n",
            cell.Blocks
                .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
                .Select(block => block.Paragraph)
                .Where(paragraph => paragraph is not null)
                .Select(paragraph => string.Concat(paragraph!.Runs.Select(run => run.Text ?? string.Empty))));
}
