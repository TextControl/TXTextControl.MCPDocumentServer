using System;
using System.Collections.Generic;
using System.IO;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services.Operations;
using TXTextControl;

namespace TxTextControl.McpServer.Services;

public sealed partial class ServerTextControlDocumentEngine
{
    public DocumentState ApplyOperations(
        string workingDocumentPath,
        DocumentState state,
        ApplyOperationsRequest request)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        EnsureDirectory(workingDocumentPath);

        var styles = BuildStyleDictionary(state);
        var document = EnsureDocument(state);
        EnsureDocumentStyles(document, styles);
        var results = new List<OperationResult>();

        using (var tx = CreateServerTextControl())
        {
            if (File.Exists(workingDocumentPath))
            {
                LoadWorkingDocument(tx, workingDocumentPath);
            }
            else
            {
                ResetDocument(tx);
            }

            var context = new DocumentOperationContext(
                tx,
                document,
                styles,
                ResolveDefaultBodyStyleName(),
                ResolveTitleStyleName());

            for (var i = 0; i < request.Operations.Count; i++)
            {
                var operation = request.Operations[i];
                var handler = _operationRegistry.GetRequiredHandler(operation.Type);
                results.Add(handler.Apply(context, operation, i));
            }

            SaveWorkingDocument(tx, workingDocumentPath);
        }

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = document,
            Styles = new Dictionary<string, TextStyleDefinition>(styles, StringComparer.OrdinalIgnoreCase),
            LastOperationResults = results
        };
    }

    private static Models.DocumentModel.Document EnsureDocument(DocumentState? state)
    {
        if (state?.Document is { } document && !string.IsNullOrWhiteSpace(document.Id))
        {
            EnsureAtLeastOneSection(document);
            return document;
        }

        return CreateNeutralDocument();
    }

    private static void EnsureAtLeastOneSection(Models.DocumentModel.Document document)
    {
        if (document.Sections.Count == 0)
        {
            document.Sections.Add(new Models.DocumentModel.Section
            {
                Id = Guid.NewGuid().ToString("N")
            });
        }
    }

    private static void EnsureDocumentStyles(
        Models.DocumentModel.Document document,
        IDictionary<string, TextStyleDefinition> styles)
    {
        foreach (var pair in styles)
        {
            var existing = document.Styles.Find(style =>
                string.Equals(style.Name, pair.Key, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                document.Styles.Add(new Style
                {
                    Name = pair.Key,
                    Type = "paragraph",
                    Text = pair.Value
                });
            }
            else if (existing.Text is null)
            {
                existing.Text = pair.Value;
            }
        }
    }

    private Dictionary<string, TextStyleDefinition> BuildStyleDictionary(DocumentState? state)
    {
        var styles = new Dictionary<string, TextStyleDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var style in _automationOptions.StylePresets)
        {
            if (!string.IsNullOrWhiteSpace(style.Name))
            {
                styles[style.Name.Trim()] = style;
            }
        }

        if (state?.Styles is not null)
        {
            foreach (var pair in state.Styles)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    styles[pair.Key.Trim()] = pair.Value;
                }
            }
        }

        if (state?.Document?.Styles is not null)
        {
            foreach (var style in state.Document.Styles)
            {
                if (!string.IsNullOrWhiteSpace(style.Name) && style.Text is not null)
                {
                    styles[style.Name.Trim()] = style.Text;
                }
            }
        }

        return styles;
    }

    private string ResolveDefaultBodyStyleName()
        => !string.IsNullOrWhiteSpace(_automationOptions.StyleRoles?.Body)
            ? _automationOptions.StyleRoles.Body
            : _automationOptions.DefaultParagraphStyleName;

    private string? ResolveTitleStyleName()
        => string.IsNullOrWhiteSpace(_automationOptions.StyleRoles?.Title)
            ? null
            : _automationOptions.StyleRoles.Title;
}
