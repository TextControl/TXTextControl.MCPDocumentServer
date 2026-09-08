using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace TxTextControl.McpServer.Tests;

public sealed class CapabilityPackTests
{
    [Fact]
    public void BasicTextPackDeclaresSupportedOperations()
    {
        var pack = new BasicTextCapabilityPack();

        Assert.Equal(BasicTextCapabilityPack.PackName, pack.Name);
        Assert.Contains(BasicTextCapabilityPack.DefineStyle, pack.OperationTypes);
        Assert.Contains(BasicTextCapabilityPack.AppendParagraph, pack.OperationTypes);
        Assert.Contains(BasicTextCapabilityPack.ApplyStyleToParagraph, pack.OperationTypes);
        Assert.Contains(BasicTextCapabilityPack.FormatParagraphs, pack.OperationTypes);
        Assert.Contains(BasicTextCapabilityPack.FormatTextOccurrences, pack.OperationTypes);
        Assert.Contains(BasicTextCapabilityPack.ReplaceText, pack.OperationTypes);
    }

    [Fact]
    public void MediaPackDeclaresSupportedOperations()
    {
        var pack = new MediaCapabilityPack();

        Assert.Equal(MediaCapabilityPack.PackName, pack.Name);
        Assert.Contains(MediaCapabilityPack.AppendImage, pack.OperationTypes);
    }

    [Fact]
    public void TablePackDeclaresSupportedOperations()
    {
        var pack = new TableCapabilityPack();

        Assert.Equal(TableCapabilityPack.PackName, pack.Name);
        Assert.Contains(TableCapabilityPack.AppendTable, pack.OperationTypes);
        Assert.Contains(TableCapabilityPack.SetTableCellText, pack.OperationTypes);
        Assert.Contains(TableCapabilityPack.FormatTableCell, pack.OperationTypes);
        Assert.Contains(TableCapabilityPack.FormatTableHeaderRow, pack.OperationTypes);
        Assert.Contains(TableCapabilityPack.FormatTableColumn, pack.OperationTypes);
        Assert.Contains(TableCapabilityPack.ApplyTableStylePreset, pack.OperationTypes);
        Assert.Contains(TableCapabilityPack.AddTableRow, pack.OperationTypes);
    }

    [Fact]
    public void FieldsPackDeclaresSupportedOperations()
    {
        var pack = new FieldsCapabilityPack();

        Assert.Equal(FieldsCapabilityPack.PackName, pack.Name);
        Assert.Contains(FieldsCapabilityPack.AppendMergeField, pack.OperationTypes);
        Assert.Contains(FieldsCapabilityPack.UpdateMergeField, pack.OperationTypes);
        Assert.Contains(FieldsCapabilityPack.ClearApplicationFields, pack.OperationTypes);
        Assert.Contains(FieldsCapabilityPack.AppendMergeBlock, pack.OperationTypes);
        Assert.Contains(FieldsCapabilityPack.AppendFormField, pack.OperationTypes);
        Assert.Contains(FieldsCapabilityPack.UpdateFormField, pack.OperationTypes);
        Assert.Contains(FieldsCapabilityPack.ClearFormFields, pack.OperationTypes);
    }

    [Fact]
    public void HeaderFooterPackDeclaresSupportedOperations()
    {
        var pack = new HeaderFooterCapabilityPack();

        Assert.Equal(HeaderFooterCapabilityPack.PackName, pack.Name);
        Assert.Contains(HeaderFooterCapabilityPack.SetHeaderFooter, pack.OperationTypes);
    }

    [Fact]
    public void SectionPackDeclaresSupportedOperations()
    {
        var pack = new SectionCapabilityPack();

        Assert.Equal(SectionCapabilityPack.PackName, pack.Name);
        Assert.Contains(SectionCapabilityPack.InsertSectionBreak, pack.OperationTypes);
        Assert.Contains(SectionCapabilityPack.SetSectionLayout, pack.OperationTypes);
    }

