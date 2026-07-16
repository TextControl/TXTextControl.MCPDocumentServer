using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Drawing;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;
using TXTextControl;
using Xunit;
using Neutral = TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Tests;

public sealed class DocumentCreationIntegrationTests
{
    [SkippableFact]
    public void ApplyOperationsCreatesAndStoresActualDocuments()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var exportRoot = Path.Combine(artifactRoot, "Exports");
        Directory.CreateDirectory(exportRoot);

        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    PageSize = "Letter",
                    Unit = "in",
                    MarginLeft = 1,
                    MarginRight = 1,
                    MarginTop = 1,
                    MarginBottom = 1
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition
                    {
                        Name = "Heading",
                        FontName = "Liberation Sans",
                        FontSize = 20,
                        FontSizeUnit = "px",
                        Bold = true
                    }
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition
                    {
                        Name = "Body",
                        FontName = "Open Sans",
                        FontSize = 12,
                        FontSizeUnit = "px",
                        Bold = false
                    }
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    StyleName = "Heading",
                    Text = "This is my title"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    StyleName = "Body",
                    Text = "This is an integration-test document created by the MCP document workflow."
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "10",
                    Rows =
                    [
                        ["Quarter", "Revenue"],
                        ["Q1", "$1.2M"],
                        ["Q2", "$1.5M"]
                    ]
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");
        var statePath = Path.Combine(sessionRoot, "document.state.json");

