using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;
using TxTextControl.McpServer.Tools;
using TXTextControl;
using Xunit;
using Neutral = TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Tests;

public sealed class DocumentCreationIntegrationTests
{
    [SkippableFact]
    public void KnowledgeExtractionPagesLosslessSourcesWithoutChangingAnotherSession()
    {
        SkipIfTxLicenseIsMissing();
        string root = Path.Combine(Path.GetTempPath(), "tx-knowledge-extraction-" + Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(root);
        var working = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        { Operations = [new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Unchanged working agreement." }] });
        var operations = Enumerable.Range(0, 70).Select(i => new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = $"Reference paragraph {i + 1}: private approved wording." }).ToList();
        operations.Add(new DocumentOperation { Type = TableCapabilityPack.AppendTable, Rows = [["Product", "Price"], ["Widget", "42 EUR"]] });
        var reference = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest { Operations = operations });
        try
        {
            List<KnowledgeExtractionBlock> blocks = [];
            int? cursor = 0;
            while (cursor.HasValue)
            {
                var page = workflow.ExtractKnowledgeBlocks(reference.SessionId, cursor.Value);
                Assert.True(page.Blocks.Count <= 32);
                blocks.AddRange(page.Blocks); cursor = page.NextBlock;
            }
            Assert.Contains(blocks, b => b.Text.Contains("Reference paragraph 70", StringComparison.Ordinal));
            Assert.Contains(blocks, b => b.Locator.StartsWith("Table ", StringComparison.Ordinal) && b.Text.Contains("Widget", StringComparison.Ordinal) && b.TableHeader!.Contains("Price", StringComparison.Ordinal));
            var original = workflow.InspectDocument(new InspectDocumentRequest { SessionId = working.SessionId });
            Assert.Contains(original.Paragraphs, p => p.Text.Contains("Unchanged working agreement.", StringComparison.Ordinal));
            Assert.DoesNotContain(original.Paragraphs, p => p.Text.Contains("Reference paragraph", StringComparison.Ordinal));
        }
        finally { workflow.DeleteSession(working.SessionId); workflow.DeleteSession(reference.SessionId); }
    }

    [SkippableFact]
    public void HeadingAwareSectionToolsReplaceOnlyTheResolvedBody()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-section-tool-tests",
            Guid.NewGuid().ToString("N")));
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading", Text = "1. Definitions" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Party A means Acme Corporation." },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading", Text = "7. NO WARRANTY" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "The information is provided as-is." },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Neither party makes any warranty." },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading", Text = "8. LIMITATION OF LIABILITY" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Liability remains limited." }
            ]
        });
        var tools = new SectionTools(workflow);

        var inspected = Assert.IsType<Models.Responses.DocumentSectionResponse>(
            tools.InspectDocumentSection(new InspectDocumentSectionRequest
            {
                SessionId = created.SessionId,
                Heading = "No Warranty"
            }));
        var edited = Assert.IsType<Models.Responses.DocumentSectionEditResponse>(
            tools.ReplaceDocumentSection(new ReplaceDocumentSectionRequest
            {
                SessionId = created.SessionId,
                Heading = inspected.Heading,
                ExpectedContentHash = inspected.ContentHash,
                ReplacementText = "Party A makes no warranty regarding forecasts supplied by Party B.\nParty B assumes the risk of relying on those forecasts."
            }));
        var verified = workflow.InspectDocumentSection(new InspectDocumentSectionRequest
        {
            SessionId = created.SessionId,
            Heading = "No Warranty"
        });
        var allParagraphs = workflow.InspectDocument(new InspectDocumentRequest
        {
            SessionId = created.SessionId
        }).Paragraphs;

        Assert.Equal(created.SessionId, edited.SessionId);
        Assert.Equal("7. NO WARRANTY", verified.Heading);
        Assert.Equal("Heading", verified.HeadingStyleName);
        Assert.Equal(2, verified.Paragraphs.Count);
        Assert.Contains("Party A makes no warranty", verified.Paragraphs[0].Text, StringComparison.Ordinal);
        Assert.Contains("Party B assumes the risk", verified.Paragraphs[1].Text, StringComparison.Ordinal);
        Assert.Contains(allParagraphs, paragraph => paragraph.Text == "8. LIMITATION OF LIABILITY");
        Assert.DoesNotContain(allParagraphs, paragraph => paragraph.Text.Contains("provided as-is", StringComparison.Ordinal));
        Assert.NotEqual(inspected.ContentHash, verified.ContentHash);
    }

    [SkippableFact]
    public void ReplaceDocumentSectionRejectsAStaleInspectionHash()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-stale-section-tests",
            Guid.NewGuid().ToString("N")));
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading", Text = "Payment" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Payment is due in 30 days." },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading", Text = "Termination" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Either party may terminate." }
            ]
        });
        var inspected = workflow.InspectDocumentSection(new InspectDocumentSectionRequest
        {
            SessionId = created.SessionId,
            Heading = "Payment"
        });
        workflow.EditDocument(new EditDocumentRequest
        {
            SessionId = created.SessionId,
            ParagraphIndex = inspected.BodyStartParagraphIndex,
            ReplacementText = "Payment is now due in 15 days."
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            workflow.ReplaceDocumentSection(new ReplaceDocumentSectionRequest
            {
                SessionId = created.SessionId,
                Heading = "Payment",
                ExpectedContentHash = inspected.ContentHash,
                ReplacementText = "Payment is waived."
            }));

        Assert.Contains("changed after it was inspected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void InsertTableToolAddsRowsWithoutExposingAnOperationType()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-insert-table-tool-tests",
            Guid.NewGuid().ToString("N")));
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Existing document content"
                }
            ]
        });

        object result = new TableTools(workflow).InsertTable(new InsertTableRequest
        {
            SessionId = created.SessionId,
            Rows =
            [
                ["A", "B", "C", "D", "E"],
                ["1A", "1B", "1C", "1D", "1E"],
                ["2A", "2B", "2C", "2D", "2E"],
                ["3A", "3B", "3C", "3D", "3E"],
                ["4A", "4B", "4C", "4D", "4E"]
            ]
        });

        var response = Assert.IsType<Models.Responses.ApplyOperationsResponse>(result);
        var table = Assert.Single(workflow.GetDocumentTables(response.SessionId).Tables);
        Assert.Equal(created.SessionId, response.SessionId);
        Assert.Equal(TableCapabilityPack.AppendTable, Assert.Single(response.Results).Type);
        Assert.Equal(5, table.RowCount);
        Assert.Equal(5, table.ColumnCount);
    }

    [SkippableFact]
    public void FormatTableToolFormatsAllCellsCoveredByAnEditorSelection()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-format-selected-table-cells-tests",
            Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "95",
                    Rows = [["Alpha", "Beta"], ["Gamma", "Delta"]]
                }
            ]
        });
        string txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        int selectionStart;
        int selectionLength;
        using (var tx = new ServerTextControl())
        {
            tx.Create();
            tx.Load(txPath, StreamType.InternalUnicodeFormat);
            var table = tx.Tables.GetItem(95);
            var first = table.Cells.GetItem(1, 1);
            var last = table.Cells.GetItem(1, 2);
            selectionStart = first.Start - 1;
            selectionLength = (last.Start - 1 + last.Length) - selectionStart;
        }

        object result = new TableTools(workflow).FormatTable(new FormatTableRequest
        {
            SessionId = created.SessionId,
            Scope = "selectedCells",
            MatchText = "Alpha",
            NearTextPosition = selectionStart,
            SelectionLength = selectionLength,
            MatchCase = true,
            BackgroundColorHex = "#FF0000"
        });

        var response = Assert.IsType<Models.Responses.ApplyOperationsResponse>(result);
        var operation = Assert.Single(response.Results);
        Assert.Equal(2, operation.Metadata["cellCount"]);
        AssertTxTableCellBackColor(txPath, 95, 1, 1, "#FF0000");
        AssertTxTableCellBackColor(txPath, 95, 1, 2, "#FF0000");
    }

    [SkippableFact]
    public void FormatTableToolResolvesTheSelectedTableAndFormatsItsHeader()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-format-selected-table-header-tests",
            Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "96",
                    Rows = [["First header", "Value"], ["First marker", "One"]]
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Between tables"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "97",
                    Rows = [["Second header", "Value"], ["Second marker", "Two"]]
                }
            ]
        });
        var match = Assert.Single(workflow.SearchTextRanges(
            created.SessionId,
            "Second marker",
            matchCase: true).Matches);

        object result = new TableTools(workflow).FormatTable(new FormatTableRequest
        {
            SessionId = created.SessionId,
            Scope = "header",
            MatchText = "Second marker",
            NearTextPosition = match.Start,
            MatchCase = true,
            BackgroundColorHex = "#008000"
        });

        var response = Assert.IsType<Models.Responses.ApplyOperationsResponse>(result);
        var operation = Assert.Single(response.Results);
        Assert.Equal("97", operation.Metadata["tableId"]);
        Assert.Equal(2, operation.Metadata["cellCount"]);
        string txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        AssertTxTableCellBackColor(txPath, 97, 1, 1, "#008000");
        AssertTxTableCellBackColor(txPath, 97, 1, 2, "#008000");
    }

    [SkippableFact]
    public void LiveTableInspectionCountsTablesAndAddsRowsToTheSecondTable()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-live-table-inspection-tests",
            Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "98",
                    Rows = [["First", "Table"], ["A", "B"]]
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Between tables"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "99",
                    Rows = [["Second", "Table"], ["C", "D"]]
                }
            ]
        });

        var exported = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = created.SessionId,
            Format = "docx"
        });
        var imported = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = exported.Base64Document,
            SourceFormat = "docx"
        });

        DocumentTablesResponse before = workflow.GetDocumentTables(imported.SessionId);
        Assert.Equal(2, before.TableCount);
        Assert.Equal([1, 2], before.Tables.Select(table => table.TableNumber));
        Assert.All(before.Tables, table => Assert.Equal(2, table.RowCount));
        Assert.Equal(2, workflow.GetDocumentStructure(imported.SessionId).TableCount);

        object result = new TableTools(workflow).AddTableRows(new AddTableRowsRequest
        {
            SessionId = imported.SessionId,
            TableNumber = 2,
            Count = 5
        });

        var response = Assert.IsType<Models.Responses.ApplyOperationsResponse>(result);
        Assert.Equal(5, response.Results.Count);
        DocumentTablesResponse after = workflow.GetDocumentTables(imported.SessionId);
        Assert.Equal(2, after.TableCount);
        Assert.Equal(2, after.Tables[0].RowCount);
        Assert.Equal(7, after.Tables[1].RowCount);
    }

    [SkippableFact]
    public void InspectDocumentFocusesUploadedMarkdownAroundQuestionTerms()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-inspect-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = "# Services Agreement\n\nAgreement Date: September 4, 2026\n\n## Payment\n\nInvoices are due within 30 days.";
        var loaded = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            SourceFormat = "md"
        });

        var inspection = workflow.InspectDocument(new InspectDocumentRequest
        {
            SessionId = loaded.SessionId,
            Query = "What is the given agreement date on this contract?",
            ContextParagraphs = 0
        });

        Assert.True(inspection.MatchCount > 0);
        Assert.Contains(inspection.Paragraphs, paragraph =>
            paragraph.Text.Contains("September 4, 2026", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void InspectDocumentPagesEveryParagraphWithoutRepeatingContent()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-inspect-paging-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = string.Join(
            "\n\n",
            Enumerable.Range(0, 12).Select(index =>
                $"Paragraph {index}: {new string((char)('a' + index), 280)}"));
        var loaded = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            SourceFormat = "md"
        });

        var indexes = new List<int>();
        int start = 0;
        int totalParagraphs = 0;
        while (true)
        {
            DocumentInspectionResponse page = workflow.InspectDocument(new InspectDocumentRequest
            {
                SessionId = loaded.SessionId,
                StartParagraphIndex = start,
                ContextParagraphs = 0,
                MaxCharacters = 1_000
            });
            totalParagraphs = page.TotalParagraphs;
            Assert.Equal(page.Paragraphs.Sum(paragraph => paragraph.Text.Length), page.ReturnedCharacters);
            indexes.AddRange(page.Paragraphs.Select(paragraph => paragraph.Index));
            if (!page.Truncated)
            {
                Assert.Null(page.NextParagraphIndex);
                break;
            }

            Assert.True(page.NextParagraphIndex > start);
            start = page.NextParagraphIndex!.Value;
        }

        Assert.True(totalParagraphs >= 12);
        Assert.Equal(Enumerable.Range(0, totalParagraphs), indexes);
        Assert.Equal(indexes.Count, indexes.Distinct().Count());
    }

    [SkippableFact]
    public void ClassifyDocumentReturnsLegalCategoryAndActionsWithoutMutatingDocument()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-classification-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = "# Mutual Nondisclosure Agreement\n\nThis agreement is between Party A and Party B. Confidential information is subject to warranty, liability, termination, and governing law clauses.";
        var loaded = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            SourceFormat = "md"
        });

        DocumentCategoryResponse category = workflow.ClassifyDocument(new ClassifyDocumentRequest
        {
            SessionId = loaded.SessionId
        });

        Assert.Equal("Legal", category.Category);
        Assert.InRange(category.Confidence, 0.55, 1);
        Assert.Contains(category.SuggestedActions, action => action.Id == "risk-assessment");
        Assert.Contains(category.SuggestedActions, action => action.Id == "obligations");
    }

    [SkippableFact]
    public void ClassifyDocumentReturnsTransportationCategoryAndActions()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-transportation-classification-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = "# Regional Freight Operations Plan\n\nThe transportation network coordinates fleet vehicles, carrier capacity, warehouse handoffs, shipment routes, cargo tracking, and final delivery milestones.";
        var loaded = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            SourceFormat = "md"
        });

        DocumentCategoryResponse category = workflow.ClassifyDocument(new ClassifyDocumentRequest
        {
            SessionId = loaded.SessionId
        });

        Assert.Equal("Transportation", category.Category);
        Assert.Contains(category.SuggestedActions, action => action.Id == "shipment-status");
        Assert.Contains(category.SuggestedActions, action => action.Id == "route-risks");
    }

    [SkippableFact]
    public void ApplyDocumentPresetStylesMapsImportedMarkdownHierarchyAndPreservesContent()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-imported-style-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = "# Document Title\n\nIntro text.\n\n## First Heading\n\nBody paragraph.\n\n### Second Heading\n\n- Item one\n- Item two";
        var loaded = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            SourceFormat = "md"
        });

        ApplyDocumentPresetStylesResponse styled = workflow.ApplyDocumentPresetStyles(
            new ApplyDocumentPresetStylesRequest { SessionId = loaded.SessionId });
        DocumentInspectionResponse inspection = workflow.InspectDocument(
            new InspectDocumentRequest { SessionId = loaded.SessionId });
        DocumentExportResponse export = workflow.CreateDocumentExport(new CreateDocumentExportRequest
        {
            SessionId = loaded.SessionId,
            Format = "tx"
        });
        var file = workflow.GetDocumentExport(export.SessionId, export.ExportId);

        Assert.Equal(loaded.SessionId, styled.SessionId);
        Assert.True(styled.PageLayoutApplied);
        Assert.Equal(inspection.TotalParagraphs, styled.ParagraphsStyled);
        Assert.Empty(styled.Warnings);
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "Document Title" && paragraph.StyleName == "Title");
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "Intro text." && paragraph.StyleName == "Body");
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "First Heading" && paragraph.StyleName == "Heading");
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "Second Heading" && paragraph.StyleName == "Heading2");
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text.Contains("Item one", StringComparison.Ordinal));
        Assert.DoesNotContain(inspection.Paragraphs, paragraph => paragraph.Text.Contains('#'));
        AssertTxSectionLayout(
            file.Path,
            sectionIndex: 0,
            expectedWidthTwips: 12240,
            expectedHeightTwips: 15840,
            expectedLeftMarginTwips: 1152,
            expectedTopMarginTwips: 1080,
            expectedLandscape: false);
    }

    [SkippableFact]
    public void CreateDocumentFromMarkdownBuildsAndStylesInvoiceInOneEnginePass()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-markdown-create-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = """
            # Invoice

            ## Bill To

            Contoso Ltd.

            ## Line Items

            | Description | Quantity | Price | Amount |
            | --- | ---: | ---: | ---: |
            | Consulting | 2 | $100.00 | $200.00 |
            | Support | 1 | $50.00 | $50.00 |
            | Hosting | 1 | $25.00 | $25.00 |

            **Subtotal:** $275.00
            **Tax:** $22.00
            **Total:** $297.00

            ### Payment Terms

            Payment is due within 30 days.
            """;

        CreateDocumentFromMarkdownResponse created = workflow.CreateDocumentFromMarkdown(
            new CreateDocumentFromMarkdownRequest { Markdown = markdown });
        DocumentInspectionResponse inspection = workflow.InspectDocument(
            new InspectDocumentRequest { SessionId = created.SessionId });
        DocumentExportResponse export = workflow.CreateDocumentExport(new CreateDocumentExportRequest
        {
            SessionId = created.SessionId,
            Format = "tx"
        });
        var file = workflow.GetDocumentExport(export.SessionId, export.ExportId);

        Assert.NotEmpty(created.SessionId);
        Assert.True(created.PageLayoutApplied);
        Assert.Equal(1, created.TablesStyled);
        Assert.Empty(created.Warnings);
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "Invoice" && paragraph.StyleName == "Title");
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "Bill To" && paragraph.StyleName == "Heading");
        Assert.Contains(inspection.Paragraphs, paragraph => paragraph.Text == "Payment Terms" && paragraph.StyleName == "Heading2");

        TxTextControlLicensing.Configure();
        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(file.Path, StreamType.InternalUnicodeFormat);
        TXTextControl.Table table = Assert.Single(tx.Tables.Cast<TXTextControl.Table>());
        Assert.Equal("Description", table.Cells.GetItem(1, 1).Text);
        Assert.Equal("$25.00", table.Cells.GetItem(4, 4).Text);
        Assert.Equal(
            ColorTranslator.FromHtml("#163A5F").ToArgb(),
            table.Cells.GetItem(1, 1).CellFormat.BackColor.ToArgb());
        table.Cells.GetItem(1, 1).Select();
        Assert.True(tx.Selection.Bold);
    }

    [SkippableFact]
    public void EditDocumentReplacesOneParagraphWithoutRecreatingItsSession()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-edit-tests",
            Guid.NewGuid().ToString("N")));
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "First paragraph" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Old payment paragraph" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Final paragraph" }
            ]
        });

        var edited = workflow.EditDocument(new EditDocumentRequest
        {
            SessionId = created.SessionId,
            ParagraphIndex = 1,
            ReplacementText = "New payment paragraph"
        });
        var paragraphs = workflow.InspectDocument(new InspectDocumentRequest
        {
            SessionId = created.SessionId
        }).Paragraphs;

        Assert.Equal(created.SessionId, edited.SessionId);
        Assert.Equal("paragraph", edited.TargetKind);
        Assert.Equal(1, edited.EditsApplied);
        Assert.Contains(paragraphs, paragraph => paragraph.Text.Contains("First paragraph", StringComparison.Ordinal));
        Assert.Contains(paragraphs, paragraph => paragraph.Text.Contains("New payment paragraph", StringComparison.Ordinal));
        Assert.Contains(paragraphs, paragraph => paragraph.Text.Contains("Final paragraph", StringComparison.Ordinal));
        Assert.DoesNotContain(paragraphs, paragraph => paragraph.Text.Contains("Old payment paragraph", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void EditDocumentRejectsAStaleCharacterRangeAndReportsVerifiedSuccess()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-stale-range-tests",
            Guid.NewGuid().ToString("N")));
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Term and survival" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Unrelated paragraph" }
            ]
        });
        string text = workflow.GetText(created.SessionId).Text;
        int start = text.IndexOf("Term and survival", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var stale = Assert.Throws<InvalidOperationException>(() => workflow.EditDocument(new EditDocumentRequest
        {
            SessionId = created.SessionId,
            Start = start,
            Length = "Term and survival".Length,
            ExpectedText = "Different text",
            ReplacementText = "Sample paragraph"
        }));
        Assert.Contains("expectedText", stale.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Term and survival", workflow.GetText(created.SessionId).Text, StringComparison.Ordinal);

        var edited = workflow.EditDocument(new EditDocumentRequest
        {
            SessionId = created.SessionId,
            Start = start,
            Length = "Term and survival".Length,
            ExpectedText = "Term and survival",
            ReplacementText = "Sample paragraph"
        });

        Assert.True(edited.Changed);
        Assert.Equal(1, edited.EditsApplied);
        Assert.True(edited.Revision > 0);
        Assert.Contains("Sample paragraph", workflow.GetText(created.SessionId).Text, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void ConvertDocumentUsesDirectMarkdownToDocxPathAndReturnsDownloadArtifact()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-convert-tests",
            Guid.NewGuid().ToString("N")));
        string markdown = "# Conversion Test\n\nThis content must remain unchanged.";

        var export = workflow.ConvertDocument(new ConvertDocumentRequest
        {
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            SourceFormat = "md",
            OutputFormat = "docx",
            FileName = "converted.docx"
        });
        var file = workflow.GetDocumentExport(export.SessionId, export.ExportId);

        Assert.Equal("docx", export.Format);
        Assert.Equal("converted.docx", export.FileName);
        Assert.True(export.ByteCount > 0);
        AssertDocxContainsText(file.Path, "Conversion Test");
        AssertDocxContainsText(file.Path, "This content must remain unchanged.");
    }

    [SkippableFact]
    public void EmptyDocumentReceivesConfiguredDefaultPageLayoutImmediately()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-default-layout-tests",
            Guid.NewGuid().ToString("N")));

        var created = workflow.CreateDocument();
        var model = workflow.GetDocumentModel(created.SessionId).Document;

        Assert.Equal("Letter", Assert.Single(model.Sections).PageLayout?.PageSize);
        Assert.Equal(57.6f, model.Sections[0].PageLayout?.MarginLeft);
    }

    [SkippableFact]
    public void CreateDocumentExportWritesPdfWithoutBase64()
    {
        SkipIfTxLicenseIsMissing();
        string artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-export-tests",
            Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var session = workflow.CreateDocument();

        var export = workflow.CreateDocumentExport(new CreateDocumentExportRequest
        {
            SessionId = session.SessionId,
            Format = "pdf",
            FileName = "streamed-export.pdf"
        });
        var file = workflow.GetDocumentExport(session.SessionId, export.ExportId);

        Assert.Equal("application/pdf", file.MimeType);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, File.ReadAllBytes(file.Path)[..4]);
        Assert.Equal(new FileInfo(file.Path).Length, export.ByteCount);
    }

    [SkippableFact]
    public void CreateDocumentExportUsesDocumentTitleForDefaultFileName()
    {
        SkipIfTxLicenseIsMissing();
        string artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-titled-export-tests",
            Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var rendered = RenderDocumentModelOrSkipIfUnlicensed(workflow, new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = new Neutral.Document
            {
                Title = "Product Launch Review - Meeting Agenda",
                Sections = [new Neutral.Section()]
            }
        });

        var export = workflow.CreateDocumentExport(new CreateDocumentExportRequest
        {
            SessionId = rendered.SessionId,
            Format = "pdf"
        });

        Assert.Equal("Product Launch Review - Meeting Agenda.pdf", export.FileName);
    }

    [SkippableFact]
    public void LoadFromBase64ImportsPdfContent()
    {
        SkipIfTxLicenseIsMissing();
        var workflow = CreateWorkflow(Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-pdf-import-tests",
            Guid.NewGuid().ToString("N")));
        var source = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "PDF upload integration test"
                }
            ]
        });
        var pdf = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = source.SessionId,
            Format = "pdf"
        });

        var imported = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = pdf.Base64Document
        });

        Assert.Contains("PDF upload integration test", workflow.GetText(imported.SessionId).Text);
    }

    [SkippableFact]
    public void LoadingIdenticalContentIntoTheSameSessionDoesNotRewriteArtifacts()
    {
        SkipIfTxLicenseIsMissing();
        string artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "tx-mcp-identical-import-tests",
            Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var source = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Identical upload performance test"
                }
            ]
        });
        var exported = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = source.SessionId,
            Format = "pdf"
        });
        var imported = workflow.LoadFromBase64(new LoadFromBase64Request
        {
            Data = exported.Base64Document
        });

        string sessionDirectory = Path.Combine(artifactRoot, "sessions", imported.SessionId);
        string documentPath = Path.Combine(sessionDirectory, "document.tx");
        string statePath = Path.Combine(sessionDirectory, "document.state.json");
        DateTime marker = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(documentPath, marker);
        File.SetLastWriteTimeUtc(statePath, marker);

        var repeated = workflow.LoadFromBase64(
            new LoadFromBase64Request { Data = exported.Base64Document },
            imported.SessionId);

        Assert.Equal(imported.SessionId, repeated.SessionId);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(documentPath));
        Assert.Equal(marker, File.GetLastWriteTimeUtc(statePath));
        string importedText = workflow.GetText(imported.SessionId).Text;
        Assert.Contains("Identical upload performance", importedText);
        Assert.Contains("test", importedText);
    }

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
    public void RenderDocumentModelRepairsWeakInvoiceAndExportsProfessionalPdf()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        DocumentBlock Paragraph(string text) => new()
        {
            Type = "paragraph",
            Paragraph = new Neutral.Paragraph { Text = text }
        };

        var response = RenderDocumentModelOrSkipIfUnlicensed(
            workflow,
            new RenderDocumentModelRequest
            {
                CreateIfMissing = true,
                Document = new Document
                {
                    Title = "INVOICE",
                    Sections =
                    [
                        new Neutral.Section
                        {
                            Blocks =
                            [
                                Paragraph("Invoice Number: INV-2026-1042"),
                                Paragraph("Date: September 4, 2026"),
                                Paragraph("Due Date: October 4, 2026"),
                                Paragraph("Bill To: Northwind Traders"),
                                new DocumentBlock
                                {
                                    Type = "table",
                                    Table = new Neutral.Table
                                    {
                                        Rows =
                                        [
                                            new Neutral.TableRow
                                            {
                                                Cells =
                                                [
                                                    CreateTextCell("header-description", "Description"),
                                                    CreateTextCell("header-quantity", "Quantity"),
                                                    CreateTextCell("header-price", "Unit Price"),
                                                    CreateTextCell("header-amount", "Amount")
                                                ]
                                            }
                                        ]
                                    }
                                },
                                Paragraph("Professional Consulting Services | 40 | $150.00 | $6,000.00"),
                                Paragraph("Implementation Support | 12 | $125.00 | $1,500.00"),
                                Paragraph("Team Training | 4 | $100.00 | $400.00"),
                                Paragraph("Subtotal: $7,900.00"),
                                Paragraph("Tax (8%): $632.00"),
                                Paragraph("Total: $8,532.00"),
                                Paragraph("Payment Terms: Payment is due within 30 days. Thank you for your business.")
                            ]
                        }
                    ]
                }
            });

        Assert.Contains(response.Warnings, warning => warning.Contains("Recovered 3", StringComparison.Ordinal));
        var model = workflow.GetDocumentModel(response.SessionId).Document;
        var table = Assert.Single(model.Sections[0].Blocks, block => block.Table is not null).Table!;
        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(4, table.ColumnWidths.Count);
        Assert.InRange(table.ColumnWidths.Sum(width => width ?? 0), 496.5f, 497.1f);
        Assert.True(table.ColumnWidths[0] > 250);
        Assert.Equal("right", table.Rows[1].Cells[3].CellStyle?.HorizontalAlignment);
        Assert.Contains(model.Sections[0].Blocks, block =>
            block.Paragraph?.StyleName == "Heading2"
            && string.Concat(block.Paragraph.Runs.Select(run => run.Text)) == "Payment Terms");

        var txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxTableCellPadding(txPath, 10, 1, 1, left: 120, right: 120, top: 100, bottom: 100);
        AssertTxTableCellParagraphAlignment(txPath, 10, 2, 4, HorizontalAlignment.Right);

        var pdf = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "pdf"
        });
        var pdfPath = Path.Combine(artifactRoot, "professional-invoice-regression.pdf");
        File.WriteAllBytes(pdfPath, Convert.FromBase64String(pdf.Base64Document));
        Assert.True(new FileInfo(pdfPath).Length > 5_000, $"Expected a non-empty styled PDF at {pdfPath}.");

        var docx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = response.SessionId,
            Format = "docx"
        });
        var docxPath = Path.Combine(artifactRoot, "professional-invoice-regression.docx");
        File.WriteAllBytes(docxPath, Convert.FromBase64String(docx.Base64Document));
        AssertDocxContainsTableText(
            docxPath,
            ["Description", "Professional Consulting Services", "Implementation Support", "Team Training"]);
        AssertDocxTableRowCount(docxPath, 4);
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
    public void FormatTextRangeAttachesSelectionBeforeApplyingCharacterFormatting()
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
                    Text = "untouched prefix"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "confidential information remains protected"
                }
            ]
        });

        var match = Assert.Single(workflow.SearchTextRanges(
            response.SessionId,
            "information",
            matchCase: true,
            wholeWord: true).Matches);

        workflow.FormatText(response.SessionId, new FormatTextRequest
        {
            Start = match.Start,
            Length = match.Length,
            Bold = true,
            ColorHex = "#008000"
        });

        string txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxTextFormat(txPath, "information", expectedBold: true, expectedHex: "#008000");
        AssertTxTextFormat(txPath, "untouched prefix", expectedBold: false, expectedHex: "#1F2937");
    }

    [SkippableFact]
    public void FormatTextRejectsARequestWithNoCharacterFormattingProperties()
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
                    Text = "Paragraph"
                }
            ]
        });

        var exception = Assert.Throws<ArgumentException>(() =>
            workflow.FormatText(response.SessionId, new FormatTextRequest { ParagraphIndex = 0 }));

        Assert.Contains("format_paragraph", exception.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void FormatTextOccurrencesFormatsEveryServerMatchInOneOperation()
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
                    Text = "untouched heading"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "information and Information, but informational stays unchanged"
                },
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "92",
                    Rows = [["Label", "information"]]
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatTextOccurrences,
                    MatchText = "information",
                    WholeWord = true,
                    Style = new TextStyleDefinition
                    {
                        Bold = true,
                        ColorHex = "#008000"
                    }
                }
            ]
        });

        var result = response.Results.Last();
        Assert.Equal(3, Convert.ToInt32(result.Metadata["occurrenceCount"]));
        string txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxAllTextMatchesFormat(
            txPath,
            "information",
            wholeWord: true,
            expectedCount: 3,
            expectedBold: true,
            expectedHex: "#008000");
        AssertTxTextFormat(txPath, "untouched heading", expectedBold: false, expectedHex: "#1F2937");
        AssertTxTextFormat(txPath, "informational", expectedBold: false, expectedHex: "#1F2937");
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

        AssertTxTableCellBackColor(txPath, 15, 1, 1, "#163A5F");
        AssertTxTableCellBackColor(txPath, 15, 2, 1, "#FFFFFF");
        AssertTxTableCellBackColor(txPath, 15, 3, 1, "#F4F7FA");
        AssertTxTableCellBorders(txPath, 15, 1, 1, 10, "#D0D5DD");
        AssertTxTableCellBorders(txPath, 15, 3, 2, 10, "#D0D5DD");
        AssertTxTableCellTextFormat(txPath, 15, 1, 1, expectedFontSize: 200, expectedHex: "#FFFFFF", expectedBold: true);
        AssertTxTableCellTextFormat(txPath, 15, 2, 2, expectedFontSize: 200, expectedHex: "#111827");

        var modelTable = workflow.GetDocumentModel(response.SessionId).Document.Sections[0].Blocks[0].Table;
        Assert.NotNull(modelTable);
        Assert.All(modelTable!.Rows[0].Cells, cell => Assert.True(cell.Blocks[0].Paragraph?.Runs[0].Style?.Bold));
        Assert.All(modelTable.Rows[0].Cells, cell => Assert.Equal("#163A5F", cell.CellStyle?.BackgroundColorHex));
        Assert.All(modelTable.Rows[1].Cells, cell => Assert.Equal("#FFFFFF", cell.CellStyle?.BackgroundColorHex));
        Assert.All(modelTable.Rows[2].Cells, cell => Assert.Equal("#F4F7FA", cell.CellStyle?.BackgroundColorHex));
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
    public void MergeFieldsCanReplaceEveryMatchingPartyNameWithRealFields()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = "Party A discloses information to Party A affiliates."
                }
            ]
        });

        var changed = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "PartyAName",
                    FieldText = "Party A",
                    MatchText = "Party A",
                    ReplaceAll = true,
                    MatchCase = true,
                    WholeWord = true
                }
            ]
        });

        var result = Assert.Single(changed.Results);
        Assert.Equal(2, Convert.ToInt32(result.Metadata["insertedFieldCount"]));
        Assert.Equal(2, workflow.GetTemplateMergeFields(created.SessionId).FieldCount);
        Assert.All(workflow.GetTemplateMergeFields(created.SessionId).Fields,
            field => Assert.Equal("PartyAName", field.Name));
        Assert.DoesNotContain("{{", workflow.GetText(created.SessionId).Text);
        Assert.Contains("Party A discloses information to Party A affiliates.", workflow.GetText(created.SessionId).Text);

        var txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        AssertTxApplicationFieldCount(txPath, 2);
    }

    [SkippableFact]
    public void FocusedInsertMergeFieldToolDoesNotRequireAnOperationDiscriminator()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations = [new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Party B accepts." }]
        });

        object raw = new FieldTools(workflow).InsertMergeField(new InsertMergeFieldRequest
        {
            SessionId = created.SessionId,
            FieldName = "PartyBName",
            FieldText = "Party B",
            MatchText = "Party B",
            ReplaceAll = true
        });

        var result = Assert.IsType<ApplyOperationsResponse>(raw);
        Assert.Equal(created.SessionId, result.SessionId);
        Assert.Equal(1, workflow.GetTemplateMergeFields(created.SessionId).FieldCount);
        AssertTxMergeField(
            Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx"),
            "PartyBName",
            "Party B");
    }

    [SkippableFact]
    public void MergeFieldCanReplaceAnExpectedCharacterRange()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations = [new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Agreement between Acme Corporation and Buyer." }]
        });
        string text = workflow.GetText(created.SessionId).Text;
        int start = text.IndexOf("Acme Corporation", StringComparison.Ordinal);

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "PartyAName",
                    FieldText = "Party A",
                    Start = start,
                    Length = "Acme Corporation".Length,
                    ExpectedText = "Acme Corporation"
                }
            ]
        });

        Assert.Contains("Agreement between Party A and Buyer.", workflow.GetText(created.SessionId).Text);
        AssertTxMergeField(Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx"), "PartyAName", "Party A");
    }

    [SkippableFact]
    public void SearchRangeCanInsertMergeFieldIntoTableTextWithoutCoordinateDrift()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        const string selectedText = "Riverbend Technologies GmbH";
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "72",
                    Rows =
                    [
                        ["Party A", "Northstar Innovations, Inc., a Delaware corporation"],
                        ["Party B", selectedText + ", a German limited liability company"],
                        ["Effective date", "1 September 2026"]
                    ]
                }
            ]
        });

        var range = Assert.Single(workflow.SearchTextRanges(
            created.SessionId,
            selectedText,
            matchCase: true,
            wholeWord: false).Matches);
        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "PartyBName",
                    FieldText = selectedText,
                    Start = range.Start,
                    Length = range.Length,
                    ExpectedText = selectedText
                }
            ]
        });

        string txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        AssertTxMergeField(txPath, "PartyBName", selectedText);
        AssertTxTableCell(txPath, 72, 2, 2, selectedText + ", a German limited liability company");
    }

    [SkippableFact]
    public void NearTextPositionResolvesTheClosestServerSideMatch()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "73",
                    Rows = [["Party A", "Company"], ["Party B", "Company"]]
                }
            ]
        });
        var matches = workflow.SearchTextRanges(created.SessionId, "Company", matchCase: true).Matches;
        Assert.Equal(2, matches.Count);

        var changed = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "PartyBName",
                    FieldText = "Company",
                    MatchText = "Company",
                    MatchCase = true,
                    NearTextPosition = matches[1].Start + 6
                }
            ]
        });

        var result = Assert.Single(changed.Results);
        var ranges = Assert.IsType<List<Dictionary<string, object?>>>(result.Metadata["ranges"]);
        Assert.Equal(matches[1].Start, Convert.ToInt32(Assert.Single(ranges)["start"]));
        Assert.Equal(1, workflow.GetTemplateMergeFields(created.SessionId).FieldCount);
    }

    [SkippableFact]
    public void MergeFieldsCanBeInsertedUpdatedAndInspectedInHeaders()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Agreement" },
                new DocumentOperation { Type = HeaderFooterCapabilityPack.SetHeaderFooter, HeaderFooterType = "header", Text = "Prepared for " },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeField,
                    FieldName = "CustomerName",
                    FieldText = "Customer",
                    HeaderFooterType = "header",
                    Placement = "end"
                },
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.UpdateMergeField,
                    FieldName = "CustomerName",
                    FieldText = "Customer Name"
                }
            ]
        });

        var field = Assert.Single(workflow.GetTemplateMergeFields(created.SessionId).Fields);
        Assert.Equal("sections[0].header", field.Location);
        Assert.Equal("Customer Name", field.Text);
        AssertTxHeaderFooterMergeField(
            Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx"),
            HeaderFooterType.Header,
            "CustomerName",
            "Customer Name");
    }

    [SkippableFact]
    public void FormFieldsCanReplaceEveryMatchingPlaceholder()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations = [new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Sign here and initial here." }]
        });

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendFormField,
                    FieldName = "Signer",
                    FormFieldType = "text",
                    Text = "here",
                    MatchText = "here",
                    ReplaceAll = true,
                    MatchCase = true,
                    WholeWord = true
                }
            ]
        });

        var fields = workflow.GetTemplateFormFields(created.SessionId);
        Assert.Equal(2, fields.FieldCount);
        Assert.All(fields.Fields, field => Assert.Equal("Signer", field.Name));
    }

    [SkippableFact]
    public void MergeBlockCanWrapAnExpectedCharacterRange()
    {
        SkipIfTxLicenseIsMissing();
        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations = [new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Repeat this clause." }]
        });
        int start = workflow.GetText(created.SessionId).Text.IndexOf("this clause", StringComparison.Ordinal);

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            Operations =
            [
                new DocumentOperation
                {
                    Type = FieldsCapabilityPack.AppendMergeBlock,
                    BlockName = "Clauses",
                    Start = start,
                    Length = "this clause".Length,
                    ExpectedText = "this clause"
                }
            ]
        });

        AssertTxMergeBlock(Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx"), "Clauses");
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

        AssertTxParagraphSpacing(txPath, collectionIndex: 1, expectedSpaceAfterTwips: 120);
        AssertTxParagraphSpacing(txPath, collectionIndex: 2, expectedSpaceAfterTwips: 100);
        AssertTxParagraphLineSpacing(txPath, collectionIndex: 2, expectedLineSpacing: 108);
        AssertTxParagraphFormattingStyle(txPath, collectionIndex: 1, expectedStyleName: "Heading");
        AssertTxParagraphFormattingStyle(txPath, collectionIndex: 2, expectedStyleName: "Body");

        var model = workflow.GetDocumentModel(response.SessionId).Document;
        Assert.Equal("Heading", model.Sections[0].Blocks[0].Paragraph?.StyleName);
        Assert.Equal("Body", model.Sections[0].Blocks[1].Paragraph?.StyleName);
        Assert.Equal(6, model.Sections[0].Blocks[0].Paragraph?.ParagraphStyle?.SpaceAfter);
        Assert.Equal(5, model.Sections[0].Blocks[1].Paragraph?.ParagraphStyle?.SpaceAfter);
        Assert.Equal(1.08f, model.Sections[0].Blocks[1].Paragraph?.ParagraphStyle?.LineSpacing);
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
    public void FormatParagraphsResolvesSelectedParagraphByTextAndNearestPosition()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        const string repeatedText = "Mutual Nondisclosure Agreement";
        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = repeatedText },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "intervening text" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = repeatedText }
            ]
        });
        var matches = workflow.SearchTextRanges(response.SessionId, repeatedText, matchCase: true).Matches;
        Assert.Equal(2, matches.Count);

        var changed = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = response.SessionId,
            CreateIfMissing = false,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatParagraphs,
                    MatchText = $"  {repeatedText}\r\n",
                    MatchCase = true,
                    NearTextPosition = matches[1].Start + 4,
                    Paragraph = new Neutral.ParagraphStyleDefinition { Alignment = "center" }
                }
            ]
        });

        var result = Assert.Single(changed.Results);
        var paragraphIndexes = Assert.IsType<List<int>>(result.Metadata["paragraphIndexes"]);
        Assert.Equal([2], paragraphIndexes);
        string txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxParagraphAlignment(txPath, 1, HorizontalAlignment.Left);
        AssertTxParagraphAlignment(txPath, 3, HorizontalAlignment.Center);
    }

    [SkippableFact]
    public void FormatParagraphsCanResolveAParagraphInsideATable()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        const string selectedText = "Payment terms are net 30 days";
        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = TableCapabilityPack.AppendTable,
                    TableId = "94",
                    Rows = [["Terms", selectedText]]
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatParagraphs,
                    MatchText = selectedText,
                    MatchCase = true,
                    NearTextPosition = 0,
                    Paragraph = new Neutral.ParagraphStyleDefinition { Alignment = "center" }
                }
            ]
        });

        string txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxTableCellParagraphAlignment(txPath, 94, 1, 2, HorizontalAlignment.Center);
        var model = workflow.GetDocumentModel(response.SessionId).Document;
        var table = Assert.Single(model.Sections[0].Blocks).Table;
        Assert.NotNull(table);
        Assert.Equal("center", table.Rows[0].Cells[1].Blocks[0].Paragraph?.ParagraphStyle?.Alignment);
    }

    [SkippableFact]
    public void FormatParagraphsSupportsAnInclusiveParagraphRange()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var response = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "First" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Second" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Third" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, Text = "Fourth" },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatParagraphs,
                    StartParagraphIndex = 1,
                    EndParagraphIndex = 2,
                    Paragraph = new Neutral.ParagraphStyleDefinition { Alignment = "justify" }
                }
            ]
        });

        string txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxParagraphAlignment(txPath, 1, HorizontalAlignment.Left);
        AssertTxParagraphAlignment(txPath, 2, HorizontalAlignment.Justify);
        AssertTxParagraphAlignment(txPath, 3, HorizontalAlignment.Justify);
        AssertTxParagraphAlignment(txPath, 4, HorizontalAlignment.Left);
    }

    [SkippableFact]
    public void ApplyParagraphStyleCanResolveTargetByMatchingText()
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
                    StyleName = "Body",
                    Text = "Body paragraph"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    StyleName = "Body",
                    Text = "Payment Terms"
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.ApplyStyleToParagraph,
                    MatchText = "Payment Terms",
                    MatchCase = true,
                    StyleName = "Heading"
                }
            ]
        });

        string txPath = Path.Combine(artifactRoot, "sessions", response.SessionId, "document.tx");
        AssertTxParagraphFormattingStyle(txPath, collectionIndex: 1, expectedStyleName: "Body");
        AssertTxParagraphFormattingStyle(txPath, collectionIndex: 2, expectedStyleName: "Heading");
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
    public void RenderDocumentModelPreservesParagraphBeforeTrailingSection()
    {
        SkipIfTxLicenseIsMissing();

        var artifactRoot = GetArtifactRoot();
        var workflow = CreateWorkflow(artifactRoot);
        var document = new Document
        {
            Title = "Hello AI!",
            Sections =
            [
                new Neutral.Section
                {
                    Blocks =
                    [
                        new DocumentBlock
                        {
                            Type = "paragraph",
                            Paragraph = new Neutral.Paragraph
                            {
                                Runs = [new Run { Text = "Hello AI!" }]
                            }
                        }
                    ]
                },
                new Neutral.Section()
            ]
        };

        var response = RenderDocumentModelOrSkipIfUnlicensed(workflow, new RenderDocumentModelRequest
        {
            CreateIfMissing = true,
            Document = document
        });

        string text = workflow.GetText(response.SessionId).Text;
        Assert.Equal(2, text.Split("Hello AI!", StringSplitOptions.None).Length - 1);
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

    private static void AssertTxTableCellPadding(
        string txPath,
        int tableId,
        int row,
        int column,
        int left,
        int right,
        int top,
        int bottom)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        var cell = tx.Tables.GetItem(tableId).Cells.GetItem(row, column);

        Assert.Equal(left, cell.CellFormat.LeftTextDistance);
        Assert.Equal(right, cell.CellFormat.RightTextDistance);
        Assert.Equal(top, cell.CellFormat.TopTextDistance);
        Assert.Equal(bottom, cell.CellFormat.BottomTextDistance);
    }

    private static void AssertTxTableCellParagraphAlignment(
        string txPath,
        int tableId,
        int row,
        int column,
        HorizontalAlignment expectedAlignment)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        tx.Tables.GetItem(tableId).Cells.GetItem(row, column).Select();

        Assert.Equal(expectedAlignment, tx.Selection.ParagraphFormat.Alignment);
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

    private static void AssertTxTextFormat(
        string txPath,
        string text,
        bool expectedBold,
        string expectedHex)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        int start = tx.Find(text, 0, FindOptions.MatchCase);
        Assert.True(start >= 0, $"Text '{text}' was not found in the TX document.");
        tx.Selection = new Selection(start, text.Length);

        Assert.Equal(expectedBold, tx.Selection.Bold);
        Assert.Equal(ColorTranslator.FromHtml(expectedHex).ToArgb(), tx.Selection.ForeColor.ToArgb());
    }

    private static void AssertTxAllTextMatchesFormat(
        string txPath,
        string text,
        bool wholeWord,
        int expectedCount,
        bool expectedBold,
        string expectedHex)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        FindOptions options = wholeWord ? FindOptions.MatchWholeWord : (FindOptions)0;
        int count = 0;
        int searchStart = 0;
        while (true)
        {
            int start = tx.Find(text, searchStart, options);
            if (start < 0)
            {
                break;
            }

            tx.Selection = new Selection(start, text.Length);
            Assert.Equal(expectedBold, tx.Selection.Bold);
            Assert.Equal(ColorTranslator.FromHtml(expectedHex).ToArgb(), tx.Selection.ForeColor.ToArgb());
            count++;
            searchStart = start + text.Length;
        }

        Assert.Equal(expectedCount, count);
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

    private static void AssertTxParagraphLineSpacing(
        string txPath,
        int collectionIndex,
        int expectedLineSpacing)
    {
        TxTextControlLicensing.Configure();

        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);

        Assert.True(collectionIndex >= 1 && collectionIndex <= tx.Paragraphs.Count);
        Assert.Equal(expectedLineSpacing, tx.Paragraphs[collectionIndex].Format.LineSpacing);
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

    private static void AssertDocxTableRowCount(string docxPath, int expectedRowCount)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(docxPath);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("DOCX does not contain word/document.xml.");
        using var reader = new StreamReader(entry.Open());
        string documentXml = reader.ReadToEnd();
        int rowCount = System.Text.RegularExpressions.Regex.Matches(documentXml, "<w:tr(?:\\s|>)").Count;

        Assert.Equal(expectedRowCount, rowCount);
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

    [SkippableFact]
    public void UpdatingNamedStylePropagatesAndLiveInspectionReportsUsage()
    {
        SkipIfTxLicenseIsMissing();
        string artifactRoot = Path.Combine(GetArtifactRoot(), "style-update", Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition { Name = "Heading 1", FontSize = 18, Bold = true }
                },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading 1", Text = "First heading" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Heading 1", Text = "Second heading" }
            ]
        });

        object setStyleResult = new StyleTools(workflow).SetDocumentStyle(new SetDocumentStyleRequest
        {
            SessionId = created.SessionId,
            StyleName = "Heading 1",
            Text = new TextStyleDefinition
            {
                ColorHex = "#FF0000"
            },
            Paragraph = new ParagraphStyleDefinition
            {
                Alignment = "center",
                KeepWithNext = true
            }
        });
        var setStyleResponse = Assert.IsType<ApplyOperationsResponse>(setStyleResult);
        Assert.Equal("applied", Assert.Single(setStyleResponse.Results).Status);

        DocumentStylesResponse response = workflow.GetDocumentStyles(created.SessionId);
        StyleInspection heading = Assert.Single(response.Styles, style => style.Name == "Heading 1");
        Assert.Equal("#FF0000", heading.Text?.ColorHex);
        Assert.Equal(2, heading.UsageCount);

        string txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        Assert.Equal(Color.Red.ToArgb(), tx.ParagraphStyles.GetItem("Heading 1").ForeColor.ToArgb());
        Assert.Equal(HorizontalAlignment.Center, tx.ParagraphStyles.GetItem("Heading 1").ParagraphFormat.Alignment);
        Assert.True(tx.ParagraphStyles.GetItem("Heading 1").ParagraphFormat.KeepWithNext);
        Assert.Equal("Heading 1", tx.Paragraphs[1].FormattingStyle);
        Assert.Equal("Heading 1", tx.Paragraphs[2].FormattingStyle);
        tx.Paragraphs[1].Select();
        Assert.Equal(Color.Red.ToArgb(), tx.Selection.ForeColor.ToArgb());
        Assert.Equal(HorizontalAlignment.Center, tx.Paragraphs[1].Format.Alignment);
    }

    [SkippableFact]
    public void CreateStylesFromParagraphsGroupsEquivalentDirectFormatting()
    {
        SkipIfTxLicenseIsMissing();
        string artifactRoot = Path.Combine(GetArtifactRoot(), "style-derivation", Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Alpha direct formatting" },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Body", Text = "Beta direct formatting" },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatTextOccurrences,
                    MatchText = "Alpha direct formatting",
                    ReplaceAll = true,
                    Style = new TextStyleDefinition { Bold = true, ColorHex = "#006400" }
                },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.FormatTextOccurrences,
                    MatchText = "Beta direct formatting",
                    ReplaceAll = true,
                    Style = new TextStyleDefinition { Bold = true, ColorHex = "#006400" }
                }
            ]
        });

        object createStylesResult = new StyleTools(workflow).CreateStylesFromParagraphs(
            new CreateStylesFromParagraphsRequest
            {
                SessionId = created.SessionId,
                StyleNamePrefix = "Detected",
                MinimumOccurrences = 2,
                IncludeStyledParagraphs = true
            });
        var createStylesResponse = Assert.IsType<ApplyOperationsResponse>(createStylesResult);
        Assert.Equal("applied", Assert.Single(createStylesResponse.Results).Status);

        DocumentStylesResponse response = workflow.GetDocumentStyles(created.SessionId);
        StyleInspection detected = Assert.Single(response.Styles, style => style.Name.StartsWith("Detected ", StringComparison.Ordinal));
        Assert.Equal(2, detected.UsageCount);
        Assert.True(detected.Text?.Bold);
        Assert.Equal("#006400", detected.Text?.ColorHex);

        var exportedTx = workflow.GetAsBase64(new GetAsBase64Request
        {
            SessionId = created.SessionId,
            Format = "tx"
        });
        string reloadedTxPath = Path.Combine(artifactRoot, "reloaded-document.tx");
        File.WriteAllBytes(reloadedTxPath, Convert.FromBase64String(exportedTx.Base64Document));
        using (var reloadedTx = new ServerTextControl())
        {
            reloadedTx.Create();
            reloadedTx.Load(reloadedTxPath, StreamType.InternalUnicodeFormat);
            Assert.NotNull(DocumentOperationFormatter.FindParagraphStyle(reloadedTx, detected.Name));
            Assert.Equal(detected.Name, reloadedTx.Paragraphs[1].FormattingStyle);
            Assert.Equal(detected.Name, reloadedTx.Paragraphs[2].FormattingStyle);
        }

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            CreateIfMissing = false,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition { Name = detected.Name, ColorHex = "#FF0000" }
                }
            ]
        });
        string txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        tx.Paragraphs[1].Select();
        Assert.Equal(Color.Red.ToArgb(), tx.Selection.ForeColor.ToArgb());
    }

    [SkippableFact]
    public void RenameAndDeleteStylePreserveExplicitParagraphLinks()
    {
        SkipIfTxLicenseIsMissing();
        string artifactRoot = Path.Combine(GetArtifactRoot(), "style-lifecycle", Guid.NewGuid().ToString("N"));
        var workflow = CreateWorkflow(artifactRoot);
        var created = ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            CreateIfMissing = true,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DefineStyle,
                    Style = new TextStyleDefinition { Name = "Review", Italic = true }
                },
                new DocumentOperation { Type = BasicTextCapabilityPack.AppendParagraph, StyleName = "Review", Text = "Review me" },
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.RenameStyle,
                    StyleName = "Review",
                    NewStyleName = "Approved"
                }
            ]
        });

        StyleInspection approved = Assert.Single(
            workflow.GetDocumentStyles(created.SessionId).Styles,
            style => style.Name == "Approved");
        Assert.Equal(1, approved.UsageCount);

        ApplyOperationsOrSkipIfUnlicensed(workflow, new ApplyOperationsRequest
        {
            SessionId = created.SessionId,
            CreateIfMissing = false,
            Operations =
            [
                new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.DeleteStyle,
                    StyleName = "Approved",
                    ReplacementStyleName = "Body"
                }
            ]
        });

        Assert.DoesNotContain(workflow.GetDocumentStyles(created.SessionId).Styles, style => style.Name == "Approved");
        string txPath = Path.Combine(artifactRoot, "sessions", created.SessionId, "document.tx");
        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load(txPath, StreamType.InternalUnicodeFormat);
        Assert.Equal("Body", tx.Paragraphs[1].FormattingStyle);
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
            DefaultPageLayout = new PageLayoutDefinition
            {
                PageSize = "Letter",
                Orientation = "portrait",
                Unit = "in",
                MarginLeft = 0.8f,
                MarginRight = 0.8f,
                MarginTop = 0.75f,
                MarginBottom = 0.75f
            },
            StylePresets =
            [
                new TextStyleDefinition
                {
                    Name = "Title",
                    FontName = "Liberation Sans",
                    FontSize = 26,
                    FontSizeUnit = "pt",
                    Bold = true,
                    ColorHex = "#163A5F",
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 0,
                        SpaceAfter = 18,
                        Unit = "pt"
                    }
                },
                new TextStyleDefinition
                {
                    Name = "Heading",
                    FontName = "Liberation Sans",
                    FontSize = 16,
                    FontSizeUnit = "pt",
                    Bold = true,
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 16,
                        SpaceAfter = 6,
                        Unit = "pt"
                    }
                },
                new TextStyleDefinition
                {
                    Name = "Heading2",
                    FontName = "Liberation Sans",
                    FontSize = 12,
                    FontSizeUnit = "pt",
                    Bold = true,
                    ColorHex = "#274C6B",
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 12,
                        SpaceAfter = 5,
                        Unit = "pt"
                    }
                },
                new TextStyleDefinition
                {
                    Name = "Body",
                    FontName = "Liberation Sans",
                    FontSize = 10.5f,
                    FontSizeUnit = "pt",
                    Bold = false,
                    Italic = false,
                    Underline = false,
                    ColorHex = "#1F2937",
                    Paragraph = new ParagraphStyleDefinition
                    {
                        SpaceBefore = 0,
                        SpaceAfter = 5,
                        LineSpacing = 1.08f,
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
                        BackgroundColorHex = "#163A5F",
                        PaddingLeft = 6,
                        PaddingRight = 6,
                        PaddingTop = 5,
                        PaddingBottom = 5,
                        VerticalAlignment = "center",
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
                        PaddingLeft = 6,
                        PaddingRight = 6,
                        PaddingTop = 4,
                        PaddingBottom = 4,
                        VerticalAlignment = "center",
                        Border = new CellBorderDefinition
                        {
                            Width = 10,
                            ColorHex = "#D0D5DD"
                        }
                    },
                    AlternatingRowCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#F4F7FA",
                        PaddingLeft = 6,
                        PaddingRight = 6,
                        PaddingTop = 4,
                        PaddingBottom = 4,
                        VerticalAlignment = "center",
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
                BasicTextCapabilityPack.RenameStyle,
                BasicTextCapabilityPack.DeleteStyle,
                BasicTextCapabilityPack.CreateStylesFromParagraphs,
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
            new RenameStyleOperationHandler(),
            new DeleteStyleOperationHandler(),
            new CreateStylesFromParagraphsOperationHandler(),
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

    private static string GetArtifactRoot([CallerFilePath] string sourceFilePath = "")
    {
        string? testsDirectory = Path.GetDirectoryName(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(testsDirectory))
        {
            return Path.Combine(testsDirectory, "TestArtifacts");
        }

        throw new InvalidOperationException("Could not locate the test artifact directory.");
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