    [Fact]
    public void RegistryReportsCapabilityPacksAsFirstClassModules()
    {
        var registry = CreateRegistry(new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [BasicTextCapabilityPack.PackName, MediaCapabilityPack.PackName, TableCapabilityPack.PackName, FieldsCapabilityPack.PackName, SectionCapabilityPack.PackName, HeaderFooterCapabilityPack.PackName],
            EnabledOperations =
            [
                BasicTextCapabilityPack.DefineStyle,
                BasicTextCapabilityPack.AppendParagraph,
                BasicTextCapabilityPack.ApplyStyleToParagraph,
                BasicTextCapabilityPack.FormatParagraphs,
                BasicTextCapabilityPack.FormatTextOccurrences,
                BasicTextCapabilityPack.ReplaceText,
                MediaCapabilityPack.AppendImage,
                TableCapabilityPack.AppendTable,
                TableCapabilityPack.SetTableCellText,
                TableCapabilityPack.FormatTableCell,
                TableCapabilityPack.FormatTableHeaderRow,
                TableCapabilityPack.FormatTableColumn,
                TableCapabilityPack.ApplyTableStylePreset,
                TableCapabilityPack.AddTableRow,
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
        });

        var packs = registry.GetCapabilityPacks();
        var pack = Assert.Single(packs, pack => pack.Name == BasicTextCapabilityPack.PackName);
        var media = Assert.Single(packs, pack => pack.Name == MediaCapabilityPack.PackName);
        var tables = Assert.Single(packs, pack => pack.Name == TableCapabilityPack.PackName);
        var fields = Assert.Single(packs, pack => pack.Name == FieldsCapabilityPack.PackName);
        var sections = Assert.Single(packs, pack => pack.Name == SectionCapabilityPack.PackName);
        var headerFooter = Assert.Single(packs, pack => pack.Name == HeaderFooterCapabilityPack.PackName);

        Assert.Equal(BasicTextCapabilityPack.PackName, pack.Name);
        Assert.True(pack.Enabled);
        Assert.Equal(9, pack.OperationTypes.Count);
        Assert.True(media.Enabled);
        Assert.Single(media.OperationTypes);
        Assert.True(tables.Enabled);
        Assert.Equal(7, tables.OperationTypes.Count);
        Assert.True(fields.Enabled);
        Assert.Equal(7, fields.OperationTypes.Count);
        Assert.True(sections.Enabled);
        Assert.Equal(2, sections.OperationTypes.Count);
        Assert.True(headerFooter.Enabled);
        Assert.Single(headerFooter.OperationTypes);
    }