        Assert.True(File.Exists(txPath), $"Expected TX document at {txPath}");
        Assert.True(new FileInfo(txPath).Length > 0);
        Assert.True(File.Exists(statePath), $"Expected document state at {statePath}");
        AssertTxParagraphStyles(txPath, 300);
        AssertTxTable(txPath);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(exportRoot, "basic-text-document.docx");
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        var html = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "html"
        });
        var htmlPath = Path.Combine(exportRoot, "basic-text-document.html");
        File.WriteAllBytes(htmlPath, Convert.FromBase64String(html.Base64Document));

        Assert.True(File.Exists(docxPath), $"Expected DOCX export at {docxPath}");
        Assert.True(new FileInfo(docxPath).Length > 0);
        Assert.True(File.Exists(htmlPath), $"Expected HTML export at {htmlPath}");
        Assert.True(new FileInfo(htmlPath).Length > 0);

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal(3, model.Sections[0].Blocks.Count);
        Assert.Equal("This is my title", model.Sections[0].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("Revenue", model.Sections[0].Blocks[2].Table?.Rows[0].Cells[1].Blocks[0].Paragraph?.Runs[0].Text);

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = response.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition
                    {
                        Name = "Heading",
                        FontName = "Arial",
                        FontSize = 40,
                        FontSizeUnit = "px",
                        Bold = true
                    }
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.ApplyStyleToParagraph,
                    StyleName = "Heading",
                    ParagraphIndex = 0
                }
            ]
        });

        AssertTxParagraphStyles(txPath, 600);
    }

    [SkippableFact]
    public void ApplyOperationsCanInsertImagesFromPathAndBase64()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var exportRoot = Path.Combine(artifactRoot, "Exports");
        Directory.CreateDirectory(exportRoot);
        Directory.CreateDirectory(artifactRoot);

        var imageBytes = CreateSamplePngBytes();
        var imagePath = Path.Combine(artifactRoot, "sample-image.png");
        File.WriteAllBytes(imagePath, imageBytes);

        var workflow = CreateWorkflow(artifactRoot);
        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Image insertion test"
                },
                new DocumentOperation
                {
                    Type = MediaCapabilityPack.AppendImage,
                    ImagePath = imagePath,
                    AltText = "Path image",
                    HorizontalScaling = 75,
                    VerticalScaling = 75,
                    Alignment = "centered",
                    InsertionMode = "displaceText"
                },
                new DocumentOperation
                {
                    Type = MediaCapabilityPack.AppendImage,
                    ImageBase64 = "data:image/png;base64," + Convert.ToBase64String(imageBytes),
                    AltText = "Base64 image",
                    HorizontalScaling = 50,
                    VerticalScaling = 50
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");
        AssertTxImageCount(txPath, 2);

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal(3, model.Sections[0].Blocks.Count);
        Assert.Equal("Path image", model.Sections[0].Blocks[1].Image?.AltText);
        Assert.Equal("Base64 image", model.Sections[0].Blocks[2].Image?.AltText);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(exportRoot, "image-insertion-document.docx");
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxContainsMedia(docxPath);
    }

    [SkippableFact]
    public void RenderDocumentModelCreatesActualDocumentAndPreservesNeutralModel()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var document = new Document
        {
            Id = "model-doc",
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
                    new Neutral.Section
                {
                    Id = "section-1",
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Type = "paragraph",
                            Paragraph = new Neutral.Paragraph
                            {
                                Id = "paragraph-1",
                                StyleName = "Title",
                                Runs =
                                [
                                    new Run { Id = "run-1", Text = "Model First Report" }
                                ]
                            }
                        },
                        new DocumentBlock
                        {
                            Type = "table",
                            Table = new Neutral.Table
                            {
                                Id = "11",
                                Rows =
                                [
                                    new Neutral.TableRow
                                    {
                                        Id = "row-1",
                                        Cells =
                                        [
                                            CreateTextCell("cell-1", "country"),
                                            CreateTextCell("cell-2", "sales")
                                        ]
                                    },
                                    new Neutral.TableRow
                                    {
                                        Id = "row-2",
                                        Cells =
                                        [
                                            CreateTextCell("cell-3", "Germany"),
                                            CreateTextCell("cell-4", "$842,000")
                                        ]
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        };

        var response = RenderDocumentModelOrSkipIfUnlicensed(
            workflow,
            new RenderDocumentModelRequest
            {
                CreateIfMissing = true,
                Document = document
            });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        Assert.True(File.Exists(txPath), $"Expected TX document at {txPath}");
        AssertTxTable(txPath, 11, "$842,000");

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("model-doc", model.Id);
        Assert.Equal("Model First Report", model.Sections[0].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("Germany", model.Sections[0].Blocks[1].Table?.Rows[1].Cells[0].Blocks[0].Paragraph?.Runs[0].Text);
    }

    [SkippableFact]
    public void DocumentInspectionToolsReturnModelAwareSummaries()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition
                    {
                        Name = "InvoiceTitle",
                        FontName = "Arial",
                        FontSize = 28,
                        FontSizeUnit = "pt",
                        Bold = true
                    }
                },
                new DocumentOperation
                {
                    Type = HeaderFooterCapabilityPack.SetHeaderFooter,
                    HeaderFooterType = "header",
                    Text = "Invoice Header"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    StyleName = "InvoiceTitle",
                    Text = "Invoice"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "20",
                    Rows =
                    [
                        ["Product", "Price"],
                        ["", "$42.00"]
                    ]
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "ProductName",
                    FieldText = "Product Name",
                    TableId = "20",
                    RowIndex = 1,
                    ColumnIndex = 0,
                    Placement = "replace"
                }
            ]
        });

        var structure = workflow.GetDocumentStructure(response.SessionId);
        Assert.Equal(response.SessionId, structure.SessionId);
        Assert.Equal(1, structure.SectionCount);
        Assert.Equal(1, structure.ParagraphCount);
        Assert.Equal(1, structure.TableCount);
        Assert.Equal(1, structure.FieldCount);
        Assert.NotNull(structure.Sections[0].Header);
        Assert.Contains(structure.Sections[0].Blocks, block => block.Type == "table" && block.TableId == "20");

        var styles = workflow.GetDocumentStyles(response.SessionId);
        var titleStyle = Assert.Single(styles.Styles, style => style.Name == "InvoiceTitle");
        Assert.True(titleStyle.Text?.Bold);

        var tables = workflow.GetDocumentTables(response.SessionId);
        var table = Assert.Single(tables.Tables);
        Assert.Equal("20", table.Id);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(2, table.ColumnCount);
        Assert.Contains("ProductName", table.Rows[1].Cells[0].FieldNames);

        var fields = workflow.GetDocumentFields(response.SessionId);
        var field = Assert.Single(fields.Fields);
        Assert.Equal("ProductName", field.Name);
        Assert.Contains("table.rows[1].cells[0]", field.Location);

        var headersFooters = workflow.GetDocumentHeadersFooters(response.SessionId);
        var header = Assert.Single(headersFooters.HeadersFooters);
        Assert.Equal("header", header.Type);
        Assert.Equal("Invoice Header", header.TextPreview);
    }

    [SkippableFact]
    public void RenderDocumentModelRendersInlineRunFormatting()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var document = new Document
        {
            Id = "inline-run-doc",
            Sections =
            [
                new Neutral.Section
                {
                    Id = "section-1",
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Type = "paragraph",
                            Paragraph = new Neutral.Paragraph
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
        };

        var response = RenderDocumentModelOrSkipIfUnlicensed(
            workflow,
            new RenderDocumentModelRequest
            {
                CreateIfMissing = true,
                Document = document
            });

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "inline-runs-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxHasBoldRun(docxPath, "jon");
    }

    [SkippableFact]
    public void ApplyOperationsCreatesUnevenTableAsRectangularTxTable()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "12",
                    Rows =
                    [
                        ["country", "sales", "qty"],
                        ["Germany", "$842,000"],
                        ["Japan", "$638,500", "940"]
                    ]
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxTableCell(txPath, 12, 1, 3, "qty");
        AssertTxTableCell(txPath, 12, 2, 3, string.Empty);
        AssertTxTableCell(txPath, 12, 3, 3, "940");

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "uneven-table-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxContainsTableText(docxPath, ["country", "sales", "qty", "Germany", "$842,000", "Japan", "$638,500", "940"]);
    }

    [SkippableFact]
    public void ApplyOperationsFormatsOccurrencesWithoutClientOffsets()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "jon met Jon near jonathan"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatTextOccurrences,
                    MatchText = "jon",
                    WholeWord = true,
                    Style = new TextStyleDefinition
                    {
                        Bold = true
                    }
                }
            ]
        });

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "format-occurrences-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxHasBoldRun(docxPath, "jon");
        AssertDocxHasBoldRun(docxPath, "Jon");
        AssertDocxContainsText(docxPath, "jonathan");
    }

    [SkippableFact]
    public void ApplyOperationsReplacesTextWithoutClientOffsets()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "draft draft drafting"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.ReplaceText,
                    MatchText = "draft",
                    ReplacementText = "final",
                    WholeWord = true,
                    MaxOccurrences = 1
                }
            ]
        });

        var text = workflow.GetText(response.SessionId).Text;
        var model = workflow.GetDocumentModel(response.SessionId).Document;

        Assert.Contains("final draft drafting", text);
        Assert.Equal("final draft drafting", model.Sections[0].Blocks[0].Paragraph?.Runs[0].Text);
    }

    [SkippableFact]
    public void RenderDocumentModelCreatesTableFromNeutralCells()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var document = new Document
        {
            Id = "neutral-table-doc",
            Sections =
            [
                new Neutral.Section
                {
                    Id = "section-1",
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Type = "table",
                            Table = new Neutral.Table
                            {
                                Id = "13",
                                Rows =
                                [
                                    new Neutral.TableRow
                                    {
                                        Id = "row-1",
                                        Cells =
                                        [
                                            CreateTextCell("cell-1", "country"),
                                            CreateTextCell("cell-2", "sales"),
                                            CreateTextCell("cell-3", "qty")
                                        ]
                                    },
                                    new Neutral.TableRow
                                    {
                                        Id = "row-2",
                                        Cells =
                                        [
                                            CreateTextCell("cell-4", "Brazil"),
                                            CreateTextCell("cell-5", "$421,750"),
                                            CreateTextCell("cell-6", "705")
                                        ]
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        };

        var response = RenderDocumentModelOrSkipIfUnlicensed(
            workflow,
            new RenderDocumentModelRequest
            {
                CreateIfMissing = true,
                Document = document
            });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxTableCell(txPath, 13, 1, 1, "country");
        AssertTxTableCell(txPath, 13, 2, 2, "$421,750");
        AssertTxTableCell(txPath, 13, 2, 3, "705");

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("neutral-table-doc", model.Id);
        Assert.Equal("Brazil", model.Sections[0].Blocks[0].Table?.Rows[1].Cells[0].Blocks[0].Paragraph?.Runs[0].Text);
    }

    [SkippableFact]
    public void ApplyOperationsEditsFormatsAndExtendsTable()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "14",
                    Rows =
                    [
                        ["country", "sales", "qty"],
                        ["Germany", "$842,000", "1280"]
                    ]
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.SetTableCellText,
                    TableId = "14",
                    RowIndex = 1,
                    ColumnIndex = 1,
                    Text = "$900,000"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AddTableRow,
                    TableId = "14",
                    Rows =
                    [
                        ["Brazil", "$421,750", "705"]
                    ]
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.FormatTableHeaderRow,
                    TableId = "14",
                    Style = new TextStyleDefinition
                    {
                        Bold = true,
                        ColorHex = "#FFFFFF"
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
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.FormatTableColumn,
                    TableId = "14",
                    ColumnIndex = 1,
                    Width = 220,
                    Unit = "pt"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.FormatTableCell,
                    TableId = "14",
                    RowIndex = 2,
                    ColumnIndex = 1,
                    Style = new TextStyleDefinition
                    {
                        Italic = true
                    },
                    CellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#FFF2CC",
                        Border = new CellBorderDefinition
                        {
                            Width = 10,
                            ColorHex = "#C00000"
                        }
                    }
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxTableCell(txPath, 14, 2, 2, "$900,000");
        AssertTxTableCell(txPath, 14, 3, 1, "Brazil");
        AssertTxTableCell(txPath, 14, 3, 2, "$421,750");
        AssertTxTableCell(txPath, 14, 3, 3, "705");
        AssertTxTableCellBackColor(txPath, 14, 1, 1, "#1F4E79");
        AssertTxTableCellBackColor(txPath, 14, 3, 2, "#FFF2CC");
        AssertTxTableCellBottomBorder(txPath, 14, 1, 1, 20, "#000000");
        AssertTxTableCellBorders(txPath, 14, 3, 2, 10, "#C00000");
        AssertTxTableColumnWidth(txPath, 14, 2, 4400);

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("$900,000", model.Sections[0].Blocks[0].Table?.Rows[1].Cells[1].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("Brazil", model.Sections[0].Blocks[0].Table?.Rows[2].Cells[0].Blocks[0].Paragraph?.Runs[0].Text);
        Assert.All(model.Sections[0].Blocks[0].Table!.Rows[0].Cells, cell =>
            Assert.True(cell.Blocks[0].Paragraph?.Runs[0].Style?.Bold));
        Assert.All(model.Sections[0].Blocks[0].Table!.Rows[0].Cells, cell =>
            Assert.Equal("#1F4E79", cell.CellStyle?.BackgroundColorHex));
        Assert.All(model.Sections[0].Blocks[0].Table!.Rows[0].Cells, cell =>
            Assert.Equal(20, cell.CellStyle?.Border?.Bottom?.Width));
        Assert.True(model.Sections[0].Blocks[0].Table?.Rows[2].Cells[1].Blocks[0].Paragraph?.Runs[0].Style?.Italic);
        Assert.Equal("#FFF2CC", model.Sections[0].Blocks[0].Table?.Rows[2].Cells[1].CellStyle?.BackgroundColorHex);
        Assert.Equal(10, model.Sections[0].Blocks[0].Table?.Rows[2].Cells[1].CellStyle?.Border?.Width);
        Assert.Equal("#C00000", model.Sections[0].Blocks[0].Table?.Rows[2].Cells[1].CellStyle?.Border?.ColorHex);
        Assert.Equal(220, model.Sections[0].Blocks[0].Table?.ColumnWidths[1]);
        Assert.Equal("pt", model.Sections[0].Blocks[0].Table?.ColumnWidthUnit);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "table-rich-operations-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxContainsTableText(docxPath, ["country", "sales", "qty", "Germany", "$900,000", "Brazil", "$421,750", "705"]);
        AssertDocxHasBoldRun(docxPath, "country");
        AssertDocxContainsText(docxPath, "1F4E79");
        AssertDocxContainsText(docxPath, "FFF2CC");
        AssertDocxContainsText(docxPath, "C00000");
    }

    [SkippableFact]
    public void ApplyOperationsAppliesConfiguredTableStylePreset()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "15",
                    Rows =
                    [
                        ["Country", "Sales", "Qty"],
                        ["Germany", "$842,000", "1280"],
                        ["Brazil", "$421,750", "705"]
                    ]
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.ApplyTableStylePreset,
                    TableId = "15",
                    StyleName = "Professional Blue"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxTableCellBackColor(txPath, 15, 1, 1, "#1F4E79");
        AssertTxTableCellBackColor(txPath, 15, 2, 1, "#FFFFFF");
        AssertTxTableCellBackColor(txPath, 15, 3, 1, "#F8FAFC");
        AssertTxTableCellBorders(txPath, 15, 1, 1, 10, "#D0D5DD");
        AssertTxTableCellBorders(txPath, 15, 3, 2, 10, "#D0D5DD");
        AssertTxTableCellTextFormat(txPath, 15, 1, 1, expectedFontSize: 200, expectedHex: "#FFFFFF", expectedBold: true);
        AssertTxTableCellTextFormat(txPath, 15, 2, 2, expectedFontSize: 200, expectedHex: "#111827");

        var modelTable = workflow.GetDocumentModel(response.SessionId).Document.Sections[0].Blocks[0].Table;
        Assert.NotNull(modelTable);
        Assert.All(modelTable!.Rows[0].Cells, cell => Assert.True(cell.Blocks[0].Paragraph?.Runs[0].Style?.Bold));
        Assert.All(modelTable.Rows[0].Cells, cell => Assert.Equal("#1F4E79", cell.CellStyle?.BackgroundColorHex));
        Assert.All(modelTable.Rows[1].Cells, cell => Assert.Equal("#FFFFFF", cell.CellStyle?.BackgroundColorHex));
        Assert.All(modelTable.Rows[2].Cells, cell => Assert.Equal("#F8FAFC", cell.CellStyle?.BackgroundColorHex));
    }

    [SkippableFact]
    public void ApplyOperationsCreatesUpdatesAndClearsMergeFields()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Dear "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "CustomerName",
                    FieldText = "Customer Name"
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "Address"
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.UpdateMergeField,
                    FieldName = "Address",
                    FieldText = "Customer Address",
                    Parameters = ["Customer.Address"]
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxMergeField(txPath, "CustomerName", "Customer Name");
        AssertTxMergeField(txPath, "Customer.Address", "Customer Address");

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        var fields = model.Sections[0].Blocks
            .Where(block => string.Equals(block.Type, "field", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Field)
            .ToList();
        Assert.Equal(2, fields.Count);
        Assert.Contains(fields, field => field?.Name == "CustomerName");
        Assert.Contains(fields, field => field?.Name == "Customer.Address" && field.Value == "Customer Address");

        var templateDocx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var templateDocxPath = Path.Combine(artifactRoot, "Exports", "merge-fields-template-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(templateDocxPath)!);
        File.WriteAllBytes(templateDocxPath, Convert.FromBase64String(templateDocx.Base64Document));

        AssertDocxContainsText(templateDocxPath, "MERGEFIELD");
        AssertDocxContainsText(templateDocxPath, "CustomerName");
        AssertDocxContainsText(templateDocxPath, "Customer.Address");

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = response.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.ClearApplicationFields,
                    KeepText = true
                }
            ]
        });

        AssertTxApplicationFieldCount(txPath, 0);
        Assert.Contains("Customer Address", workflow.GetText(response.SessionId).Text);
        Assert.DoesNotContain(workflow.GetDocumentModel(response.SessionId).Document.Sections[0].Blocks,
            block => string.Equals(block.Type, "field", StringComparison.OrdinalIgnoreCase));

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "merge-fields-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxContainsText(docxPath, "Customer Address");
    }

    [SkippableFact]
    public void ApplyOperationsInsertsMergeFieldsIntoTableCells()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "31",
                    Rows =
                    [
                        ["Product", "Qty", "Price"],
                        ["Placeholder", "2", "$750.00"]
                    ]
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "ProductName",
                    FieldText = "Product Name",
                    TableId = "31",
                    RowIndex = 1,
                    ColumnIndex = 0,
                    Placement = "replace"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxMergeField(txPath, "ProductName", "Product Name");
        AssertTxTableCell(txPath, 31, 2, 1, "Product Name");

        var modelCellBlocks = workflow.GetDocumentModel(response.SessionId)
            .Document
            .Sections[0]
            .Blocks[0]
            .Table!
            .Rows[1]
            .Cells[0]
            .Blocks;
        Assert.Single(modelCellBlocks);
        Assert.Equal("field", modelCellBlocks[0].Type);
        Assert.Equal("ProductName", modelCellBlocks[0].Field?.Name);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "table-cell-merge-field-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxContainsText(docxPath, "MERGEFIELD");
        AssertDocxContainsText(docxPath, "ProductName");
        AssertDocxContainsText(docxPath, "Product Name");
    }

    [SkippableFact]
    public void TableTextAndMergeFieldsDoNotInheritPreviousHeadingFormatting()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition
                    {
                        Name = "InvoiceTitle",
                        FontName = "Arial",
                        FontSize = 30,
                        FontSizeUnit = "pt",
                        Bold = true,
                        ColorHex = "#1F4E79"
                    }
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    StyleName = "InvoiceTitle",
                    Text = "Payment Invoice"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "32",
                    Rows =
                    [
                        ["Due Date", "DueDate"],
                        ["Item", "ItemName"]
                    ]
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "DueDate",
                    FieldText = "DueDate",
                    TableId = "32",
                    RowIndex = 0,
                    ColumnIndex = 1,
                    Placement = "replace"
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "ItemName",
                    FieldText = "ItemName",
                    TableId = "32",
                    RowIndex = 1,
                    ColumnIndex = 1,
                    Placement = "replace"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxTableCellTextFormat(txPath, 32, 1, 1, expectedFontSize: 200, expectedHex: "#FFFFFF", expectedBold: true);
        AssertTxTableCellTextFormat(txPath, 32, 1, 2, expectedFontSize: 200, expectedHex: "#FFFFFF", expectedBold: true);
        AssertTxTableCellTextFormat(txPath, 32, 2, 2, expectedFontSize: 200, expectedHex: "#111827");
    }

    [SkippableFact]
    public void AppendTableAfterSectionLayoutFitsWithinPageMargins()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    PageSize = "Letter",
                    Unit = "in",
                    MarginLeft = 1,
                    MarginRight = 1,
                    MarginTop = 1,
                    MarginBottom = 1
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "33",
                    Rows =
                    [
                        ["Item", "Description", "Qty", "Unit Price", "Line Total"],
                        ["ItemName", "Description", "Quantity", "UnitPrice", "LineTotal"]
                    ]
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.FormatTableColumn,
                    TableId = "33",
                    ColumnIndex = 1,
                    Width = 230,
                    Unit = "pt"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxTableWidthAtMost(txPath, 33, expectedMaximumWidth: 9360);

        var modelTable = workflow.GetDocumentModel(response.SessionId).Document.Sections[0].Blocks[0].Table;
        Assert.NotNull(modelTable);
        Assert.Equal("pt", modelTable.ColumnWidthUnit);
        Assert.True(modelTable.ColumnWidths.Sum(width => width ?? 0) <= 468);
    }

    [SkippableFact]
    public void AppendMergeFieldAfterParagraphKeepsFieldOnSameLine()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Invoice Number: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "InvoiceNumber",
                    FieldText = "InvoiceNumber"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Invoice Date: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "InvoiceDate",
                    FieldText = "InvoiceDate"
                }
            ]
        });

        var text = workflow.GetText(response.SessionId).Text;
        var visibleText = text.Replace("\r", "<CR>").Replace("\n", "<LF>");

        Assert.True(text.Contains("Invoice Number: InvoiceNumber", StringComparison.Ordinal), visibleText);
        Assert.True(text.Contains("Invoice Date: InvoiceDate", StringComparison.Ordinal), visibleText);
        Assert.DoesNotContain("Invoice Number: \r\nInvoiceNumber", text);
        Assert.DoesNotContain("Invoice Date: \r\nInvoiceDate", text);
    }

    [SkippableFact]
    public void FormatParagraphsAppliesSpaceAfterToAllParagraphs()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "First"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Second"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatParagraphs,
                    Paragraph = new Neutral.ParagraphStyleDefinition
                    {
                        SpaceAfter = 20,
                        Unit = "pt"
                    }
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxParagraphSpaceAfter(txPath, 400);
    }

    [SkippableFact]
    public void StylePresetsCanApplyParagraphSpacing()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    StyleName = "Heading",
                    Text = "Preset Heading"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Preset body text"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxParagraphSpacing(txPath, collectionIndex: 1, expectedSpaceAfterTwips: 240);
        AssertTxParagraphSpacing(txPath, collectionIndex: 2, expectedSpaceAfterTwips: 120);
        AssertTxParagraphFormattingStyle(txPath, collectionIndex: 1, expectedStyleName: "Heading");
        AssertTxParagraphFormattingStyle(txPath, collectionIndex: 2, expectedStyleName: "Body");

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("Heading", model.Sections[0].Blocks[0].Paragraph?.StyleName);
        Assert.Equal("Body", model.Sections[0].Blocks[1].Paragraph?.StyleName);
        Assert.Equal(12, model.Sections[0].Blocks[0].Paragraph?.ParagraphStyle?.SpaceAfter);
        Assert.Equal(6, model.Sections[0].Blocks[1].Paragraph?.ParagraphStyle?.SpaceAfter);
    }

    [SkippableFact]
    public void FormatParagraphsCanRightAlignOneParagraph()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Subtotal: Subtotal"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Tax: Tax"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatParagraphs,
                    ParagraphIndex = 0,
                    Paragraph = new Neutral.ParagraphStyleDefinition
                    {
                        Alignment = "right"
                    }
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxParagraphAlignment(txPath, 1, HorizontalAlignment.Right);
        AssertTxParagraphAlignment(txPath, 2, HorizontalAlignment.Left);
    }

    [SkippableFact]
    public void MergeTemplateUsesMailMergeWithJsonDataAndStoresDocx()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Invoice for "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "CustomerName",
                    FieldText = "Customer Name"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "41",
                    Rows =
                    [
                        ["Product", "Price"],
                        ["Product placeholder", "Price placeholder"]
                    ]
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "ProductName",
                    FieldText = "Product Name",
                    TableId = "41",
                    RowIndex = 1,
                    ColumnIndex = 0,
                    Placement = "replace"
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "Price",
                    FieldText = "Price",
                    TableId = "41",
                    RowIndex = 1,
                    ColumnIndex = 1,
                    Placement = "replace"
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeBlock,
                    BlockName = "lineItems",
                    TableId = "41",
                    RowIndex = 1
                }
            ]
        });

        var templateFields = workflow.GetTemplateMergeFields(response.SessionId);
        Assert.Equal(3, templateFields.FieldCount);
        Assert.Contains(templateFields.Fields, field => field.Name == "CustomerName");
        Assert.Contains(templateFields.Fields, field => field.Name == "ProductName");
        Assert.Contains(templateFields.Fields, field => field.Name == "Price");
        var templateBlocks = workflow.GetTemplateMergeBlocks(response.SessionId);
        var block = Assert.Single(templateBlocks.Blocks);
        Assert.Equal("txmb_lineItems", block.Name);
        Assert.Equal("lineItems", block.BlockName);

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");
        AssertTxMergeBlock(txPath, "lineItems");

        var mergeResponse = MergeTemplateOrSkipIfUnlicensed(workflow, new MergeTemplateRequest
        {
            SessionId = response.SessionId,
            JsonData = """
            {
              "CustomerName": "ACME Corp",
              "lineItems": [
                {
                  "ProductName": "TX Fuel",
                  "Price": "199.00"
                },
                {
                  "ProductName": "Support Plan",
                  "Price": "49.00"
                }
              ]
            }
            """
        });

        Assert.Equal(response.SessionId, mergeResponse.SessionId);
        Assert.Equal(3, mergeResponse.MergedFieldCount);
        Assert.Contains("CustomerName", mergeResponse.MergedFieldNames);
        Assert.Equal(0, mergeResponse.RemainingFieldCount);

        var text = workflow.GetText(response.SessionId).Text;
        Assert.Contains("ACME Corp", text);
        Assert.Contains("TX Fuel", text);
        Assert.Contains("199.00", text);
        Assert.Contains("Support Plan", text);
        Assert.Contains("49.00", text);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "mailmerge-json-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxContainsText(docxPath, "ACME Corp");
        AssertDocxContainsText(docxPath, "TX Fuel");
        AssertDocxContainsText(docxPath, "199.00");
        AssertDocxContainsText(docxPath, "Support Plan");
        AssertDocxContainsText(docxPath, "49.00");
    }

    [SkippableFact]
    public void ApplyOperationsCreatesAllSupportedTxFormFields()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Customer name: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "customer_name",
                    FormFieldType = "text",
                    Text = "Jane Doe"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Contract type: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "contract_type",
                    FormFieldType = "selection",
                    Items = ["Standard", "Enterprise", "Trial"],
                    Text = "Enterprise",
                    Editable = true
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Approved: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "approved",
                    FormFieldType = "checkbox",
                    Checked = true
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Contract date: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "contract_date",
                    FormFieldType = "date",
                    Date = "2026-07-14"
                }
            ]
        });

        var fields = workflow.GetTemplateFormFields(response.SessionId);

        Assert.Equal(4, fields.FieldCount);
        Assert.Contains(fields.Fields, field => field.Name == "customer_name" && field.Type == "text" && field.Text == "Jane Doe");
        Assert.Contains(fields.Fields, field => field.Name == "contract_type" && field.Type == "selection" && field.Text == "Enterprise" && field.Editable == true && field.Items.Contains("Trial"));
        Assert.Contains(fields.Fields, field => field.Name == "approved" && field.Type == "checkbox" && field.Checked == true);
        Assert.Contains(fields.Fields, field => field.Name == "contract_date" && field.Type == "date");

        var modelFields = workflow.GetDocumentFields(response.SessionId);
        Assert.Contains(modelFields.Fields, field => field.Name == "customer_name" && field.Type == "form");
        Assert.Contains(modelFields.Fields, field => field.Name == "contract_type" && field.Properties["formFieldType"] == "selection");

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "form-fields-template.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));
        Assert.True(File.Exists(docxPath));
    }

    [SkippableFact]
    public void MergeTemplateCanPreselectAndReplaceFormFields()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Customer: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "customer_name",
                    FormFieldType = "text",
                    Text = "Placeholder"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Approved: "
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "approved",
                    FormFieldType = "checkbox",
                    Checked = false
                }
            ]
        });

        var preselectResponse = MergeTemplateOrSkipIfUnlicensed(workflow, new MergeTemplateRequest
        {
            SessionId = response.SessionId,
            FormFieldMergeType = "preselect",
            JsonData = """
            {
              "customer_name": "ACME Corp",
              "approved": true
            }
            """
        });

        Assert.Equal(2, preselectResponse.MergedFormFieldCount);
        Assert.Equal(2, preselectResponse.RemainingFormFieldCount);
        Assert.Contains("customer_name", preselectResponse.MergedFormFieldNames);

        var preselectedFields = workflow.GetTemplateFormFields(response.SessionId);
        Assert.Contains(preselectedFields.Fields, field => field.Name == "customer_name" && field.Text == "ACME Corp");
        Assert.Contains(preselectedFields.Fields, field => field.Name == "approved" && field.Checked == true);

        var replaceResponse = MergeTemplateOrSkipIfUnlicensed(workflow, new MergeTemplateRequest
        {
            SessionId = response.SessionId,
            FormFieldMergeType = "replace",
            JsonData = """
            {
              "customer_name": "ACME Corp",
              "approved": true
            }
            """
        });

        Assert.Equal(2, replaceResponse.MergedFormFieldCount);
        Assert.Equal(0, replaceResponse.RemainingFormFieldCount);
        Assert.Contains("ACME Corp", workflow.GetText(response.SessionId).Text);

        var pdf = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "pdf"
        });
        var pdfPath = Path.Combine(artifactRoot, "Exports", "form-fields-flattened.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
        File.WriteAllBytes(pdfPath, Convert.FromBase64String(pdf.Base64Document));
        Assert.True(File.Exists(pdfPath));
    }

    [SkippableFact]
    public void ApplyOperationsCreatesHeadersAndFooters()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = HeaderFooterCapabilityPack.SetHeaderFooter,
                    HeaderFooterType = "header",
                    Text = "Invoice Header",
                    Style = new TextStyleDefinition
                    {
                        Bold = true
                    }
                },
                new DocumentOperation
                {
                    Type = HeaderFooterCapabilityPack.SetHeaderFooter,
                    HeaderFooterType = "footer",
                    Text = "Page ",
                    IncludePageNumber = true,
                    TypeName = "DATE",
                    Date = "2026-07-14",
                    DateFormat = "yyyy-MM-dd"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Body content"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxHeaderFooterText(txPath, HeaderFooterType.Header, "Invoice Header");
        AssertTxHeaderFooterText(txPath, HeaderFooterType.Footer, "Page");
        AssertTxHeaderFooterText(txPath, HeaderFooterType.Footer, "2026-07-14");
        AssertTxFooterPageNumber(txPath);
        AssertTxHeaderFooterDateField(txPath, HeaderFooterType.Footer, new DateTime(2026, 7, 14), "yyyy-MM-dd");

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("Invoice Header", model.Sections[0].Header?.Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("Page {DATE}{PAGE}", model.Sections[0].Footer?.Blocks[0].Paragraph?.Runs[0].Text);
        Assert.Equal("Date", model.Sections[0].Footer?.Blocks[1].Field?.Name);
        Assert.Equal("DATE", model.Sections[0].Footer?.Blocks[1].Field?.Properties["typeName"]);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "header-footer-document.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxPartContainsText(docxPath, "word/header", "Invoice Header");
        AssertDocxPartContainsText(docxPath, "word/footer", "Page");
        AssertDocxPartContainsText(docxPath, "word/footer", "DATE");
    }

    [SkippableFact]
    public void ApplyOperationsCanInsertImagesIntoHeadersAndFooters()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var exportRoot = Path.Combine(artifactRoot, "Exports");
        Directory.CreateDirectory(exportRoot);
        Directory.CreateDirectory(artifactRoot);

        var imageBytes = CreateSamplePngBytes();
        var imagePath = Path.Combine(artifactRoot, "header-logo.png");
        File.WriteAllBytes(imagePath, imageBytes);

        var workflow = CreateWorkflow(artifactRoot);
        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = HeaderFooterCapabilityPack.SetHeaderFooter,
                    HeaderFooterType = "header",
                    Text = "Header with logo "
                },
                new DocumentOperation
                {
                    Type = MediaCapabilityPack.AppendImage,
                    Target = "header",
                    ImagePath = imagePath,
                    AltText = "Header logo",
                    HorizontalScaling = 40,
                    VerticalScaling = 40
                },
                new DocumentOperation
                {
                    Type = MediaCapabilityPack.AppendImage,
                    Target = "footer",
                    ImageBase64 = "data:image/png;base64," + Convert.ToBase64String(imageBytes),
                    AltText = "Footer logo",
                    HorizontalScaling = 35,
                    VerticalScaling = 35
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Body content"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxHeaderFooterImageCount(txPath, HeaderFooterType.Header, 1);
        AssertTxHeaderFooterImageCount(txPath, HeaderFooterType.Footer, 1);

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("Header logo", model.Sections[0].Header?.Blocks.Single(block => block.Type == "image").Image?.AltText);
        Assert.Equal("Footer logo", model.Sections[0].Footer?.Blocks.Single(block => block.Type == "image").Image?.AltText);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(exportRoot, "header-footer-images.docx");
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));

        AssertDocxPartContainsText(docxPath, "word/header", "Header with logo");
        AssertDocxPartContainsDrawing(docxPath, "word/header");
        AssertDocxPartContainsDrawing(docxPath, "word/footer");
    }

    [SkippableFact]
    public void RenderDocumentModelCreatesHeadersAndFooters()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var document = new Document
        {
            Id = "model-header-footer-doc",
            Sections =
            [
                new Neutral.Section
                {
                    Id = "section-1",
                    Header = new Neutral.HeaderFooter
                    {
                        Type = "header",
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "paragraph",
                                Paragraph = new Neutral.Paragraph
                                {
                                    Runs = [new Run { Text = "Model Header" }]
                                }
                            }
                        ]
                    },
                    Footer = new Neutral.HeaderFooter
                    {
                        Type = "footer",
                        Blocks =
                        [
                            new DocumentBlock
                            {
                                Type = "paragraph",
                                Paragraph = new Neutral.Paragraph
                                {
                                    Runs = [new Run { Text = "Model Page {PAGE}" }]
                                }
                            }
                        ]
                    },
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Type = "paragraph",
                            Paragraph = new Neutral.Paragraph
                            {
                                Runs = [new Run { Text = "Model body" }]
                            }
                        }
                    ]
                }
            ]
        };

        var response = RenderDocumentModelOrSkipIfUnlicensed(workflow, new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = document
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxHeaderFooterText(txPath, HeaderFooterType.Header, "Model Header");
        AssertTxHeaderFooterText(txPath, HeaderFooterType.Footer, "Model Page");
        AssertTxFooterPageNumber(txPath);
    }

    [SkippableFact]
    public void ApplyOperationsSetsSectionPageSizeAndMargins()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    PageSize = "A4",
                    Orientation = "landscape",
                    Unit = "cm",
                    MarginLeft = 2,
                    MarginRight = 2,
                    MarginTop = 1.5f,
                    MarginBottom = 1.5f
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Landscape A4 layout"
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxSectionLayout(
            txPath,
            sectionIndex: 0,
            expectedWidthTwips: 16838,
            expectedHeightTwips: 11906,
            expectedLeftMarginTwips: 1134,
            expectedTopMarginTwips: 850,
            expectedLandscape: true);

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "Exports", "section-layout-a4-landscape.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(docxPath)!);
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));
        AssertDocxPageLayout(
            docxPath,
            expectedWidthTwips: 16838,
            expectedHeightTwips: 11906,
            expectedLeftMarginTwips: 1134,
            expectedTopMarginTwips: 850);

        var structure = workflow.GetDocumentStructure(response.SessionId);
        var layout = structure.Sections[0].PageLayout;

        Assert.Equal("landscape", layout?.Orientation);
        Assert.True(layout?.PageWidth > layout?.PageHeight);
    }

    [SkippableFact]
    public void ApplyOperationsCreatesMultipleSectionsWithDifferentLayouts()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);

        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    PageWidth = 105,
                    PageHeight = 148,
                    Unit = "mm",
                    Orientation = "landscape",
                    MarginLeft = 8,
                    MarginRight = 8,
                    MarginTop = 8,
                    MarginBottom = 8
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "The first A6 page is landscape and contains a compact paragraph about launch timing, team notes, and a calm morning plan."
                },
                new DocumentOperation
                {
                    Type = SectionCapabilityPack.InsertSectionBreak
                },
                new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    SectionIndex = 1,
                    PageWidth = 105,
                    PageHeight = 148,
                    Unit = "mm",
                    Orientation = "portrait",
                    MarginLeft = 8,
                    MarginRight = 8,
                    MarginTop = 8,
                    MarginBottom = 8
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "The second A6 page is portrait and contains follow-up notes, decisions, and a short closing summary for the reader."
                }
            ]
        });

        var sessionRoot = Path.Combine(artifactRoot, "sessions", response.SessionId);
        var txPath = Path.Combine(sessionRoot, "document.tx");

        AssertTxSectionLayout(
            txPath,
            sectionIndex: 0,
            expectedWidthTwips: 8391,
            expectedHeightTwips: 5953,
            expectedLeftMarginTwips: 454,
            expectedTopMarginTwips: 454,
            expectedLandscape: true);
        AssertTxSectionLayout(
            txPath,
            sectionIndex: 1,
            expectedWidthTwips: 5953,
            expectedHeightTwips: 8391,
            expectedLeftMarginTwips: 454,
            expectedTopMarginTwips: 454,
            expectedLandscape: false);

        var structure = workflow.GetDocumentStructure(response.SessionId);
        Assert.Equal(2, structure.SectionCount);
        Assert.Equal("landscape", structure.Sections[0].PageLayout?.Orientation);
        Assert.Equal("portrait", structure.Sections[1].PageLayout?.Orientation);
        Assert.Contains("first A6 page", structure.Sections[0].Blocks[0].TextPreview);
        Assert.Contains("second A6 page", structure.Sections[1].Blocks[0].TextPreview);
    }

    private static void SkipIfTxLicenseIsMissing()
    {
        try
        {
            TxTextControlLicensing.Configure();
            using var tx = new ServerTextControl();
            tx.Create();
        }
        catch (LicenseException ex)
        {
            Skip.If(true, $"TX Text Control license is required to create actual document artifacts: {ex.Message}");
        }
    }

    private static Models.Responses.ApplyOperationsResponse ApplyOperationsOrSkipIfUnlicensed(
        DocumentWorkflowService workflow,
        ApplyOperationsRequest request)
    {
        try
        {
            return workflow.ApplyOperations(request);
        }
        catch (LicenseException ex)
        {
            Skip.If(true, $"TX Text Control license is required to create actual document artifacts: {ex.Message}");
            throw;
        }
    }

    private static Models.Responses.ApplyOperationsResponse RenderDocumentModelOrSkipIfUnlicensed(
        DocumentWorkflowService workflow,
        RenderDocumentModelRequest request)
    {
        try
        {
            return workflow.RenderDocumentModel(request);
        }
        catch (LicenseException ex)
        {
            Skip.If(true, $"TX Text Control license is required to create actual document artifacts: {ex.Message}");
            throw;
        }
    }

    private static Models.Responses.MergeTemplateResponse MergeTemplateOrSkipIfUnlicensed(
        DocumentWorkflowService workflow,
        MergeTemplateRequest request)
    {
        try
        {
            return workflow.MergeTemplate(request);
        }
        catch (LicenseException ex)
        {
            Skip.If(true, $"TX Text Control license is required to create actual document artifacts: {ex.Message}");
            throw;
        }
    }

    private static void AssertTxParagraphStyles(string txPath, int expectedHeadingFontSize)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var heading = tx.ParagraphStyles.GetItem("Heading");
        var paragraphText = tx.ParagraphStyles.GetItem("Body");

        Assert.NotNull(heading);
        Assert.NotNull(paragraphText);
        Assert.True(heading.Bold);
        Assert.Equal(expectedHeadingFontSize, heading.FontSize);
        Assert.False(paragraphText.Bold);
        Assert.Equal(180, paragraphText.FontSize);
        Assert.Equal("Heading", tx.Paragraphs[1].FormattingStyle);
        Assert.Equal("Body", tx.Paragraphs[2].FormattingStyle);
    }

    private static void AssertTxTable(string txPath)
        => AssertTxTable(txPath, 10, "$1.5M");

    private static void AssertTxImageCount(string txPath, int expectedCount)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.Equal(expectedCount, tx.Images.Count);
    }

    private static void AssertTxHeaderFooterImageCount(string txPath, HeaderFooterType type, int expectedCount)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var headerFooter = tx.HeadersAndFooters.GetItem(type);
        Assert.NotNull(headerFooter);
        Assert.Equal(expectedCount, headerFooter!.Images.Count);
    }

    private static void AssertTxTable(string txPath, int tableId, string expectedLastValue)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        if (tableId == 10)
        {
            Assert.Equal("Quarter", table.Cells.GetItem(1, 1).Text);
            Assert.Equal("Revenue", table.Cells.GetItem(1, 2).Text);
            Assert.Equal(expectedLastValue, table.Cells.GetItem(3, 2).Text);
        }
        else
        {
            Assert.Equal("country", table.Cells.GetItem(1, 1).Text);
            Assert.Equal("sales", table.Cells.GetItem(1, 2).Text);
            Assert.Equal(expectedLastValue, table.Cells.GetItem(2, 2).Text);
        }
    }

    private static void AssertTxTableCell(string txPath, int tableId, int row, int column, string expectedValue)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        Assert.Equal(expectedValue, table.Cells.GetItem(row, column).Text);
    }

    private static void AssertTxTableCellTextFormat(
        string txPath,
        int tableId,
        int row,
        int column,
        int expectedFontSize,
        string expectedHex,
        bool expectedBold = false)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        table.Cells.GetItem(row, column).Select();
        var selection = tx.Selection;
        Assert.Equal(expectedFontSize, selection.FontSize);
        Assert.Equal(ColorTranslator.FromHtml(expectedHex).ToArgb(), selection.ForeColor.ToArgb());
        Assert.Equal(expectedBold, selection.Bold);
        Assert.False(selection.Italic);
    }

    private static void AssertTxTableCellBackColor(string txPath, int tableId, int row, int column, string expectedHex)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        var expected = ColorTranslator.FromHtml(expectedHex).ToArgb();
        Assert.Equal(expected, table.Cells.GetItem(row, column).CellFormat.BackColor.ToArgb());
    }

    private static void AssertTxTableColumnWidth(string txPath, int tableId, int column, int expectedWidth)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        Assert.Equal(expectedWidth, table.Columns.GetItem(column).Width);
    }

    private static void AssertTxTableWidthAtMost(string txPath, int tableId, int expectedMaximumWidth)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        var width = Enumerable
            .Range(1, table.Columns.Count)
            .Sum(column => table.Columns.GetItem(column).Width);
        Assert.InRange(width, 1, expectedMaximumWidth);
    }

    private static void AssertTxTableCellBottomBorder(
        string txPath,
        int tableId,
        int row,
        int column,
        int expectedWidth,
        string expectedHex)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        var cellFormat = table.Cells.GetItem(row, column).CellFormat;
        Assert.Equal(expectedWidth, cellFormat.BottomBorderWidth);
        Assert.Equal(ColorTranslator.FromHtml(expectedHex).ToArgb(), cellFormat.BottomBorderColor.ToArgb());
    }

    private static void AssertTxTableCellBorders(
        string txPath,
        int tableId,
        int row,
        int column,
        int expectedWidth,
        string expectedHex)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var table = tx.Tables.GetItem(tableId);

        Assert.NotNull(table);
        var expectedColor = ColorTranslator.FromHtml(expectedHex).ToArgb();
        var cellFormat = table.Cells.GetItem(row, column).CellFormat;
        Assert.Equal(expectedWidth, cellFormat.LeftBorderWidth);
        Assert.Equal(expectedWidth, cellFormat.TopBorderWidth);
        Assert.Equal(expectedWidth, cellFormat.RightBorderWidth);
        Assert.Equal(expectedWidth, cellFormat.BottomBorderWidth);
        Assert.Equal(expectedColor, cellFormat.LeftBorderColor.ToArgb());
        Assert.Equal(expectedColor, cellFormat.TopBorderColor.ToArgb());
        Assert.Equal(expectedColor, cellFormat.RightBorderColor.ToArgb());
        Assert.Equal(expectedColor, cellFormat.BottomBorderColor.ToArgb());
    }

    private static void AssertTxMergeField(string txPath, string expectedName, string expectedText)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var field = tx.ApplicationFields
            .Cast<ApplicationField>()
            .SingleOrDefault(field => string.Equals(field.TypeName, "MERGEFIELD", StringComparison.OrdinalIgnoreCase)
                                      && field.Parameters.Length > 0
                                      && string.Equals(field.Parameters[0], expectedName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(field);
        Assert.Equal(expectedText, field!.Text);
    }

    private static void AssertTxApplicationFieldCount(string txPath, int expectedCount)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.Equal(expectedCount, tx.ApplicationFields.Count);
    }

    private static void AssertTxParagraphSpaceAfter(string txPath, int expectedTwips)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.True(tx.Paragraphs.Count > 0);
        foreach (TXTextControl.Paragraph paragraph in tx.Paragraphs)
        {
            Assert.Equal(expectedTwips, paragraph.Format.BottomDistance);
        }
    }

    private static void AssertTxParagraphSpacing(
        string txPath,
        int collectionIndex,
        int expectedSpaceAfterTwips)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.True(collectionIndex >= 1 && collectionIndex <= tx.Paragraphs.Count);
        Assert.Equal(expectedSpaceAfterTwips, tx.Paragraphs[collectionIndex].Format.BottomDistance);
    }

    private static void AssertTxParagraphFormattingStyle(
        string txPath,
        int collectionIndex,
        string expectedStyleName)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.True(collectionIndex >= 1 && collectionIndex <= tx.Paragraphs.Count);
        Assert.Equal(expectedStyleName, tx.Paragraphs[collectionIndex].FormattingStyle);
    }

    private static void AssertTxParagraphAlignment(string txPath, int collectionIndex, HorizontalAlignment expected)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.True(collectionIndex >= 1 && collectionIndex <= tx.Paragraphs.Count);
        Assert.Equal(expected, tx.Paragraphs[collectionIndex].Format.Alignment);
    }

    private static void AssertTxMergeBlock(string txPath, string expectedName)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat, new LoadSettings
        {
            ApplicationFieldFormat = ApplicationFieldFormat.MSWord,
            LoadSubTextParts = true
        });

        var block = tx.SubTextParts
            .Cast<SubTextPart>()
            .SingleOrDefault(part => string.Equals(part.Name, "txmb_" + expectedName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(block);
        Assert.True(block!.Length > 0);
    }

    private static void AssertTxHeaderFooterText(string txPath, HeaderFooterType type, string expectedText)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var headerFooter = tx.HeadersAndFooters.GetItem(type);
        Assert.NotNull(headerFooter);
        var text = string.Join(
            "\n",
            headerFooter!.Paragraphs.Cast<TXTextControl.Paragraph>().Select(paragraph => paragraph.Text ?? string.Empty));
        Assert.Contains(expectedText, text);
    }

    private static void AssertTxFooterPageNumber(string txPath)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var footer = tx.HeadersAndFooters.GetItem(HeaderFooterType.Footer);
        Assert.NotNull(footer);
        Assert.True(footer!.PageNumberFields.Count > 0);
    }

    private static void AssertTxHeaderFooterMergeField(string txPath, HeaderFooterType type, string expectedName, string expectedText)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat, new LoadSettings
        {
            ApplicationFieldFormat = ApplicationFieldFormat.MSWord
        });

        var headerFooter = tx.HeadersAndFooters.GetItem(type);
        Assert.NotNull(headerFooter);

        var field = headerFooter!.ApplicationFields
            .Cast<ApplicationField>()
            .SingleOrDefault(field => string.Equals(field.TypeName, "MERGEFIELD", StringComparison.OrdinalIgnoreCase)
                                      && field.Parameters.Length > 0
                                      && string.Equals(field.Parameters[0], expectedName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(field);
        Assert.Equal(expectedText, field!.Text);
    }

    private static void AssertTxHeaderFooterDateField(string txPath, HeaderFooterType type, DateTime expectedDate, string expectedFormat)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat, new LoadSettings
        {
            ApplicationFieldFormat = ApplicationFieldFormat.MSWord
        });

        var headerFooter = tx.HeadersAndFooters.GetItem(type);
        Assert.NotNull(headerFooter);

        var field = headerFooter!.ApplicationFields
            .Cast<ApplicationField>()
            .Select(field => new TXTextControl.DocumentServer.Fields.DateField(field))
            .SingleOrDefault(field => string.Equals(field.TypeName, "DATE", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(field);
        Assert.Equal("DATE", field!.TypeName);
        Assert.Equal(expectedFormat, field.Format);
    }

    private static void AssertTxSectionLayout(
        string txPath,
        int sectionIndex,
        int expectedWidthTwips,
        int expectedHeightTwips,
        int expectedLeftMarginTwips,
        int expectedTopMarginTwips,
        bool expectedLandscape)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        var section = tx.Sections[sectionIndex + 1];
        var format = section!.Format;

        Assert.NotNull(section);
        Assert.Equal(ToTxSectionUnit(expectedWidthTwips), (int)Math.Round(format.PageSize.Width));
        Assert.Equal(ToTxSectionUnit(expectedHeightTwips), (int)Math.Round(format.PageSize.Height));
        Assert.Equal(ToTxSectionUnit(expectedLeftMarginTwips), (int)Math.Round(format.PageMargins.Left));
        Assert.Equal(ToTxSectionUnit(expectedTopMarginTwips), (int)Math.Round(format.PageMargins.Top));
        Assert.Equal(expectedLandscape, format.Landscape);
    }

    private static void AssertDocxPageLayout(
        string docxPath,
        int expectedWidthTwips,
        int expectedHeightTwips,
        int expectedLeftMarginTwips,
        int expectedTopMarginTwips)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("DOCX does not contain word/document.xml.");
        using var reader = new StreamReader(entry.Open());
        var documentXml = reader.ReadToEnd();

        var pageSize = System.Text.RegularExpressions.Regex.Match(documentXml, "<w:pgSz[^>]*>");
        var pageMargins = System.Text.RegularExpressions.Regex.Match(documentXml, "<w:pgMar[^>]*>");
        Assert.True(pageSize.Success, "DOCX does not contain w:pgSz.");
        Assert.True(pageMargins.Success, "DOCX does not contain w:pgMar.");

        AssertDocxAttributeNear(pageSize.Value, "w", expectedWidthTwips);
        AssertDocxAttributeNear(pageSize.Value, "h", expectedHeightTwips);
        AssertDocxAttributeNear(pageMargins.Value, "left", expectedLeftMarginTwips);
        AssertDocxAttributeNear(pageMargins.Value, "top", expectedTopMarginTwips);
    }

    private static void AssertDocxAttributeNear(string xml, string attributeName, int expectedTwips)
    {
        var match = System.Text.RegularExpressions.Regex.Match(xml, $@"w:{attributeName}=""(?<value>\d+)""");
        Assert.True(match.Success, $"DOCX XML does not contain w:{attributeName}.");
        var actual = int.Parse(match.Groups["value"].Value);
        Assert.InRange(actual, expectedTwips - 6, expectedTwips + 6);
    }

    private static int ToTxSectionUnit(int twips)
        => (int)Math.Round(twips / 14.4f);

    private static void AssertDocxHasBoldRun(string docxPath, string text)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("DOCX does not contain word/document.xml.");
        using var reader = new StreamReader(entry.Open());
        var documentXml = reader.ReadToEnd();

        Assert.Matches(
            $"<w:rPr>[\\s\\S]*?<w:b[\\s\\S]*?</w:rPr>[\\s\\S]*?<w:t[^>]*>{text}</w:t>",
            documentXml);
    }

    private static void AssertDocxContainsTableText(string docxPath, IReadOnlyList<string> expectedText)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("DOCX does not contain word/document.xml.");
        using var reader = new StreamReader(entry.Open());
        var documentXml = reader.ReadToEnd();

        Assert.Contains("<w:tbl>", documentXml);
        foreach (var text in expectedText)
        {
            Assert.Contains(text, documentXml);
        }
    }

    private static void AssertDocxContainsMedia(string docxPath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);

        Assert.Contains(
            zip.Entries,
            entry => entry.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertDocxPartContainsDrawing(string docxPath, string entryPrefix)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entries = zip.Entries
            .Where(entry => entry.FullName.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"DOCX does not contain a part starting with {entryPrefix}.");
        }

        foreach (var entry in entries)
        {
            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();
            if (xml.Contains("<w:drawing", StringComparison.Ordinal)
                || xml.Contains("<w:pict", StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail($"DOCX parts starting with {entryPrefix} do not contain drawing markup.");
    }

    private static void AssertDocxContainsText(string docxPath, string expectedText)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("DOCX does not contain word/document.xml.");
        using var reader = new StreamReader(entry.Open());
        var documentXml = reader.ReadToEnd();

        Assert.Contains(expectedText, documentXml);
    }

    private static void AssertDocxPartContainsText(string docxPath, string entryPrefix, string expectedText)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entries = zip.Entries
            .Where(entry => entry.FullName.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase)
                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"DOCX does not contain a part starting with {entryPrefix}.");
        }

        foreach (var entry in entries)
        {
            using var reader = new StreamReader(entry.Open());
            if (reader.ReadToEnd().Contains(expectedText, StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail($"DOCX parts starting with {entryPrefix} do not contain '{expectedText}'.");
    }

    private static DocumentWorkflowService CreateWorkflow(string artifactRoot)
    {
        var automationOptions = new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [BasicTextCapabilityPack.PackName, TableCapabilityPack.PackName, MediaCapabilityPack.PackName, FieldsCapabilityPack.PackName, SectionCapabilityPack.PackName, HeaderFooterCapabilityPack.PackName],
            DefaultParagraphStyleName = "Body",
            StyleRoles = new StyleRoleDefinition
            {
                Title = "Title",
                Heading1 = "Heading",
                Heading2 = "Heading2",
                Body = "Body"
            },
            StylePresets =
            [
                new TextStyleDefinition
                {
                    Name = "Title",
                    FontName = "Liberation Sans",
                    FontSize = 30,
                    FontSizeUnit = "pt",
                    Bold = true,
                    ColorHex = "#1F4E79",
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 0,
                        SpaceAfter = 16,
                        Unit = "pt"
                    }
                },
                new TextStyleDefinition
                {
                    Name = "Heading",
                    FontName = "Liberation Sans",
                    FontSize = 20,
                    FontSizeUnit = "px",
                    Bold = true,
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 0,
                        SpaceAfter = 12,
                        Unit = "pt"
                    }
                },
                new TextStyleDefinition
                {
                    Name = "Heading2",
                    FontName = "Liberation Sans",
                    FontSize = 16,
                    FontSizeUnit = "pt",
                    Bold = true,
                    ColorHex = "#344054",
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 10,
                        SpaceAfter = 8,
                        Unit = "pt"
                    }
                },
                new TextStyleDefinition
                {
                    Name = "Body",
                    FontName = "Liberation Sans",
                    FontSize = 12,
                    FontSizeUnit = "px",
                    Bold = false,
                    Italic = false,
                    Underline = false,
                    ColorHex = "#000000",
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 0,
                        SpaceAfter = 6,
                        Unit = "pt"
                    }
                }
            ],
            TableStylePresets =
            [
                new TableStylePresetDefinition
                {
                    Name = "Professional Blue",
                    HeaderRowIndex = 0,
                    HeaderStyle = new TextStyleDefinition
                    {
                        FontName = "Liberation Sans",
                        FontSize = 10,
                        FontSizeUnit = "pt",
                        Bold = true,
                        ColorHex = "#FFFFFF"
                    },
                    HeaderCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#1F4E79",
                        Border = new CellBorderDefinition
                        {
                            Width = 10,
                            ColorHex = "#D0D5DD"
                        }
                    },
                    BodyStyle = new TextStyleDefinition
                    {
                        FontName = "Liberation Sans",
                        FontSize = 10,
                        FontSizeUnit = "pt",
                        Bold = false,
                        ColorHex = "#111827"
                    },
                    BodyCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#FFFFFF",
                        Border = new CellBorderDefinition
                        {
                            Width = 10,
                            ColorHex = "#D0D5DD"
                        }
                    },
                    AlternatingRowCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#F8FAFC",
                        Border = new CellBorderDefinition
                        {
                            Width = 10,
                            ColorHex = "#D0D5DD"
                        }
                    }
                }
            ],
            EnabledOperations =
            [
                BasicTextCapabilityPack.DefineStyle,
                BasicTextCapabilityPack.AppendParagraph,
                BasicTextCapabilityPack.ApplyStyleToParagraph,
                BasicTextCapabilityPack.FormatParagraphs,
                BasicTextCapabilityPack.FormatTextOccurrences,
                BasicTextCapabilityPack.ReplaceText,
                TableCapabilityPack.AppendTable,
                TableCapabilityPack.SetTableCellText,
                TableCapabilityPack.FormatTableCell,
                TableCapabilityPack.FormatTableHeaderRow,
                TableCapabilityPack.FormatTableColumn,
                TableCapabilityPack.ApplyTableStylePreset,
                TableCapabilityPack.AddTableRow,
                MediaCapabilityPack.AppendImage,
                FieldsCapabilityPack.AppendMergeField,
                FieldsCapabilityPack.UpdateMergeField,
                FieldsCapabilityPack.ClearApplicationFields,
                FieldsCapabilityPack.AppendMergeBlock,
                FieldsCapabilityPack.AppendFormField,
                FieldsCapabilityPack.UpdateFormField,
                FieldsCapabilityPack.ClearFormFields,
                SectionCapabilityPack.InsertSectionBreak,
                SectionCapabilityPack.SetSectionLayout,
                HeaderFooterCapabilityPack.SetHeaderFooter
            ]
        };

        IDocumentOperationHandler[] handlers =
        [
            new DefineStyleOperationHandler(),
            new AppendParagraphOperationHandler(),
            new ApplyStyleToParagraphOperationHandler(),
            new FormatParagraphsOperationHandler(),
            new FormatTextOccurrencesOperationHandler(),
            new ReplaceTextOperationHandler(),
            new AppendTableOperationHandler(automationOptions),
            new SetTableCellTextOperationHandler(),
            new FormatTableCellOperationHandler(),
            new FormatTableHeaderRowOperationHandler(),
            new FormatTableColumnOperationHandler(),
            new ApplyTableStylePresetOperationHandler(automationOptions),
            new AddTableRowOperationHandler(),
            new AppendImageOperationHandler(),
            new AppendMergeFieldOperationHandler(),
            new UpdateMergeFieldOperationHandler(),
            new ClearApplicationFieldsOperationHandler(),
            new AppendMergeBlockOperationHandler(),
            new AppendFormFieldOperationHandler(),
            new UpdateFormFieldOperationHandler(),
            new ClearFormFieldsOperationHandler(),
            new InsertSectionBreakOperationHandler(),
            new SetSectionLayoutOperationHandler(),
            new SetHeaderFooterOperationHandler()
        ];
        ICapabilityPack[] packs =
        [
            new BasicTextCapabilityPack(),
            new TableCapabilityPack(),
            new MediaCapabilityPack(),
            new FieldsCapabilityPack(),
            new SectionCapabilityPack(),
            new HeaderFooterCapabilityPack()
        ];
        var settings = new AutomationSettingsService(
            automationOptions,
            Path.Combine(artifactRoot, "appsettings.test.json"),
            packs,
            handlers);
        var registry = new DocumentOperationRegistry(handlers, packs, settings);

        var pathResolver = new PathResolver(Microsoft.Extensions.Options.Options.Create(new McpServerOptions
        {
            BasePath = artifactRoot
        }));
        var sessions = new DocumentSessionService(pathResolver);
        var engine = new ServerTextControlDocumentEngine(
            registry,
            Microsoft.Extensions.Options.Options.Create(automationOptions));

        return new DocumentWorkflowService(
            sessions,
            engine,
            Microsoft.Extensions.Options.Options.Create(automationOptions));
    }

    private static string GetArtifactRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TxTextControl.McpServer.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root.");
        }

        return Path.Combine(directory.FullName, "Tests", "TestArtifacts");
    }

    private static byte[] CreateSamplePngBytes()
        => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADUlEQVR42mP8z8BQDwAFgwJ/lwusNwAAAABJRU5ErkJggg==");

    private static Neutral.TableCell CreateTextCell(string id, string text)
        => new()
        {
            Id = id,
            Blocks =
            [
                new DocumentBlock
                {
                    Type = "paragraph",
                    Paragraph = new Neutral.Paragraph
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
                }
            ]
        };
}
