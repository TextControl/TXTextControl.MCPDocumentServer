using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

internal static class FormFieldOperationUtilities
{
    public const int DefaultEmptyWidth = 1000;

    public static string RequireFieldName(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("fieldName is required.");
        }

        return fieldName.Trim();
    }

    public static string ResolveType(string? formFieldType)
    {
        if (string.IsNullOrWhiteSpace(formFieldType))
        {
            return "text";
        }

        return formFieldType.Trim().ToLowerInvariant() switch
        {
            "text" or "textformfield" => "text",
            "selection" or "select" or "dropdown" or "drop-down" or "combobox" or "combo" or "selectionformfield" => "selection",
            "check" or "checkbox" or "checkformfield" => "checkbox",
            "date" or "dateformfield" => "date",
            _ => throw new ArgumentException("formFieldType must be one of: text, selection, checkbox, date.")
        };
    }

    public static int ResolveEmptyWidth(int? emptyWidth)
    {
        if (!emptyWidth.HasValue)
        {
            return DefaultEmptyWidth;
        }

        if (emptyWidth.Value <= 0)
        {
            throw new ArgumentException("emptyWidth must be greater than 0.");
        }

        return emptyWidth.Value;
    }

    public static FormField CreateFormField(DocumentOperation operation, string fieldName, string type)
    {
        var emptyWidth = ResolveEmptyWidth(operation.EmptyWidth);
        FormField field = type switch
        {
            "text" => new TextFormField(emptyWidth)
            {
                Text = operation.Text ?? operation.FieldText ?? string.Empty
            },
            "selection" => CreateSelectionFormField(operation, emptyWidth),
            "checkbox" => new CheckFormField(operation.Checked ?? false),
            "date" => CreateDateFormField(operation, emptyWidth),
            _ => throw new ArgumentException("Unsupported form field type.")
        };

        field.Name = fieldName;
        if (operation.Enabled.HasValue)
        {
            field.Enabled = operation.Enabled.Value;
        }

        return field;
    }

    public static void ApplyValue(FormField field, DocumentOperation operation)
    {
        if (operation.Enabled.HasValue)
        {
            field.Enabled = operation.Enabled.Value;
        }

        switch (field)
        {
            case TextFormField text:
                text.Text = operation.Text ?? operation.FieldText ?? text.Text ?? string.Empty;
                break;
            case SelectionFormField selection:
                if (operation.Items.Count > 0)
                {
                    selection.Items = operation.Items.Where(item => item is not null).ToArray();
                }

                if (operation.Editable.HasValue)
                {
                    selection.Editable = operation.Editable.Value;
                }

                selection.Text = operation.Text ?? operation.FieldText ?? selection.Text ?? string.Empty;
                break;
            case CheckFormField check:
                if (operation.Checked.HasValue)
                {
                    check.Checked = operation.Checked.Value;
                }
                break;
            case DateFormField date:
                if (!string.IsNullOrWhiteSpace(operation.Date) || !string.IsNullOrWhiteSpace(operation.Text))
                {
                    date.Date = ParseDate(operation.Date ?? operation.Text);
                }
                break;
            default:
                field.Text = operation.Text ?? operation.FieldText ?? field.Text ?? string.Empty;
                break;
        }
    }

    public static DocumentModel.Field ToModelField(string fieldId, string fieldName, string type, DocumentOperation operation)
    {
        var value = type switch
        {
            "checkbox" => (operation.Checked ?? false).ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            "date" => NormalizeDateText(operation.Date ?? operation.Text),
            _ => operation.Text ?? operation.FieldText ?? string.Empty
        };

        var properties = new Dictionary<string, string>
        {
            ["formFieldType"] = type,
            ["txType"] = ToTxTypeName(type),
            ["enabled"] = (operation.Enabled ?? true).ToString(CultureInfo.InvariantCulture).ToLowerInvariant()
        };

        if (operation.Items.Count > 0)
        {
            properties["items"] = string.Join("|", operation.Items);
        }

        if (operation.Editable.HasValue)
        {
            properties["editable"] = operation.Editable.Value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        if (operation.EmptyWidth.HasValue)
        {
            properties["emptyWidth"] = operation.EmptyWidth.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new DocumentModel.Field
        {
            Id = fieldId,
            Type = "form",
            Name = fieldName,
            Value = value,
            Properties = properties
        };
    }

    public static void SetInsertionPoint(DocumentOperationContext context, ServerTextControl tx, DocumentOperation operation)
    {
        var hasTableTarget = !string.IsNullOrWhiteSpace(operation.TableId)
                             || operation.RowIndex.HasValue
                             || operation.ColumnIndex.HasValue;
        if (!hasTableTarget)
        {
            tx.Selection = new Selection((tx.Text ?? string.Empty).Length, 0);
            return;
        }

        var table = TableOperationUtilities.GetTxTable(tx, TableOperationUtilities.RequireTableId(operation.TableId));
        var cell = TableOperationUtilities.GetTxCell(
            table,
            TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex)),
            TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex)));

        var placement = ResolvePlacement(operation.Placement);
        if (placement == FormFieldPlacement.Replace)
        {
            cell.Text = string.Empty;
        }

        var offset = placement switch
        {
            FormFieldPlacement.Start or FormFieldPlacement.Replace => 0,
            FormFieldPlacement.End => cell.Text?.Length ?? 0,
            _ => 0
        };
        tx.Selection = new Selection(Math.Max(0, cell.Start - 1 + offset), 0);
    }

    public static void AddModelField(DocumentOperationContext context, DocumentOperation operation, DocumentModel.DocumentBlock fieldBlock)
    {
        var hasTableTarget = !string.IsNullOrWhiteSpace(operation.TableId)
                             || operation.RowIndex.HasValue
                             || operation.ColumnIndex.HasValue;
        if (!hasTableTarget)
        {
            context.GetMainSection().Blocks.Add(fieldBlock);
            return;
        }

        var table = TableOperationUtilities.GetModelTable(context.Document, TableOperationUtilities.RequireTableId(operation.TableId).ToString(CultureInfo.InvariantCulture));
        var cell = TableOperationUtilities.GetModelCell(
            table,
            TableOperationUtilities.RequireIndex(operation.RowIndex, nameof(operation.RowIndex)),
            TableOperationUtilities.RequireIndex(operation.ColumnIndex, nameof(operation.ColumnIndex)));

        var placement = ResolvePlacement(operation.Placement);
        if (placement == FormFieldPlacement.Replace)
        {
            cell.Blocks.Clear();
        }

        if (placement == FormFieldPlacement.Start)
        {
            cell.Blocks.Insert(0, fieldBlock);
        }
        else
        {
            cell.Blocks.Add(fieldBlock);
        }
    }

    public static string Location(DocumentOperation operation)
    {
        var hasTableTarget = !string.IsNullOrWhiteSpace(operation.TableId)
                             || operation.RowIndex.HasValue
                             || operation.ColumnIndex.HasValue;
        return hasTableTarget
            ? $"tables['{operation.TableId}'].rows[{operation.RowIndex}].cells[{operation.ColumnIndex}].blocks"
            : "sections[0].blocks";
    }

    public static string ToTxTypeName(string type)
        => type switch
        {
            "text" => nameof(TextFormField),
            "selection" => nameof(SelectionFormField),
            "checkbox" => nameof(CheckFormField),
            "date" => nameof(DateFormField),
            _ => type
        };

    private static SelectionFormField CreateSelectionFormField(DocumentOperation operation, int emptyWidth)
    {
        var field = new SelectionFormField(emptyWidth)
        {
            Items = operation.Items.Where(item => item is not null).ToArray(),
            Text = operation.Text ?? operation.FieldText ?? operation.Items.FirstOrDefault() ?? string.Empty
        };

        if (operation.Editable.HasValue)
        {
            field.Editable = operation.Editable.Value;
        }

        return field;
    }

    private static DateFormField CreateDateFormField(DocumentOperation operation, int emptyWidth)
    {
        var field = new DateFormField(emptyWidth);
        if (!string.IsNullOrWhiteSpace(operation.Date) || !string.IsNullOrWhiteSpace(operation.Text))
        {
            field.Date = ParseDate(operation.Date ?? operation.Text);
        }

        return field;
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.Today;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            return parsed;
        }

        throw new ArgumentException("date must be a valid date string.");
    }

    private static string NormalizeDateText(string? value)
        => ParseDate(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static FormFieldPlacement ResolvePlacement(string? placement)
    {
        if (string.IsNullOrWhiteSpace(placement))
        {
            return FormFieldPlacement.End;
        }

        return placement.Trim().ToLowerInvariant() switch
        {
            "end" => FormFieldPlacement.End,
            "start" => FormFieldPlacement.Start,
            "replace" => FormFieldPlacement.Replace,
            _ => throw new ArgumentException("placement must be 'end', 'start', or 'replace'.")
        };
    }

    private enum FormFieldPlacement
    {
        End,
        Start,
        Replace
    }
}