    [Fact]
    public void RegistryReportsDetailedOperationDescriptors()
    {
        var registry = CreateRegistry(new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [BasicTextCapabilityPack.PackName, MediaCapabilityPack.PackName, TableCapabilityPack.PackName, FieldsCapabilityPack.PackName, SectionCapabilityPack.PackName, HeaderFooterCapabilityPack.PackName],
            EnabledOperations =
            [
                BasicTextCapabilityPack.DefineStyle,
                BasicTextCapabilityPack.AppendParagraph,
                BasicTextCapabilityPack.ApplyStyleToParagraph,
                BasicTextCapabilityPack.FormatParagraphs,
                BasicTextCapabilityPack.FormatTextOccurrences,
                BasicTextCapabilityPack.ReplaceText,
                MediaCapabilityPack.AppendImage,
                TableCapabilityPack.AppendTable,
                TableCapabilityPack.SetTableCellText,
                TableCapabilityPack.FormatTableCell,
                TableCapabilityPack.FormatTableHeaderRow,
                TableCapabilityPack.FormatTableColumn,
                TableCapabilityPack.ApplyTableStylePreset,
                TableCapabilityPack.AddTableRow,
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
        });

        var descriptors = registry.GetOperationDescriptors();
        var table = Assert.Single(descriptors, descriptor => descriptor.Type == TableCapabilityPack.AppendTable);

        Assert.True(table.Enabled);
        Assert.Contains("rows", table.RequiredProperties);
        Assert.True(table.Example.ContainsKey("rows"));
        Assert.Contains(table.ModelEffects, effect => effect.Contains("table", StringComparison.OrdinalIgnoreCase));
        var field = Assert.Single(descriptors, descriptor => descriptor.Type == FieldsCapabilityPack.AppendMergeField);
        Assert.True(field.Enabled);
        Assert.Contains("fieldName", field.RequiredProperties);
        var paragraphFormat = Assert.Single(descriptors, descriptor => descriptor.Type == BasicTextCapabilityPack.FormatParagraphs);
        Assert.True(paragraphFormat.Enabled);
        Assert.Contains("paragraph", paragraphFormat.RequiredProperties);
        Assert.Contains("matchText", paragraphFormat.OptionalProperties);
        Assert.Contains("nearTextPosition", paragraphFormat.OptionalProperties);
        Assert.Contains("allParagraphs", paragraphFormat.OptionalProperties);
        var tableColumn = Assert.Single(descriptors, descriptor => descriptor.Type == TableCapabilityPack.FormatTableColumn);
        Assert.True(tableColumn.Enabled);
        Assert.Contains("width", tableColumn.RequiredProperties);
        var tableStylePreset = Assert.Single(descriptors, descriptor => descriptor.Type == TableCapabilityPack.ApplyTableStylePreset);
        Assert.True(tableStylePreset.Enabled);
        Assert.Contains("styleName", tableStylePreset.OptionalProperties);
        var block = Assert.Single(descriptors, descriptor => descriptor.Type == FieldsCapabilityPack.AppendMergeBlock);
        Assert.True(block.Enabled);
        Assert.Contains("blockName", block.RequiredProperties);
        var formField = Assert.Single(descriptors, descriptor => descriptor.Type == FieldsCapabilityPack.AppendFormField);
        Assert.True(formField.Enabled);
        Assert.Contains("fieldName", formField.RequiredProperties);
        Assert.Contains("formFieldType", formField.RequiredProperties);
        var sectionBreak = Assert.Single(descriptors, descriptor => descriptor.Type == SectionCapabilityPack.InsertSectionBreak);
        Assert.True(sectionBreak.Enabled);
        Assert.Contains("breakKind", sectionBreak.OptionalProperties);
        var sectionLayout = Assert.Single(descriptors, descriptor => descriptor.Type == SectionCapabilityPack.SetSectionLayout);
        Assert.True(sectionLayout.Enabled);
        Assert.Contains("pageSize", sectionLayout.OptionalProperties);
        var headerFooter = Assert.Single(descriptors, descriptor => descriptor.Type == HeaderFooterCapabilityPack.SetHeaderFooter);
        Assert.True(headerFooter.Enabled);
        Assert.Contains("headerFooterType", headerFooter.RequiredProperties);
    }

