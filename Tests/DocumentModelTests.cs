using TxTextControl.McpServer.Models.DocumentModel;
using System.Runtime.CompilerServices;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Operations;
using Xunit;

namespace TxTextControl.McpServer.Tests;

public sealed class DocumentModelTests
{
    [Fact]
    public void DefineStyleOperationUpdatesNeutralDocumentStyles()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new DefineStyleOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = BasicTextCapabilityPack.DefineStyle,
            Style = new TextStyleDefinition
            {
                Name = "Heading",
                FontName = "Arial",
                FontSize = 20,
                FontSizeUnit = "px",
                Bold = true
            }
        }, 0);

        var style = Assert.Single(document.Styles);
        Assert.Equal("Heading", style.Name);
        Assert.Equal("Arial", style.Text?.FontName);
        Assert.True(style.Text?.Bold);
    }

    [Fact]
    public void AppendImageRejectsUnsupportedImageFormats()
    {
        var artifactRoot = Path.Combine(GetRepositoryRoot(), "Tests", "TestArtifacts");
        Directory.CreateDirectory(artifactRoot);
        var imagePath = Path.Combine(artifactRoot, "unsupported-image.webp");
        File.WriteAllBytes(imagePath, [1, 2, 3]);

        var context = new DocumentOperationContext(
            CreateDocument(),
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendImageOperationHandler();

        var exception = Assert.Throws<NotSupportedException>(() => handler.Apply(context, new DocumentOperation
        {
            Type = MediaCapabilityPack.AppendImage,
            ImagePath = imagePath
        }, 0));

        Assert.Contains("Unsupported image format", exception.Message);
    }

    [Fact]
    public void CompileImageSupportsDataUriSources()
    {
        var dataUri = "data:image/png;base64,iVBORw0KGgo=";

        var request = new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = new Document
            {
                Sections =
                [
                    new Section
                    {
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "image",
                                Image = new Image
                                {
                                    Source = dataUri,
                                    AltText = "Embedded image",
                                    HorizontalScaling = 75,
                                    VerticalScaling = 75,
                                    Alignment = "centered",
                                    InsertionMode = "displaceText"
                                }
                            }
                        ]
                    }
                ]
            }
        };

        var compiled = DocumentModelOperationCompiler.Compile(request);
        var operation = Assert.Single(compiled.Operations);

        Assert.Equal(MediaCapabilityPack.AppendImage, operation.Type);
        Assert.Null(operation.ImagePath);
        Assert.Equal(dataUri, operation.ImageBase64);
        Assert.Equal("Embedded image", operation.AltText);
        Assert.Equal(75, operation.HorizontalScaling);
        Assert.Equal(75, operation.VerticalScaling);
        Assert.Equal("centered", operation.Alignment);
        Assert.Equal("displaceText", operation.InsertionMode);
    }

    [Fact]
    public void CompileParagraphSupportsPlainTextShorthand()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Sections =
                [
                    new Section
                    {
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "paragraph",
                                Paragraph = new Paragraph { Text = "Hello from AI!" }
                            }
                        ]
                    }
                ]
            }
        };

        var operation = Assert.Single(DocumentModelOperationCompiler.Compile(request).Operations);

        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operation.Type);
        Assert.Equal("Hello from AI!", operation.Text);
        Assert.Equal("Hello from AI!", Assert.Single(operation.Runs).Text);
    }

    [Fact]
    public void AppendParagraphUsesTitleRoleForFirstUnstyledBodyParagraph()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["Title"] = new()
                {
                    Name = "Title",
                    FontName = "Arial",
                    FontSize = 30,
                    FontSizeUnit = "pt",
                    Bold = true
                },
                ["Body"] = new()
                {
                    Name = "Body",
                    FontName = "Arial",
                    FontSize = 12,
                    FontSizeUnit = "pt"
                }
            },
            defaultParagraphStyleName: "Body",
            titleStyleName: "Title");
        var handler = new AppendParagraphOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = BasicTextCapabilityPack.AppendParagraph,
            Text = "INVOICE"
        }, 0);
        handler.Apply(context, new DocumentOperation
        {
            Type = BasicTextCapabilityPack.AppendParagraph,
            Text = "Thank you for your business."
        }, 1);

        Assert.Equal("Title", document.Sections[0].Blocks[0].Paragraph?.StyleName);
        Assert.Equal("Body", document.Sections[0].Blocks[1].Paragraph?.StyleName);
    }

    [Fact]
    public void AppendTableUpdatesNeutralDocumentModel()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            TableId = "42",
            Rows =
            [
                ["Quarter", "Revenue"],
                ["Q1", "$1.2M"],
                ["Q2", "$1.5M"]
            ]
        }, 0);

        var block = Assert.Single(document.Sections[0].Blocks);
        Assert.Equal("table", block.Type);
        Assert.Equal("42", block.Table?.Id);
        Assert.Equal(3, block.Table?.Rows.Count);
        Assert.Equal("Revenue", block.Table?.Rows[0].Cells[1].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("table", result.TargetType);
        Assert.Equal("42", result.TargetId);
        Assert.Equal("42", result.Metadata["tableId"]);
        Assert.Equal(3, result.Metadata["rowCount"]);
        Assert.Equal(2, result.Metadata["columnCount"]);
    }

    [Fact]
    public void AppendTableAppliesConfiguredDefaultTablePreset()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler(new DocumentAutomationOptions
        {
            TableStylePresets =
            [
                new TableStylePresetDefinition
                {
                    Name = "Professional Blue",
                    HeaderStyle = new TextStyleDefinition
                    {
                        Bold = true,
                        ColorHex = "#FFFFFF"
                    },
                    HeaderCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#1F4E79"
                    },
                    BodyStyle = new TextStyleDefinition
                    {
                        FontSize = 10,
                        FontSizeUnit = "pt",
                        ColorHex = "#111827"
                    },
                    BodyCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#FFFFFF"
                    }
                }
            ]
        });

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            TableId = "45",
            Rows =
            [
                ["Item", "Price"],
                ["Support", "$100"]
            ]
        }, 0);

        var table = document.Sections[0].Blocks[0].Table;
        Assert.Equal("Professional Blue", table?.StyleName);
        Assert.Equal("#1F4E79", table?.Rows[0].Cells[0].CellStyle?.BackgroundColorHex);
        Assert.True(table?.Rows[0].Cells[0].Blocks[0].Paragraph?.Runs[0].Style?.Bold);
        Assert.Equal("#FFFFFF", table?.Rows[1].Cells[0].CellStyle?.BackgroundColorHex);
        Assert.Equal("Professional Blue", result.Metadata["styleName"]);
    }

    [Fact]
    public void AppendTablePadsUnevenRowsInNeutralDocumentModel()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            TableId = "43",
            Rows =
            [
                ["country", "sales", "qty"],
                ["Germany", "$842,000"],
                ["Japan", "$638,500", "940"]
            ]
        }, 0);

        var table = document.Sections[0].Blocks[0].Table;

        Assert.NotNull(table);
        Assert.All(table.Rows, row => Assert.Equal(3, row.Cells.Count));
        Assert.Equal(string.Empty, table.Rows[1].Cells[2].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("940", table.Rows[2].Cells[2].Blocks[0].Paragraph?.Runs[0].Text);
    }

    [Fact]
    public void AppendTableCanInsertAfterParagraphInNeutralDocumentModel()
    {
        var document = CreateDocument();
        document.Sections[0].Blocks.Add(CreateTextBlock("First paragraph"));
        document.Sections[0].Blocks.Add(CreateTextBlock("Second paragraph"));
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            TableId = "44",
            ParagraphIndex = 0,
            Placement = "after",
            Rows =
            [
                ["Metric", "Value"],
                ["Pipeline", "Healthy"]
            ]
        }, 0);

        Assert.Equal("paragraph", document.Sections[0].Blocks[0].Type);
        Assert.Equal("table", document.Sections[0].Blocks[1].Type);
        Assert.Equal("paragraph", document.Sections[0].Blocks[2].Type);
        Assert.Equal("44", document.Sections[0].Blocks[1].Table?.Id);
        Assert.Equal("Second paragraph", document.Sections[0].Blocks[2].Paragraph?.Runs[0].Text);
    }

    [Fact]
    public void AppendTableGeneratesNextAvailableTableId()
    {
        var document = CreateDocument();
        document.Sections[0].Blocks.Add(new DocumentBlock
        {
            Type = "table",
            Table = new Table
            {
                Id = "10",
                Rows =
                [
                    new TableRow
                    {
                        Id = "10:r1",
                        Cells = [CreateTextCell("10:r1c1", "existing")]
                    }
                ]
            }
        });
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            Rows =
            [
                ["new"]
            ]
        }, 0);

        Assert.Equal("11", document.Sections[0].Blocks[1].Table?.Id);
    }

    [Fact]
    public void AppendTableRejectsDuplicateTableId()
    {
        var document = CreateDocument();
        document.Sections[0].Blocks.Add(new DocumentBlock
        {
            Type = "table",
            Table = new Table { Id = "42" }
        });
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        var exception = Assert.Throws<InvalidOperationException>(() => handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            TableId = "42",
            Rows =
            [
                ["duplicate"]
            ]
        }, 0));

        Assert.Contains("already exists", exception.Message);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("9")]
    [InlineData("32768")]
    public void AppendTableRejectsInvalidTableIds(string tableId)
    {
        var context = new DocumentOperationContext(
            CreateDocument(),
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        var exception = Assert.Throws<ArgumentException>(() => handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            TableId = tableId,
            Rows =
            [
                ["invalid"]
            ]
        }, 0));

        Assert.Contains("tableId", exception.Message);
    }

    [Fact]
    public void SetTableCellTextUpdatesNeutralModel()
    {
        var document = CreateDocument();
        AddSampleTable(document, "50");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new SetTableCellTextOperationHandler();

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.SetTableCellText,
            TableId = "50",
            RowIndex = 1,
            ColumnIndex = 1,
            Text = "$900,000"
        }, 0);

        Assert.Equal("$900,000", document.Sections[0].Blocks[0].Table?.Rows[1].Cells[1].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("tableCell", result.TargetType);
        Assert.Equal("50:r2c2", result.TargetId);
        Assert.Equal(1, result.Metadata["rowIndex"]);
        Assert.Equal(1, result.Metadata["columnIndex"]);
    }

    [Fact]
    public void FormatTableCellUpdatesNeutralModel()
    {
        var document = CreateDocument();
        AddSampleTable(document, "51");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new FormatTableCellOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.FormatTableCell,
            TableId = "51",
            RowIndex = 1,
            ColumnIndex = 1,
            Style = new TextStyleDefinition
            {
                Bold = true
            },
            CellStyle = new CellStyleDefinition
            {
                BackgroundColorHex = "#D9EAF7",
                Border = new CellBorderDefinition
                {
                    Width = 10,
                    ColorHex = "#000000"
                }
            }
        }, 0);

        var run = document.Sections[0].Blocks[0].Table?.Rows[1].Cells[1].Blocks[0].Paragraph?.Runs[0];
        Assert.Equal("$842,000", run?.Text);
        Assert.True(run?.Style?.Bold);
        Assert.Equal("#D9EAF7", document.Sections[0].Blocks[0].Table?.Rows[1].Cells[1].CellStyle?.BackgroundColorHex);
        Assert.Equal(10, document.Sections[0].Blocks[0].Table?.Rows[1].Cells[1].CellStyle?.Border?.Width);
        Assert.Equal("#000000", document.Sections[0].Blocks[0].Table?.Rows[1].Cells[1].CellStyle?.Border?.ColorHex);
    }

    [Fact]
    public void FormatTableHeaderRowUpdatesNeutralModel()
    {
        var document = CreateDocument();
        AddSampleTable(document, "52");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new FormatTableHeaderRowOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.FormatTableHeaderRow,
            TableId = "52",
            Style = new TextStyleDefinition
            {
                Bold = true
            },
            CellStyle = new CellStyleDefinition
            {
                BackgroundColorHex = "#1F4E79",
                Border = new CellBorderDefinition
                {
                    Bottom = new CellBorderSideDefinition
                    {
                        Width = 20,
                        ColorHex = "#000000"
                    }
                }
            }
        }, 0);

        var header = document.Sections[0].Blocks[0].Table?.Rows[0];
        Assert.All(header!.Cells, cell => Assert.True(cell.Blocks[0].Paragraph?.Runs[0].Style?.Bold));
        Assert.All(header.Cells, cell => Assert.Equal("#1F4E79", cell.CellStyle?.BackgroundColorHex));
        Assert.All(header.Cells, cell => Assert.Equal(20, cell.CellStyle?.Border?.Bottom?.Width));
        Assert.All(header.Cells, cell => Assert.Equal("#000000", cell.CellStyle?.Border?.Bottom?.ColorHex));
    }

    [Fact]
    public void AddTableRowUpdatesNeutralModel()
    {
        var document = CreateDocument();
        AddSampleTable(document, "53");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AddTableRowOperationHandler();

        handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AddTableRow,
            TableId = "53",
            Rows =
            [
                ["Brazil", "$421,750"]
            ]
        }, 0);

        var table = document.Sections[0].Blocks[0].Table;
        Assert.Equal(3, table?.Rows.Count);
        Assert.Equal("Brazil", table?.Rows[2].Cells[0].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("$421,750", table?.Rows[2].Cells[1].Blocks[0].Paragraph?.Runs[0].Text);
    }

    [Fact]
    public void AddTableRowRejectsTooManyCells()
    {
        var document = CreateDocument();
        AddSampleTable(document, "54");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AddTableRowOperationHandler();

        var exception = Assert.Throws<ArgumentException>(() => handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AddTableRow,
            TableId = "54",
            Rows =
            [
                ["Brazil", "$421,750", "705"]
            ]
        }, 0));

        Assert.Contains("more cells", exception.Message);
    }

    [Fact]
    public void AppendMergeFieldUpdatesNeutralModel()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendMergeFieldOperationHandler();

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendMergeField,
            FieldName = "CustomerName",
            FieldText = "Customer Name"
        }, 0);

        var field = document.Sections[0].Blocks[0].Field;
        Assert.Equal("field", document.Sections[0].Blocks[0].Type);
        Assert.Equal("merge", field?.Type);
        Assert.Equal("CustomerName", field?.Name);
        Assert.Equal("Customer Name", field?.Value);
        Assert.Equal("MERGEFIELD", field?.Properties["typeName"]);
        Assert.Equal("CustomerName", field?.Properties["parameters"]);
        Assert.Equal("field", result.TargetType);
        Assert.Equal(field?.Id, result.TargetId);
        Assert.Equal("CustomerName", result.Metadata["fieldName"]);
    }

    [Fact]
    public void AppendMergeFieldCanReplaceTableCellContentInNeutralModel()
    {
        var document = CreateDocument();
        AddSampleTable(document, "55");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));

        new AppendMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendMergeField,
            FieldName = "ProductName",
            FieldText = "Product Name",
            TableId = "55",
            RowIndex = 1,
            ColumnIndex = 0,
            Placement = "replace"
        }, 0);

        var cellBlocks = document.Sections[0].Blocks[0].Table?.Rows[1].Cells[0].Blocks;
        Assert.Single(cellBlocks!);
        Assert.Equal("field", cellBlocks![0].Type);
        Assert.Equal("ProductName", cellBlocks[0].Field?.Name);
        Assert.Equal("Product Name", cellBlocks[0].Field?.Value);
    }

    [Fact]
    public void UpdateMergeFieldUpdatesNeutralModel()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        new AppendMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendMergeField,
            FieldName = "Address"
        }, 0);

        new UpdateMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.UpdateMergeField,
            FieldName = "Address",
            FieldText = "Customer Address",
            Parameters = ["Customer.Address"]
        }, 1);

        var field = document.Sections[0].Blocks[0].Field;
        Assert.Equal("Customer.Address", field?.Name);
        Assert.Equal("Customer Address", field?.Value);
        Assert.Equal("Customer.Address", field?.Properties["parameters"]);
    }

    [Fact]
    public void ClearApplicationFieldsRemovesNeutralFieldBlocks()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        new AppendMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendMergeField,
            FieldName = "CustomerName"
        }, 0);

        new ClearApplicationFieldsOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.ClearApplicationFields
        }, 1);

        Assert.Empty(document.Sections[0].Blocks);
    }

    [Fact]
    public void ClearApplicationFieldsRemovesNestedTableCellFieldBlocks()
    {
        var document = CreateDocument();
        AddSampleTable(document, "56");
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        new AppendMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendMergeField,
            FieldName = "ProductName",
            TableId = "56",
            RowIndex = 1,
            ColumnIndex = 0,
            Placement = "replace"
        }, 0);

        new ClearApplicationFieldsOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.ClearApplicationFields
        }, 1);

        Assert.Empty(document.Sections[0].Blocks[0].Table!.Rows[1].Cells[0].Blocks);
    }

    [Fact]
    public void ClearApplicationFieldsPreservesFormFieldBlocks()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        new AppendMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendMergeField,
            FieldName = "CustomerName"
        }, 0);
        new AppendFormFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendFormField,
            FieldName = "approved",
            FormFieldType = "checkbox"
        }, 1);

        new ClearApplicationFieldsOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.ClearApplicationFields
        }, 2);

        var block = Assert.Single(document.Sections[0].Blocks);
        Assert.Equal("form", block.Field?.Type);
        Assert.Equal("approved", block.Field?.Name);
    }

    [Fact]
    public void ClearFormFieldsRemovesFieldsFromOriginalModelLists()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        new AppendFormFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.AppendFormField,
            FieldName = "signature",
            FormFieldType = "text"
        }, 0);

        new ClearFormFieldsOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.ClearFormFields
        }, 1);

        Assert.Empty(document.Sections[0].Blocks);
    }

    [Fact]
    public void UpdatingUnknownFieldsFailsInsteadOfReportingFalseSuccess()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));

        Assert.Throws<ArgumentException>(() => new UpdateMergeFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.UpdateMergeField,
            FieldName = "Missing"
        }, 0));
        Assert.Throws<ArgumentException>(() => new UpdateFormFieldOperationHandler().Apply(context, new DocumentOperation
        {
            Type = FieldsCapabilityPack.UpdateFormField,
            FieldName = "Missing"
        }, 1));
    }

    [Fact]
    public void SetHeaderFooterUpdatesNeutralModel()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new SetHeaderFooterOperationHandler();

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = HeaderFooterCapabilityPack.SetHeaderFooter,
            HeaderFooterType = "header",
            Text = "Invoice Header"
        }, 0);

        handler.Apply(context, new DocumentOperation
        {
            Type = HeaderFooterCapabilityPack.SetHeaderFooter,
            HeaderFooterType = "footer",
            Text = "Page ",
            IncludePageNumber = true,
            TypeName = "DATE",
            Date = "2026-07-14",
            DateFormat = "yyyy-MM-dd"
        }, 1);

        Assert.Equal("header", document.Sections[0].Header?.Type);
        Assert.Equal("Invoice Header", document.Sections[0].Header?.Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("headerFooter", result.TargetType);
        Assert.Equal("0:header", result.TargetId);
        Assert.Equal("header", result.Metadata["headerFooterType"]);
        Assert.Equal("footer", document.Sections[0].Footer?.Type);
        Assert.Equal("Page {DATE}{PAGE}", document.Sections[0].Footer?.Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("Date", document.Sections[0].Footer?.Blocks[1].Field?.Name);
        Assert.Equal("DATE", document.Sections[0].Footer?.Blocks[1].Field?.Properties["typeName"]);
        Assert.Equal("yyyy-MM-dd", document.Sections[0].Footer?.Blocks[1].Field?.Properties["dateFormat"]);
    }

    [Fact]
    public void SetSectionLayoutUpdatesNeutralModel()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new SetSectionLayoutOperationHandler();

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = SectionCapabilityPack.SetSectionLayout,
            PageSize = "A4",
            Orientation = "landscape",
            Unit = "cm",
            MarginLeft = 2,
            MarginRight = 2,
            MarginTop = 1.5f,
            MarginBottom = 1.5f
        }, 0);

        var layout = document.Sections[0].PageLayout;

        Assert.NotNull(layout);
        Assert.Equal("A4", layout.PageSize);
        Assert.Equal("landscape", layout.Orientation);
        Assert.Equal("pt", layout.Unit);
        Assert.True(layout.PageWidth > layout.PageHeight);
        Assert.Equal(56.7f, layout.MarginLeft!.Value, 1);
        Assert.Equal("section", result.TargetType);
        Assert.Equal(0, result.Metadata["sectionIndex"]);
    }

    [Fact]
    public void InsertSectionBreakUpdatesCurrentNeutralSection()
    {
        var document = CreateDocument();
        var context = new DocumentOperationContext(
            document,
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new InsertSectionBreakOperationHandler();

        var result = handler.Apply(context, new DocumentOperation
        {
            Type = SectionCapabilityPack.InsertSectionBreak
        }, 0);

        Assert.Equal(1, context.CurrentSectionIndex);
        Assert.Equal(2, document.Sections.Count);
        Assert.Equal("section", result.TargetType);
        Assert.Equal("1", result.TargetId);
    }

    [Fact]
    public void AppendTableRejectsEmptyRows()
    {
        var context = new DocumentOperationContext(
            CreateDocument(),
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        var exception = Assert.Throws<ArgumentException>(() => handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable
        }, 0));

        Assert.Contains("rows", exception.Message);
    }

    [Fact]
    public void AppendTableRejectsEmptyCellsInARow()
    {
        var context = new DocumentOperationContext(
            CreateDocument(),
            new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase));
        var handler = new AppendTableOperationHandler();

        var exception = Assert.Throws<ArgumentException>(() => handler.Apply(context, new DocumentOperation
        {
            Type = TableCapabilityPack.AppendTable,
            Rows =
            [
                []
            ]
        }, 0));

        Assert.Contains("at least one cell", exception.Message);
    }

    [Fact]
    public void DocumentModelCompilerCreatesOperationsFromNeutralDocument()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Id = "doc-1",
                Styles =
                [
                    new Style
                    {
                        Name = "Title",
                        Type = "paragraph",
                        Text = new TextStyleDefinition
                        {
                            Name = "Title",
                            FontName = "Arial",
                            FontSize = 24,
                            FontSizeUnit = "pt",
                            Bold = true
                        }
                    }
                ],
                Sections =
                [
                    new Section
                    {
                        Id = "section-1",
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "paragraph",
                                Paragraph = new Paragraph
                                {
                                    Id = "paragraph-1",
                                    StyleName = "Title",
                                    Runs =
                                    [
                                        new Run { Id = "run-1", Text = "Model First" }
                                    ]
                                }
                            },
                            new DocumentBlock
                            {
                                Type = "table",
                                Table = new Table
                                {
                                    Id = "10",
                                    Rows =
                                    [
                                        new TableRow
                                        {
                                            Id = "row-1",
                                            Cells =
                                            [
                                                CreateTextCell("cell-1", "country"),
                                                CreateTextCell("cell-2", "sales")
                                            ]
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        };

        var compiled = DocumentModelOperationCompiler.Compile(request);

        Assert.Equal(3, compiled.Operations.Count);
        Assert.Equal(BasicTextCapabilityPack.DefineStyle, compiled.Operations[0].Type);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, compiled.Operations[1].Type);
        Assert.Equal(TableCapabilityPack.AppendTable, compiled.Operations[2].Type);
        Assert.Equal("Model First", compiled.Operations[1].Text);
        Assert.Single(compiled.Operations[1].Runs);
        Assert.Equal("Model First", compiled.Operations[1].Runs[0].Text);
        Assert.Equal("sales", compiled.Operations[2].Rows[0][1]);
    }

    [Fact]
    public void DocumentModelCompilerAppliesConfiguredDefaultsToPlainModel()
    {
        var request = new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = new Document
            {
                Id = "defaults-doc",
                Title = "Defaults Report",
                Sections =
                [
                    new Section
                    {
                        Id = "section-1",
                        Header = new HeaderFooter
                        {
                            Type = "header",
                            Blocks =
                            [
                                new DocumentBlock
                                {
                                    Type = "paragraph",
                                    Paragraph = new Paragraph
                                    {
                                        Runs = [new Run { Text = "Document Header" }]
                                    }
                                }
                            ]
                        },
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "paragraph",
                                Paragraph = new Paragraph
                                {
                                    Runs = [new Run { Text = "Plain body paragraph." }]
                                }
                            },
                            new DocumentBlock
                            {
                                Type = "table",
                                Table = new Table
                                {
                                    Rows =
                                    [
                                        new TableRow
                                        {
                                            Cells =
                                            [
                                                CreateTextCell("cell-1", "Name"),
                                                CreateTextCell("cell-2", "Value")
                                            ]
                                        },
                                        new TableRow
                                        {
                                            Cells =
                                            [
                                                CreateTextCell("cell-3", "North"),
                                                CreateTextCell("cell-4", "42")
                                            ]
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        };
        var options = new DocumentAutomationOptions
        {
            DefaultParagraphStyleName = "Body",
            StyleRoles = new StyleRoleDefinition
            {
                Title = "Title",
                Body = "Body"
            },
            TableStylePresets =
            [
                new TableStylePresetDefinition
                {
                    Name = "Professional Blue"
                }
            ]
        };

        var operations = DocumentModelOperationCompiler.Compile(request, options).Operations;

        Assert.Equal(HeaderFooterCapabilityPack.SetHeaderFooter, operations[0].Type);
        Assert.Equal("Body", operations[0].StyleName);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operations[1].Type);
        Assert.Equal("Defaults Report", operations[1].Text);
        Assert.Equal("Title", operations[1].StyleName);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operations[2].Type);
        Assert.Equal("Body", operations[2].StyleName);
        Assert.Equal(TableCapabilityPack.AppendTable, operations[3].Type);
        Assert.Equal("10", operations[3].TableId);
        Assert.Equal("Professional Blue", operations[3].StyleName);
        Assert.Equal(4, operations.Count);
    }

    [Fact]
    public void DocumentModelCompilerPreservesSimpleTableCellStylesAsFormatOperations()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Sections =
                [
                    new Section
                    {
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "table",
                                Table = new Table
                                {
                                    Id = "10",
                                    Rows =
                                    [
                                        new TableRow
                                        {
                                            Cells =
                                            [
                                                new TableCell
                                                {
                                                    CellStyle = new CellStyleDefinition
                                                    {
                                                        BackgroundColorHex = "#FFC0CB"
                                                    },
                                                    Blocks =
                                                    [
                                                        new DocumentBlock
                                                        {
                                                            Type = "paragraph",
                                                            Paragraph = new Paragraph
                                                            {
                                                                Runs =
                                                                [
                                                                    new Run
                                                                    {
                                                                        Text = "Key",
                                                                        Style = new TextStyleDefinition
                                                                        {
                                                                            FontSize = 20,
                                                                            FontSizeUnit = "pt"
                                                                        }
                                                                    }
                                                                ]
                                                            }
                                                        }
                                                    ]
                                                }
                                            ]
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        };
        var options = new DocumentAutomationOptions
        {
            TableStylePresets = [new TableStylePresetDefinition { Name = "Professional Blue" }]
        };

        var compiled = DocumentModelOperationCompiler.CompileDetailed(request, options);

        Assert.Empty(compiled.Warnings);
        Assert.Equal(TableCapabilityPack.AppendTable, compiled.Request.Operations[0].Type);
        Assert.Equal("Professional Blue", compiled.Request.Operations[0].StyleName);
        Assert.Equal(TableCapabilityPack.FormatTableCell, compiled.Request.Operations[1].Type);
        Assert.Equal("#FFC0CB", compiled.Request.Operations[1].CellStyle?.BackgroundColorHex);
        Assert.Equal(20, compiled.Request.Operations[1].Style?.FontSize);
        Assert.Equal("pt", compiled.Request.Operations[1].Style?.FontSizeUnit);
    }

    [Fact]
    public void DocumentModelCompilerWarnsForUnsupportedRichTableCellContent()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Sections =
                [
                    new Section
                    {
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "table",
                                Table = new Table
                                {
                                    Id = "10",
                                    Rows =
                                    [
                                        new TableRow
                                        {
                                            Cells =
                                            [
                                                new TableCell
                                                {
                                                    ColumnSpan = 2,
                                                    Blocks =
                                                    [
                                                        new DocumentBlock
                                                        {
                                                            Type = "paragraph",
                                                            Paragraph = new Paragraph
                                                            {
                                                                Runs =
                                                                [
                                                                    new Run { Text = "Plain " },
                                                                    new Run
                                                                    {
                                                                        Text = "styled",
                                                                        Style = new TextStyleDefinition { Bold = true }
                                                                    }
                                                                ]
                                                            }
                                                        },
                                                        new DocumentBlock
                                                        {
                                                            Type = "field",
                                                            Field = new Field
                                                            {
                                                                Type = "merge",
                                                                Name = "CustomerName"
                                                            }
                                                        }
                                                    ]
                                                }
                                            ]
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        };

        var compiled = DocumentModelOperationCompiler.CompileDetailed(request);

        Assert.Contains(compiled.Warnings, warning => warning.Contains("columnSpan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(compiled.Warnings, warning => warning.Contains("block type 'field'", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(compiled.Warnings, warning => warning.Contains("mixed styled and unstyled runs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(compiled.Request.Operations, operation => operation.Type == TableCapabilityPack.FormatTableCell);
    }

    [Fact]
    public void DocumentModelCompilerCreatesMergeFieldOperations()
    {
        var request = new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = new Document
            {
                Id = "field-doc",
                Sections =
                [
                    new Section
                    {
                        Id = "section-1",
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "field",
                                Field = new Field
                                {
                                    Id = "field-1",
                                    Type = "merge",
                                    Name = "CustomerName",
                                    Value = "Customer Name"
                                }
                            }
                        ]
                    }
                ]
            }
        };

        var operation = Assert.Single(DocumentModelOperationCompiler.Compile(request).Operations);
        Assert.Equal(FieldsCapabilityPack.AppendMergeField, operation.Type);
        Assert.Equal("field-1", operation.FieldId);
        Assert.Equal("CustomerName", operation.FieldName);
        Assert.Equal("Customer Name", operation.FieldText);
    }

    [Fact]
    public void DocumentModelCompilerCreatesHeaderFooterOperations()
    {
        var request = new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = new Document
            {
                Id = "header-footer-doc",
                Sections =
                [
                    new Section
                    {
                        Id = "section-1",
                        Header = new HeaderFooter
                        {
                            Type = "header",
                            Blocks =
                            [
                                new DocumentBlock
                                {
                                    Type = "paragraph",
                                    Paragraph = new Paragraph
                                    {
                                        Runs = [new Run { Text = "Header Text" }]
                                    }
                                }
                            ]
                        },
                        Footer = new HeaderFooter
                        {
                            Type = "footer",
                            Blocks =
                            [
                                new DocumentBlock
                                {
                                    Type = "paragraph",
                                    Paragraph = new Paragraph
                                    {
                                        Runs = [new Run { Text = "Page {PAGE}" }]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        var operations = DocumentModelOperationCompiler.Compile(request).Operations;

        Assert.Equal(2, operations.Count);
        Assert.Equal(HeaderFooterCapabilityPack.SetHeaderFooter, operations[0].Type);
        Assert.Equal("header", operations[0].HeaderFooterType);
        Assert.Equal("Header Text", operations[0].Text);
        Assert.Equal(HeaderFooterCapabilityPack.SetHeaderFooter, operations[1].Type);
        Assert.True(operations[1].IncludePageNumber);
    }

    [Fact]
    public void DocumentModelCompilerCreatesHeaderFooterImageOperations()
    {
        var request = new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = new Document
            {
                Id = "header-footer-image-doc",
                Sections =
                [
                    new Section
                    {
                        Header = new HeaderFooter
                        {
                            Type = "header",
                            Blocks =
                            [
                                new DocumentBlock
                                {
                                    Type = "paragraph",
                                    Paragraph = new Paragraph
                                    {
                                        Runs = [new Run { Text = "Header Text" }]
                                    }
                                },
                                new DocumentBlock
                                {
                                    Type = "image",
                                    Image = new Image
                                    {
                                        Source = "data:image/png;base64,iVBORw0KGgo=",
                                        AltText = "Header logo",
                                        HorizontalScaling = 40
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        var operations = DocumentModelOperationCompiler.Compile(request).Operations;

        Assert.Equal(2, operations.Count);
        Assert.Equal(HeaderFooterCapabilityPack.SetHeaderFooter, operations[0].Type);
        Assert.Equal(MediaCapabilityPack.AppendImage, operations[1].Type);
        Assert.Equal("header", operations[1].Target);
        Assert.Equal("Header logo", operations[1].AltText);
        Assert.Equal(40, operations[1].HorizontalScaling);
    }

    [Fact]
    public void DocumentModelCompilerCreatesSectionLayoutOperation()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Sections =
                [
                    new Section
                    {
                        PageLayout = new PageLayoutDefinition
                        {
                            PageSize = "Letter",
                            Orientation = "portrait",
                            Unit = "in",
                            MarginLeft = 1,
                            MarginRight = 1
                        },
                        Blocks =
                        [
                            CreateTextBlock("Body")
                        ]
                    }
                ]
            }
        };

        var operations = DocumentModelOperationCompiler.Compile(request).Operations;

        Assert.Equal(SectionCapabilityPack.SetSectionLayout, operations[0].Type);
        Assert.Equal("Letter", operations[0].PageLayout?.PageSize);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operations[1].Type);
    }

    [Fact]
    public void DocumentModelCompilerCreatesSectionBreaksForMultipleSections()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Sections =
                [
                    new Section
                    {
                        PageLayout = new PageLayoutDefinition
                        {
                            PageSize = "A5",
                            Orientation = "landscape"
                        },
                        Blocks = [CreateTextBlock("First")]
                    },
                    new Section
                    {
                        PageLayout = new PageLayoutDefinition
                        {
                            PageSize = "A5",
                            Orientation = "portrait"
                        },
                        Blocks = [CreateTextBlock("Second")]
                    }
                ]
            }
        };

        var operations = DocumentModelOperationCompiler.Compile(request).Operations;

        Assert.Equal(5, operations.Count);
        Assert.Equal(SectionCapabilityPack.SetSectionLayout, operations[0].Type);
        Assert.Equal(0, operations[0].SectionIndex);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operations[1].Type);
        Assert.Equal(SectionCapabilityPack.InsertSectionBreak, operations[2].Type);
        Assert.Equal(SectionCapabilityPack.SetSectionLayout, operations[3].Type);
        Assert.Equal(1, operations[3].SectionIndex);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operations[4].Type);
    }

    [Fact]
    public void DocumentModelCompilerCombinesMultipleParagraphsInTableCell()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Id = "doc-1",
                Sections =
                [
                    new Section
                    {
                        Id = "section-1",
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "table",
                                Table = new Table
                                {
                                    Id = "10",
                                    Rows =
                                    [
                                        new TableRow
                                        {
                                            Id = "row-1",
                                            Cells =
                                            [
                                                new TableCell
                                                {
                                                    Id = "cell-1",
                                                    Blocks =
                                                    [
                                                        CreateTextBlock("first"),
                                                        CreateTextBlock("second")
                                                    ]
                                                }
                                            ]
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        };

        var compiled = DocumentModelOperationCompiler.Compile(request);

        var operation = Assert.Single(compiled.Operations);
        Assert.Equal(TableCapabilityPack.AppendTable, operation.Type);
        Assert.Equal("first\nsecond", operation.Rows[0][0]);
    }

    [Fact]
    public void DocumentModelCompilerPreservesInlineRunStyles()
    {
        var request = new RenderDocumentModelRequest
        {
            Document = new Document
            {
                Id = "doc-1",
                Sections =
                [
                    new Section
                    {
                        Id = "section-1",
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "paragraph",
                                Paragraph = new Paragraph
                                {
                                    Id = "paragraph-1",
                                    Runs =
                                    [
                                        new Run { Id = "run-1", Text = "hello " },
                                        new Run
                                        {
                                            Id = "run-2",
                                            Text = "jon",
                                            Style = new TextStyleDefinition
                                            {
                                                Bold = true
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                ]
            }
        };

        var compiled = DocumentModelOperationCompiler.Compile(request);

        var operation = Assert.Single(compiled.Operations);
        Assert.Equal(BasicTextCapabilityPack.AppendParagraph, operation.Type);
        Assert.Equal("hello jon", operation.Text);
        Assert.Equal(2, operation.Runs.Count);
        Assert.True(operation.Runs[1].Style?.Bold);
    }

    [Fact]
    public void FormatTextOccurrencesUpdatesNeutralDocumentRunsWithoutOffsets()
    {
        var document = CreateDocument();
        document.Sections[0].Blocks.Add(new DocumentBlock
        {
            Type = "paragraph",
            Paragraph = new Paragraph
            {
                Id = "paragraph-1",
                Runs =
                [
                    new Run
                    {
                        Id = "run-1",
                        Text = "jon and Jon wrote to jonathan"
                    }
                ]
            }
        });

        var count = TextOccurrenceUtilities.FormatModelOccurrences(
            document,
            "jon",
            matchCase: false,
            wholeWord: true,
            maxOccurrences: null,
            new TextStyleDefinition { Bold = true });

        var runs = document.Sections[0].Blocks[0].Paragraph!.Runs;

        Assert.Equal(2, count);
        Assert.Equal("jon", runs[0].Text);
        Assert.True(runs[0].Style?.Bold);
        Assert.Equal(" and ", runs[1].Text);
        Assert.Equal("Jon", runs[2].Text);
        Assert.True(runs[2].Style?.Bold);
        Assert.Equal(" wrote to jonathan", runs[3].Text);
        Assert.Null(runs[3].Style?.Bold);
    }

    [Fact]
    public void ReplaceTextUpdatesNeutralDocumentRunsWithoutOffsets()
    {
        var document = CreateDocument();
        document.Sections[0].Blocks.Add(new DocumentBlock
        {
            Type = "paragraph",
            Paragraph = new Paragraph
            {
                Id = "paragraph-1",
                Runs =
                [
                    new Run
                    {
                        Id = "run-1",
                        Text = "draft draft drafting"
                    }
                ]
            }
        });

        var count = TextOccurrenceUtilities.ReplaceModelOccurrences(
            document,
            "draft",
            "final",
            matchCase: false,
            wholeWord: true,
            maxOccurrences: 1);

        Assert.Equal(1, count);
        Assert.Equal("final draft drafting", document.Sections[0].Blocks[0].Paragraph?.Runs[0].Text);
    }


    [Fact]
    public void NeutralDocumentModelRepresentsCoreDocumentElements()
    {
        var document = new Document
        {
            Id = "doc-1",
            Title = "Sample",
            Styles =
            [
                new Style
                {
                    Name = "Heading",
                    Type = "paragraph",
                    Text = new TextStyleDefinition
                    {
                        Name = "Heading",
                        FontName = "Arial"
                    }
                }
            ],
            Sections =
            [
                new Section
                {
                    Id = "section-1",
                    PageLayout = new PageLayoutDefinition
                    {
                        PageSize = "A4",
                        Orientation = "portrait"
                    },
                    Header = new HeaderFooter
                    {
                        Type = "default"
                    },
                    Footer = new HeaderFooter
                    {
                        Type = "default"
                    },
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Type = "paragraph",
                            Paragraph = new Paragraph
                            {
                                Id = "paragraph-1",
                                Runs =
                                [
                                    new Run
                                    {
                                        Id = "run-1",
                                        Text = "Hello"
                                    }
                                ]
                            }
                        },
                        new DocumentBlock
                        {
                            Type = "table",
                            Table = new Table
                            {
                                Id = "table-1",
                                Rows =
                                [
                                    new TableRow
                                    {
                                        Id = "row-1",
                                        Cells =
                                        [
                                            new TableCell
                                            {
                                                Id = "cell-1"
                                            }
                                        ]
                                    }
                                ]
                            }
                        },
                        new DocumentBlock
                        {
                            Type = "image",
                            Image = new Image
                            {
                                Id = "image-1",
                                Source = "data:image/png;base64,..."
                            }
                        },
                        new DocumentBlock
                        {
                            Type = "field",
                            Field = new Field
                            {
                                Id = "field-1",
                                Type = "merge",
                                Name = "CustomerName"
                            }
                        }
                    ]
                }
            ]
        };

        Assert.Equal("Sample", document.Title);
        Assert.Single(document.Styles);
        Assert.Equal(4, document.Sections[0].Blocks.Count);
        Assert.Equal("A4", document.Sections[0].PageLayout?.PageSize);
        Assert.NotNull(document.Sections[0].Header);
        Assert.NotNull(document.Sections[0].Footer);
        Assert.NotNull(document.Sections[0].Blocks[0].Paragraph);
        Assert.NotNull(document.Sections[0].Blocks[1].Table);
        Assert.NotNull(document.Sections[0].Blocks[2].Image);
        Assert.NotNull(document.Sections[0].Blocks[3].Field);
    }

    private static Document CreateDocument()
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Sections =
            [
                new Section
                {
                    Id = Guid.NewGuid().ToString("N")
                }
            ]
        };

    [Fact]
    public void CompilerRepairsAndStylesWeakModelInvoicePayload()
    {
        var header = new TableRow
        {
            Cells =
            [
                CreateTextCell("h1", "Description"),
                CreateTextCell("h2", "Quantity"),
                CreateTextCell("h3", "Unit Price"),
                CreateTextCell("h4", "Amount")
            ]
        };
        var document = new Document
        {
            Title = "INVOICE",
            Sections =
            [
                new Section
                {
                    Blocks =
                    [
                        CreateTextBlock("Invoice Number: INV-1001"),
                        CreateTextBlock("Date: September 4, 2026"),
                        new DocumentBlock
                        {
                            Type = "table",
                            Table = new Table { Rows = [header] }
                        },
                        CreateTextBlock("Professional Consulting | 40 | $150.00 | $6,000.00"),
                        CreateTextBlock("Implementation Support | 12 | $125.00 | $1,500.00"),
                        CreateTextBlock("Training | 4 | $100.00 | $400.00"),
                        CreateTextBlock("Subtotal: $7,900.00"),
                        CreateTextBlock("Tax (8%): $632.00"),
                        CreateTextBlock("Total: $8,532.00"),
                        CreateTextBlock("Payment Terms: Due within 30 days")
                    ]
                }
            ]
        };
        var options = new DocumentAutomationOptions
        {
            DefaultPageLayout = new PageLayoutDefinition
            {
                PageSize = "Letter",
                Unit = "in",
                MarginLeft = 0.8f,
                MarginRight = 0.8f
            },
            StyleRoles = new StyleRoleDefinition
            {
                Title = "Title",
                Heading1 = "Heading",
                Heading2 = "Heading2",
                Body = "Body"
            },
            TableStylePresets = [new TableStylePresetDefinition { Name = "Professional Blue" }]
        };

        var compiled = DocumentModelOperationCompiler.CompileDetailed(
            new RenderDocumentModelRequest { CreateIfMissing = true, Document = document },
            options);

        Assert.Contains(compiled.Warnings, warning => warning.Contains("Recovered 3", StringComparison.Ordinal));
        Assert.Contains(compiled.Warnings, warning => warning.Contains("invoice quality profile", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, document.Sections[0].Blocks.Single(block => block.Table is not null).Table!.Rows.Count);
        Assert.DoesNotContain(document.Sections[0].Blocks, block =>
            block.Paragraph?.Text?.Contains('|', StringComparison.Ordinal) == true);

        var layout = compiled.Request.Operations.First();
        Assert.Equal(SectionCapabilityPack.SetSectionLayout, layout.Type);
        Assert.Equal("Letter", layout.PageLayout?.PageSize);

        var tableOperation = compiled.Request.Operations.Single(operation => operation.Type == TableCapabilityPack.AppendTable);
        Assert.Equal(4, tableOperation.Rows.Count);
        Assert.Equal(new float?[] { 255, 68, 84, 90 }, tableOperation.ColumnWidths);
        Assert.Equal("pt", tableOperation.ColumnWidthUnit);

        var totalOperation = compiled.Request.Operations.Single(operation =>
            operation.Type == BasicTextCapabilityPack.AppendParagraph
            && operation.Text == "Total: $8,532.00");
        Assert.Equal("right", totalOperation.Paragraph?.Alignment);

        Assert.Contains(compiled.Request.Operations, operation =>
            operation.Type == BasicTextCapabilityPack.AppendParagraph
            && operation.Text == "Payment Terms"
            && operation.StyleName == "Heading2");
    }

    [Fact]
    public void CompilerMapsSemanticParagraphRoleAndPreservesExplicitParagraphFormatting()
    {
        var block = CreateTextBlock("Payment Terms");
        block.Paragraph!.Role = "heading2";
        block.Paragraph.Alignment = "center";
        var options = new DocumentAutomationOptions
        {
            StyleRoles = new StyleRoleDefinition { Heading2 = "Subheading", Body = "Body" }
        };

        var operation = Assert.Single(DocumentModelOperationCompiler.Compile(
            new RenderDocumentModelRequest
            {
                Document = new Document { Sections = [new Section { Blocks = [block] }] }
            },
            options).Operations);

        Assert.Equal("Subheading", operation.StyleName);
        Assert.Equal("center", operation.Paragraph?.Alignment);
    }

    [Fact]
    public void CompilerMergesExplicitPageSizeWithUnspecifiedDefaultLayoutProperties()
    {
        var options = new DocumentAutomationOptions
        {
            DefaultPageLayout = new PageLayoutDefinition
            {
                PageSize = "Letter",
                Orientation = "portrait",
                Unit = "in",
                MarginLeft = 0.8f,
                MarginRight = 0.8f,
                MarginTop = 0.75f,
                MarginBottom = 0.75f
            }
        };
        var document = new Document
        {
            Sections =
            [
                new Section
                {
                    PageLayout = new PageLayoutDefinition { PageSize = "A4" },
                    Blocks = [CreateTextBlock("Body")]
                }
            ]
        };

        var layout = DocumentModelOperationCompiler.Compile(
            new RenderDocumentModelRequest { Document = document },
            options).Operations.First(operation => operation.Type == SectionCapabilityPack.SetSectionLayout).PageLayout;

        Assert.NotNull(layout);
        Assert.Equal("A4", layout.PageSize);
        Assert.Equal("portrait", layout.Orientation);
        Assert.Equal("in", layout.Unit);
        Assert.Equal(0.8f, layout.MarginLeft);
        Assert.Equal(0.8f, layout.MarginRight);
        Assert.Equal(0.75f, layout.MarginTop);
        Assert.Equal(0.75f, layout.MarginBottom);
    }

    [Fact]
    public void CompilerKeepsExplicitElementStyleAndDefaultsUnstyledBodyElements()
    {
        var explicitlyStyled = CreateTextBlock("Explicit heading");
        explicitlyStyled.Paragraph!.StyleName = "Customer Heading";
        var options = new DocumentAutomationOptions
        {
            DefaultParagraphStyleName = "Body",
            StyleRoles = new StyleRoleDefinition { Body = "Body" }
        };

        var operations = DocumentModelOperationCompiler.Compile(
            new RenderDocumentModelRequest
            {
                Document = new Document
                {
                    Sections =
                    [
                        new Section
                        {
                            Blocks = [explicitlyStyled, CreateTextBlock("Unstyled body")]
                        }
                    ]
                }
            },
            options).Operations.Where(operation => operation.Type == BasicTextCapabilityPack.AppendParagraph).ToList();

        Assert.Equal("Customer Heading", operations[0].StyleName);
        Assert.Equal("Body", operations[1].StyleName);
    }

    [Fact]
    public void CompilerRejectsMalformedCompletedInvoiceRows()
    {
        var table = new Table
        {
            Rows =
            [
                new TableRow
                {
                    Cells =
                    [
                        CreateTextCell("h1", "Description"),
                        CreateTextCell("h2", "Quantity"),
                        CreateTextCell("h3", "Unit Price"),
                        CreateTextCell("h4", "Amount")
                    ]
                },
                new TableRow
                {
                    Cells =
                    [
                        CreateTextCell("r1c1", "Professional Services"),
                        CreateTextCell("r1c2", ", 150.00"),
                        CreateTextCell("r1c3", ", 2250.00"),
                        CreateTextCell("r1c4", "")
                    ]
                }
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => DocumentModelOperationCompiler.Compile(
            new RenderDocumentModelRequest
            {
                Document = new Document
                {
                    Title = "INVOICE",
                    Sections = [new Section { Blocks = [new DocumentBlock { Type = "table", Table = table }] }]
                }
            }));

        Assert.Contains("description, quantity, unit price, amount", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompilerRejectsParagraphOnlyInvoiceAndCorrectsFalseTitle()
    {
        var firstLineItem = CreateTextBlock("Line Item 1: Web Development Services");
        firstLineItem.Paragraph!.StyleName = "Title";
        var document = new Document
        {
            Sections =
            [
                new Section
                {
                    Blocks =
                    [
                        firstLineItem,
                        CreateTextBlock("Line Item 2: UI/UX Design"),
                        CreateTextBlock("Line Item 3: Server Setup and Configuration"),
                        CreateTextBlock("Subtotal: $3,500.00"),
                        CreateTextBlock("Tax (10%): $350.00"),
                        CreateTextBlock("Total: $3,850.00"),
                        CreateTextBlock("Payment Terms: Net 30 days")
                    ]
                }
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => DocumentModelOperationCompiler.Compile(
            new RenderDocumentModelRequest { CreateIfMissing = true, Document = document }));

        Assert.Contains("real line-item table", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("INVOICE", document.Title);
        Assert.Null(firstLineItem.Paragraph.StyleName);
        Assert.Equal("body", firstLineItem.Paragraph.Role);
    }

    private static void AddSampleTable(Document document, string id)
        => document.Sections[0].Blocks.Add(new DocumentBlock
        {
            Type = "table",
            Table = new Table
            {
                Id = id,
                Rows =
                [
                    new TableRow
                    {
                        Id = $"{id}:r1",
                        Cells =
                        [
                            CreateTextCell($"{id}:r1c1", "country"),
                            CreateTextCell($"{id}:r1c2", "sales")
                        ]
                    },
                    new TableRow
                    {
                        Id = $"{id}:r2",
                        Cells =
                        [
                            CreateTextCell($"{id}:r2c1", "Germany"),
                            CreateTextCell($"{id}:r2c2", "$842,000")
                        ]
                    }
                ]
            }
        });

    private static TableCell CreateTextCell(string id, string text)
        => new()
        {
            Id = id,
            Blocks =
            [
                CreateTextBlock(text)
            ]
        };

    private static DocumentBlock CreateTextBlock(string text)
        => new()
        {
            Type = "paragraph",
            Paragraph = new Paragraph
            {
                Id = Guid.NewGuid().ToString("N"),
                Runs =
                [
                    new Run
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Text = text
                    }
                ]
            }
        };

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(testsDirectory))
        {
            return Directory.GetParent(testsDirectory)?.FullName
                ?? throw new InvalidOperationException("Could not locate repository root.");
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

}
