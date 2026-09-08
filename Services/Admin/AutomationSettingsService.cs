using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services.Admin;

public sealed class AutomationSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly DocumentAutomationOptions _options;
    private readonly List<ICapabilityPack> _knownPacks;
    private readonly List<IDocumentOperationHandler> _knownOperations;
    private readonly string _settingsPath;
    private readonly object _gate = new();
    private volatile HashSet<string> _enabledPacks;
    private volatile HashSet<string> _enabledOperations;

    public AutomationSettingsService(
        IOptions<DocumentAutomationOptions> options,
        IHostEnvironment environment,
        IEnumerable<ICapabilityPack> knownPacks,
        IEnumerable<IDocumentOperationHandler> knownOperations)
        : this(
            options.Value,
            Path.Combine(environment.ContentRootPath, "appsettings.json"),
            knownPacks,
            knownOperations)
    {
    }

    public AutomationSettingsService(
        DocumentAutomationOptions options,
        string settingsPath,
        IEnumerable<ICapabilityPack> knownPacks,
        IEnumerable<IDocumentOperationHandler> knownOperations)
    {
        _options = options;
        _knownPacks = knownPacks.ToList();
        _knownOperations = knownOperations.ToList();
        _settingsPath = settingsPath;
        _enabledPacks = ResolveEnabledValues(
                _options.EnabledCapabilityPacks,
                _knownPacks.Select(pack => pack.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _enabledOperations = ResolveEnabledValues(
                _options.EnabledOperations,
                _knownOperations.Select(operation => operation.Type))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetEnabledCapabilityPacks()
        => _knownPacks.Select(pack => pack.Name).Where(_enabledPacks.Contains).ToArray();

    public IReadOnlyList<string> GetEnabledOperations()
        => _knownOperations.Select(operation => operation.Type).Where(_enabledOperations.Contains).ToArray();

    public bool IsCapabilityPackEnabled(string capabilityPack)
        => _enabledPacks.Contains(capabilityPack);

    public bool IsOperationEnabled(string operation)
        => _enabledOperations.Contains(operation);

    public IReadOnlyList<TextStyleDefinition> GetStylePresets()
        => _options.StylePresets;

    public IReadOnlyList<TableStylePresetDefinition> GetTableStylePresets()
        => _options.TableStylePresets;

    public string GetDefaultParagraphStyleName()
        => ResolveBodyStyleName();

    public StyleRoleDefinition GetStyleRoles()
        => _options.StyleRoles;

    internal DocumentAutomationOptions GetWorkerSnapshot()
    {
        lock (_gate)
        {
            return JsonSerializer.Deserialize<DocumentAutomationOptions>(
                JsonSerializer.Serialize(_options))!;
        }
    }

    internal void ApplyWorkerSnapshot(DocumentAutomationOptions snapshot)
    {
        // Keep the options instance shared by the engine and operation handlers.
        // Called between commands, never while a document operation is executing.
        _options.DefaultParagraphStyleName = snapshot.DefaultParagraphStyleName;
        _options.StyleRoles = snapshot.StyleRoles;
        _options.StylePresets = snapshot.StylePresets;
        _options.TableStylePresets = snapshot.TableStylePresets;
        _options.DefaultPageLayout = snapshot.DefaultPageLayout;
        _options.EnabledCapabilityPacks = snapshot.EnabledCapabilityPacks;
        _options.EnabledOperations = snapshot.EnabledOperations;
        _enabledPacks = ResolveEnabledValues(snapshot.EnabledCapabilityPacks,
            _knownPacks.Select(pack => pack.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _enabledOperations = ResolveEnabledValues(snapshot.EnabledOperations,
            _knownOperations.Select(operation => operation.Type)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(
        IEnumerable<string> enabledCapabilityPacks,
        IEnumerable<string> enabledOperations)
    {
        var packSet = _knownPacks
            .Select(pack => pack.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var operationSet = _knownOperations
            .Select(operation => operation.Type)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalizedPacks = enabledCapabilityPacks
            .Where(pack => !string.IsNullOrWhiteSpace(pack))
            .Select(pack => pack.Trim())
            .Where(packSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pack => _knownPacks.FindIndex(known => string.Equals(known.Name, pack, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var normalizedOperations = enabledOperations
            .Where(operation => !string.IsNullOrWhiteSpace(operation))
            .Select(operation => operation.Trim())
            .Where(operationSet.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(operation => _knownOperations.FindIndex(known => string.Equals(known.Type, operation, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        lock (_gate)
        {
            _options.EnabledCapabilityPacks = normalizedPacks;
            _options.EnabledOperations = normalizedOperations;
            _enabledPacks = normalizedPacks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _enabledOperations = normalizedOperations.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var root = LoadSettings();
            var automation = root[DocumentAutomationOptions.SectionName] as JsonObject;
            if (automation is null)
            {
                automation = [];
                root[DocumentAutomationOptions.SectionName] = automation;
            }

            automation["EnabledCapabilityPacks"] = ToJsonArray(normalizedPacks);
            automation["EnabledOperations"] = ToJsonArray(normalizedOperations);

            File.WriteAllText(_settingsPath, root.ToJsonString(SerializerOptions) + Environment.NewLine);
        }
    }

    public void SaveStylePresets(
        string? defaultParagraphStyleName,
        StyleRoleDefinition? styleRoles,
        IEnumerable<TextStyleDefinition> stylePresets,
        IEnumerable<TableStylePresetDefinition> tableStylePresets)
    {
        var normalizedStyles = stylePresets
            .Where(style => !string.IsNullOrWhiteSpace(style.Name))
            .Select(NormalizeStyle)
            .GroupBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        var normalizedRoles = NormalizeStyleRoles(styleRoles, defaultParagraphStyleName);
        var normalizedDefault = normalizedRoles.Body;

        if (!normalizedStyles.Any(style => string.Equals(style.Name, normalizedDefault, StringComparison.OrdinalIgnoreCase)))
        {
            normalizedDefault = normalizedStyles.FirstOrDefault()?.Name ?? "Body";
            normalizedRoles.Body = normalizedDefault;
        }

        var normalizedTableStyles = tableStylePresets
            .Where(style => !string.IsNullOrWhiteSpace(style.Name))
            .Select(NormalizeTableStyle)
            .GroupBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        lock (_gate)
        {
            _options.DefaultParagraphStyleName = normalizedDefault;
            _options.StyleRoles = normalizedRoles;
            _options.StylePresets = normalizedStyles;
            _options.TableStylePresets = normalizedTableStyles;

            var root = LoadSettings();
            var automation = root[DocumentAutomationOptions.SectionName] as JsonObject;
            if (automation is null)
            {
                automation = [];
                root[DocumentAutomationOptions.SectionName] = automation;
            }

            automation["DefaultParagraphStyleName"] = normalizedDefault;
            automation["StyleRoles"] = JsonSerializer.SerializeToNode(normalizedRoles, SerializerOptions);
            automation["StylePresets"] = JsonSerializer.SerializeToNode(normalizedStyles, SerializerOptions);
            automation["TableStylePresets"] = JsonSerializer.SerializeToNode(normalizedTableStyles, SerializerOptions);

            File.WriteAllText(_settingsPath, root.ToJsonString(SerializerOptions) + Environment.NewLine);
        }
    }

    private JsonObject LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return [];
        }

        var json = File.ReadAllText(_settingsPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidOperationException("appsettings.json must contain a JSON object.");
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static IReadOnlyList<string> ResolveEnabledValues(
        IReadOnlyList<string>? configuredValues,
        IEnumerable<string> defaultValues)
    {
        if (configuredValues is null)
        {
            return defaultValues.ToList();
        }

        return configuredValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ResolveBodyStyleName()
        => !string.IsNullOrWhiteSpace(_options.StyleRoles?.Body)
            ? _options.StyleRoles.Body
            : string.IsNullOrWhiteSpace(_options.DefaultParagraphStyleName)
                ? "Body"
                : _options.DefaultParagraphStyleName;

    private static StyleRoleDefinition NormalizeStyleRoles(
        StyleRoleDefinition? roles,
        string? fallbackBodyStyleName)
        => new()
        {
            Title = NormalizeRoleStyleName(roles?.Title, "Title"),
            Heading1 = NormalizeRoleStyleName(roles?.Heading1, "Heading"),
            Heading2 = NormalizeRoleStyleName(roles?.Heading2, "Heading2"),
            Body = NormalizeRoleStyleName(roles?.Body, fallbackBodyStyleName, "Body")
        };

    private static string NormalizeRoleStyleName(params string?[] candidates)
        => candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim() ?? "Body";

    private static TextStyleDefinition NormalizeStyle(TextStyleDefinition style)
    {
        style.Name = style.Name.Trim();
        style.FontName = string.IsNullOrWhiteSpace(style.FontName) ? null : style.FontName.Trim();
        style.FontSizeUnit = string.IsNullOrWhiteSpace(style.FontSizeUnit) ? "pt" : style.FontSizeUnit.Trim();
        style.ColorHex = NormalizeHex(style.ColorHex);
        if (style.Paragraph is not null)
        {
            style.Paragraph.Unit = string.IsNullOrWhiteSpace(style.Paragraph.Unit) ? "pt" : style.Paragraph.Unit.Trim();
            style.Paragraph.Alignment = string.IsNullOrWhiteSpace(style.Paragraph.Alignment) ? null : style.Paragraph.Alignment.Trim();
        }

        return style;
    }

    private static TableStylePresetDefinition NormalizeTableStyle(TableStylePresetDefinition style)
    {
        style.Name = style.Name.Trim();
        style.HeaderRowIndex = Math.Max(0, style.HeaderRowIndex);
        if (style.HeaderStyle is not null)
        {
            style.HeaderStyle = NormalizeStyle(style.HeaderStyle);
        }

        if (style.BodyStyle is not null)
        {
            style.BodyStyle = NormalizeStyle(style.BodyStyle);
        }

        if (style.AlternatingRowStyle is not null)
        {
            style.AlternatingRowStyle = NormalizeStyle(style.AlternatingRowStyle);
        }

        NormalizeCellStyle(style.HeaderCellStyle);
        NormalizeCellStyle(style.BodyCellStyle);
        NormalizeCellStyle(style.AlternatingRowCellStyle);
        return style;
    }

    private static void NormalizeCellStyle(CellStyleDefinition? style)
    {
        if (style is null)
        {
            return;
        }

        style.BackgroundColorHex = NormalizeHex(style.BackgroundColorHex);
        style.PaddingUnit = string.IsNullOrWhiteSpace(style.PaddingUnit) ? "pt" : style.PaddingUnit.Trim();
        style.HorizontalAlignment = string.IsNullOrWhiteSpace(style.HorizontalAlignment)
            ? null
            : style.HorizontalAlignment.Trim();
        style.VerticalAlignment = string.IsNullOrWhiteSpace(style.VerticalAlignment)
            ? null
            : style.VerticalAlignment.Trim();
        if (style.Border is not null)
        {
            style.Border.ColorHex = NormalizeHex(style.Border.ColorHex);
            NormalizeBorderSide(style.Border.Left);
            NormalizeBorderSide(style.Border.Top);
            NormalizeBorderSide(style.Border.Right);
            NormalizeBorderSide(style.Border.Bottom);
        }
    }

    private static void NormalizeBorderSide(CellBorderSideDefinition? side)
    {
        if (side is not null)
        {
            side.ColorHex = NormalizeHex(side.ColorHex);
        }
    }

    private static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : "#" + trimmed;
    }
}