    [Fact]
    public void RegistryBlocksHandlerWhenCapabilityPackIsDisabled()
    {
        var registry = CreateRegistry(new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [],
            EnabledOperations = [MediaCapabilityPack.AppendImage]
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequiredHandler(MediaCapabilityPack.AppendImage));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistryBlocksHandlerWhenOperationIsDisabled()
    {
        var registry = CreateRegistry(new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [BasicTextCapabilityPack.PackName, MediaCapabilityPack.PackName, TableCapabilityPack.PackName, FieldsCapabilityPack.PackName, SectionCapabilityPack.PackName, HeaderFooterCapabilityPack.PackName],
            EnabledOperations = []
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.GetRequiredHandler(BasicTextCapabilityPack.AppendParagraph));

        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistryEnablesAllRegisteredFeaturesWhenAutomationConfigIsMissing()
    {
        var registry = CreateRegistry(new DocumentAutomationOptions());

        var packs = registry.GetCapabilityPacks();
        var operations = registry.GetCapabilities();

        Assert.All(packs, pack => Assert.True(pack.Enabled));
        Assert.All(operations, operation => Assert.True(operation.Enabled));
        Assert.NotNull(registry.GetRequiredHandler(BasicTextCapabilityPack.AppendParagraph));
    }

    [Fact]
    public void RegistryTreatsEmptyAutomationConfigAsDisableAll()
    {
        var registry = CreateRegistry(new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [],
            EnabledOperations = []
        });

        var packs = registry.GetCapabilityPacks();
        var operations = registry.GetCapabilities();

        Assert.All(packs, pack => Assert.False(pack.Enabled));
        Assert.All(operations, operation => Assert.False(operation.Enabled));
    }

    [Fact]
    public void AuthoringGuideProvidesSelfContainedInstructionsForExternalAiClients()
    {
        var options = new DocumentAutomationOptions
        {
            EnabledCapabilityPacks = [BasicTextCapabilityPack.PackName, TableCapabilityPack.PackName, FieldsCapabilityPack.PackName],
            EnabledOperations =
            [
                BasicTextCapabilityPack.AppendParagraph,
                TableCapabilityPack.AppendTable,
                TableCapabilityPack.ApplyTableStylePreset,
                FieldsCapabilityPack.AppendMergeField,
                FieldsCapabilityPack.AppendMergeBlock
            ],
            DefaultParagraphStyleName = "Body",
            StylePresets =
            [
                new TextStyleDefinition
                {
                    Name = "Body",
                    FontName = "Arial",
                    FontSize = 12,
                    FontSizeUnit = "pt"
                }
            ],
            TableStylePresets =
            [
                new TableStylePresetDefinition
                {
                    Name = "Professional Blue",
                    HeaderStyle = new TextStyleDefinition
                    {
                        Bold = true,
                        ColorHex = "#ffffff"
                    },
                    HeaderCellStyle = new CellStyleDefinition
                    {
                        BackgroundColorHex = "#1f4e79"
                    }
                }
            ]
        };
        var registry = CreateRegistry(options);
        var service = new AuthoringGuideService(registry, Microsoft.Extensions.Options.Options.Create(options));
        var guide = service.Build();

        Assert.Equal("create_document", guide.ToolMap.CreateModelFirst);
        Assert.Equal("create_document_from_markdown", guide.ToolMap.CreateMarkdown);
        Assert.Equal("inspect_document", guide.ToolMap.InspectPrimary);
        Assert.Equal("edit_document", guide.ToolMap.EditPrimary);
        Assert.Equal("convert_document", guide.ToolMap.Convert);
        Assert.Equal("apply_operations", guide.ToolMap.EditOperationFirst);
        Assert.Equal("create_document_export", guide.ToolMap.Export);
        Assert.Equal("list_document_recipes", guide.ToolMap.ListRecipes);
        Assert.Equal("create_document_from_recipe", guide.ToolMap.CreateFromRecipe);
        Assert.Equal("apply_document_preset_styles", guide.ToolMap.StyleImported);
        Assert.Contains("docx", guide.ValueSets["outputFormats"]);
        Assert.Contains("rtf", guide.ValueSets["outputFormats"]);
        Assert.Contains("txt", guide.ValueSets["outputFormats"]);
        Assert.Contains("Letter", guide.ValueSets["pageSizes"]);
        Assert.Contains("paragraph", guide.DocumentModelContract.SupportedBlockTypes);
        Assert.Contains("field", guide.DocumentModelContract.SupportedBlockTypes);
        string minimalShapeJson = System.Text.Json.JsonSerializer.Serialize(guide.DocumentModelContract.MinimalDocumentShape);
        Assert.DoesNotContain("pageLayout", minimalShapeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("styles", minimalShapeJson, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"body\"", minimalShapeJson, StringComparison.Ordinal);
        Assert.Contains(guide.OperationSchemas, schema => schema.Type == TableCapabilityPack.AppendTable);
        Assert.Contains(guide.StylePresets, style => style.Name == "Body" && style.FontName == "Arial");
        Assert.Contains(guide.TableStylePresets, preset => preset.Name == "Professional Blue");
        Assert.True(guide.StylePolicy.OmitStylePropertiesWhenPromptHasNoStyleInstructions);
        Assert.Contains("styleName", guide.StylePolicy.PropertiesToOmitUnlessExplicitlyRequested);
        Assert.Contains(guide.StylePolicy.AutomaticDefaults, value => value.Contains("document.title", StringComparison.OrdinalIgnoreCase));
        Assert.True(guide.SessionPolicy.ReuseSessionForFollowUpEdits);
        Assert.Contains("inspect_document", guide.SessionPolicy.RecommendedInspectionToolsBeforeEditing);
        Assert.Contains(guide.RecommendedWorkflow, item => item.StartsWith("QUESTION:", StringComparison.Ordinal));
        Assert.Contains(guide.RecommendedWorkflow, item => item.StartsWith("EDIT:", StringComparison.Ordinal));
        Assert.Contains(guide.RecommendedWorkflow, item => item.StartsWith("CONVERT:", StringComparison.Ordinal));
        Assert.Contains(guide.RecommendedWorkflow, item => item.StartsWith("CREATE:", StringComparison.Ordinal));
        Assert.Contains("change", guide.SessionPolicy.FollowUpEditTriggers);
        Assert.Contains("get_document_tables", guide.SessionPolicy.RecommendedInspectionToolsBeforeEditing);
        Assert.Contains(guide.Recipes, recipe => recipe.Name == "invoice-template-mail-merge");
        Assert.Equal(guide.Recipes.Count, service.GetRecipes().Count);
        Assert.Equal(
            "invoice-template-mail-merge",
            service.GetRecipe("INVOICE-TEMPLATE-MAIL-MERGE").Name);
        var invoiceRecipe = service.GetRecipe("invoice-template-mail-merge");
        string invoiceRecipeJson = System.Text.Json.JsonSerializer.Serialize(invoiceRecipe.ExampleRequest);
        Assert.Equal(
            ["create_document_from_recipe", "merge_template", "create_document_export"],
            invoiceRecipe.ToolSequence);
        Assert.Contains("Subtotal", invoiceRecipeJson, StringComparison.Ordinal);
        Assert.Contains("PaymentTerms", invoiceRecipeJson, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => service.GetRecipe("unknown-recipe"));
        Assert.Contains(guide.BestPractices, practice => practice.Contains("Set page size", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(guide.BestPractices, practice => practice.Contains("Style omission policy", StringComparison.OrdinalIgnoreCase));
    }

    private static DocumentOperationRegistry CreateRegistry(DocumentAutomationOptions options)
    {
        IDocumentOperationHandler[] handlers =
        [
            new DefineStyleOperationHandler(),
            new AppendParagraphOperationHandler(),
            new ApplyStyleToParagraphOperationHandler(),
            new FormatParagraphsOperationHandler(),
            new FormatTextOccurrencesOperationHandler(),
            new ReplaceTextOperationHandler(),
            new AppendImageOperationHandler(),
            new AppendTableOperationHandler(),
            new SetTableCellTextOperationHandler(),
            new FormatTableCellOperationHandler(),
            new FormatTableHeaderRowOperationHandler(),
            new FormatTableColumnOperationHandler(),
            new ApplyTableStylePresetOperationHandler(options),
            new AddTableRowOperationHandler(),
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
            new MediaCapabilityPack(),
            new TableCapabilityPack(),
            new FieldsCapabilityPack(),
            new SectionCapabilityPack(),
            new HeaderFooterCapabilityPack()
        ];
        var settings = new AutomationSettingsService(
            options,
            Path.Combine(Path.GetTempPath(), "tx-mcp-test-appsettings.json"),
            packs,
            handlers);

        return new DocumentOperationRegistry(handlers, packs, settings);
    }
}
