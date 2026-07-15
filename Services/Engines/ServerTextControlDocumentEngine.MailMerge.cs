using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;
using TXTextControl.DocumentServer;

namespace TxTextControl.McpServer.Services;

public sealed partial class ServerTextControlDocumentEngine
{
    public IReadOnlyList<TemplateMergeFieldInfo> GetTemplateMergeFields(string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        using var tx = CreateServerTextControl();
        tx.Create();
        tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat, CreateTemplateLoadSettings());

        return tx.ApplicationFields
            .Cast<ApplicationField>()
            .Where(field => string.Equals(field.TypeName, "MERGEFIELD", StringComparison.OrdinalIgnoreCase))
            .Select(field => new TemplateMergeFieldInfo
            {
                Name = field.Parameters.Length > 0 ? field.Parameters[0] : string.Empty,
                Text = field.Text ?? string.Empty,
                TypeName = field.TypeName ?? string.Empty,
                Parameters = field.Parameters.ToList()
            })
            .ToList();
    }

    public IReadOnlyList<TemplateMergeBlockInfo> GetTemplateMergeBlocks(string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        using var tx = CreateServerTextControl();
        tx.Create();
        tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat, CreateTemplateLoadSettings());

        return tx.SubTextParts
            .Cast<SubTextPart>()
            .Where(part => part.Name.StartsWith("txmb_", StringComparison.OrdinalIgnoreCase))
            .Select(part => new TemplateMergeBlockInfo
            {
                Name = part.Name,
                BlockName = part.Name[5..],
                Id = part.ID,
                Start = part.Start,
                Length = part.Length,
                TextPreview = Preview(part.Text)
            })
            .ToList();
    }

    public IReadOnlyList<TemplateFormFieldInfo> GetTemplateFormFields(string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        using var tx = CreateServerTextControl();
        tx.Create();
        tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat, CreateTemplateLoadSettings());

        return tx.FormFields
            .Cast<FormField>()
            .Select(ToTemplateFormFieldInfo)
            .ToList();
    }

    public DocumentState MergeTemplate(
        string workingDocumentPath,
        DocumentState state,
        MergeTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var jsonData = ResolveJsonData(request);

        using var tx = CreateServerTextControl();
        tx.Create();
        tx.Load(workingDocumentPath, StreamType.InternalUnicodeFormat, CreateTemplateLoadSettings());

        using (var mailMerge = new MailMerge { TextComponent = tx })
        {
            var formFieldMergeType = ResolveFormFieldMergeType(request.FormFieldMergeType);
            if (formFieldMergeType.HasValue)
            {
                mailMerge.FormFieldMergeType = formFieldMergeType.Value;
            }

            mailMerge.RemoveEmptyFields = request.RemoveEmptyFields;
            mailMerge.RemoveEmptyBlocks = request.RemoveEmptyBlocks;
            mailMerge.RemoveEmptyImages = request.RemoveEmptyImages;
            mailMerge.RemoveEmptyLines = request.RemoveEmptyLines;
            mailMerge.RemoveTrailingWhitespace = request.RemoveTrailingWhitespace;
            mailMerge.MergeJsonData(jsonData, request.Append);
        }

        tx.Save(workingDocumentPath, StreamType.InternalUnicodeFormat);

        return new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = state.Document,
            Styles = new Dictionary<string, Models.DocumentModel.TextStyleDefinition>(
                state.Styles,
                StringComparer.OrdinalIgnoreCase),
            LastOperationResults =
            [
                new OperationResult
                {
                    Index = 0,
                    Type = "merge_template",
                    Detail = "Merged JSON data into the current template using TX Text Control MailMerge.",
                    TargetType = "document",
                    Location = "document",
                    Metadata = new Dictionary<string, object?>
                    {
                        ["append"] = request.Append
                        ,
                        ["formFieldMergeType"] = request.FormFieldMergeType
                    }
                }
            ]
        };
    }

    private static TemplateFormFieldInfo ToTemplateFormFieldInfo(FormField field)
    {
        var info = new TemplateFormFieldInfo
        {
            Name = field.Name ?? string.Empty,
            Type = ResolveFormFieldType(field),
            Text = field.Text ?? string.Empty,
            Enabled = field.Enabled
        };

        switch (field)
        {
            case CheckFormField check:
                info.Checked = check.Checked;
                break;
            case SelectionFormField selection:
                info.Items = selection.Items?.ToList() ?? [];
                info.Editable = selection.Editable;
                break;
            case DateFormField date:
                info.Date = Convert.ToString(date.Date, System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        return info;
    }

    private static string ResolveFormFieldType(FormField field)
        => field switch
        {
            TextFormField => "text",
            SelectionFormField => "selection",
            CheckFormField => "checkbox",
            DateFormField => "date",
            _ => field.GetType().Name
        };

    private static FormFieldMergeType? ResolveFormFieldMergeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "preselect" or "pre-select" or "prefill" => FormFieldMergeType.Preselect,
            "replace" or "flatten" => FormFieldMergeType.Replace,
            _ => throw new ArgumentException("formFieldMergeType must be one of: none, preselect, replace.")
        };
    }

    private static LoadSettings CreateTemplateLoadSettings()
        => new()
        {
            ApplicationFieldFormat = ApplicationFieldFormat.MSWord,
            LoadSubTextParts = true
        };

    private static string Preview(string? text, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "...";
    }

    private static string ResolveJsonData(MergeTemplateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.JsonData))
        {
            ValidateJson(request.JsonData!);
            return request.JsonData!;
        }

        if (request.Data is { } data && data.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            if (data.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                throw new ArgumentException("'data' must contain a JSON object or array.");
            }

            return data.GetRawText();
        }

        throw new ArgumentException("Either 'jsonData' or 'data' is required.");
    }

    private static void ValidateJson(string jsonData)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonData);
            if (document.RootElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                throw new ArgumentException("'jsonData' must contain a JSON object or array.");
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("'jsonData' must contain valid JSON.", ex);
        }
    }
}
