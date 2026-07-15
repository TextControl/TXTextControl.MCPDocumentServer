using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Pages.Admin;

public sealed class IndexModel : PageModel
{
    private readonly AutomationSettingsService _settings;
    private readonly DocumentOperationRegistry _registry;

    public IndexModel(
        AutomationSettingsService settings,
        DocumentOperationRegistry registry)
    {
        _settings = settings;
        _registry = registry;
    }

    [BindProperty]
    public List<string> EnabledCapabilityPacks { get; set; } = [];

    [BindProperty]
    public List<string> EnabledOperations { get; set; } = [];

    public IReadOnlyList<CapabilityPackResponse> CapabilityPacks { get; private set; } = [];
    public IReadOnlyList<OperationViewModel> Operations { get; private set; } = [];
    public IReadOnlyList<IGrouping<string, OperationViewModel>> OperationGroups { get; private set; } = [];
    public int EnabledPackCount { get; private set; }
    public int EnabledOperationCount { get; private set; }
    public int TotalOperationCount { get; private set; }
    public string? StatusMessage { get; private set; }

    public void OnGet()
    {
        LoadViewModel();
    }

    public IActionResult OnPost()
    {
        _settings.Save(EnabledCapabilityPacks, EnabledOperations);
        StatusMessage = "Beta 1 automation settings saved.";
        LoadViewModel();
        return Page();
    }

    private void LoadViewModel()
    {
        CapabilityPacks = _registry.GetCapabilityPacks();
        var descriptors = _registry.GetOperationDescriptors();
        var enabledPacks = _settings.GetEnabledCapabilityPacks().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabledOperations = _settings.GetEnabledOperations().ToHashSet(StringComparer.OrdinalIgnoreCase);

        EnabledCapabilityPacks = CapabilityPacks
            .Where(pack => enabledPacks.Contains(pack.Name))
            .Select(pack => pack.Name)
            .ToList();

        EnabledOperations = descriptors
            .Where(operation => enabledOperations.Contains(operation.Type))
            .Select(operation => operation.Type)
            .ToList();

        Operations = descriptors
            .Select(operation => new OperationViewModel
            {
                Type = operation.Type,
                CapabilityPack = operation.CapabilityPack,
                Description = operation.Description,
                Intent = operation.Intent,
                RequiresTxExecution = operation.RequiresTxExecution,
                IsSelected = enabledOperations.Contains(operation.Type),
                IsEffective = enabledPacks.Contains(operation.CapabilityPack)
                              && enabledOperations.Contains(operation.Type)
            })
            .OrderBy(operation => operation.CapabilityPack, StringComparer.OrdinalIgnoreCase)
            .ThenBy(operation => operation.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

        OperationGroups = Operations
            .GroupBy(operation => operation.CapabilityPack)
            .ToList();

        EnabledPackCount = CapabilityPacks.Count(pack => pack.Enabled);
        EnabledOperationCount = Operations.Count(operation => operation.IsEffective);
        TotalOperationCount = Operations.Count;
    }

    public sealed class OperationViewModel
    {
        public string Type { get; set; } = string.Empty;
        public string CapabilityPack { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public bool RequiresTxExecution { get; set; }
        public bool IsSelected { get; set; }
        public bool IsEffective { get; set; }
    }
}
