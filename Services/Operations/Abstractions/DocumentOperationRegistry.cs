using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services.Admin;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class DocumentOperationRegistry
{
    private readonly Dictionary<string, IDocumentOperationHandler> _handlers;
    private readonly Dictionary<string, ICapabilityPack> _capabilityPacks;
    private readonly AutomationSettingsService _settings;

    public DocumentOperationRegistry(
        IEnumerable<IDocumentOperationHandler> handlers,
        IEnumerable<ICapabilityPack> capabilityPacks,
        AutomationSettingsService settings)
    {
        _handlers = handlers.ToDictionary(handler => handler.Type, StringComparer.OrdinalIgnoreCase);
        _capabilityPacks = capabilityPacks.ToDictionary(pack => pack.Name, StringComparer.OrdinalIgnoreCase);
        _settings = settings;
    }

    public IDocumentOperationHandler GetRequiredHandler(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("operation.type is required.");
        }

        var normalizedType = type.Trim();
        if (!_handlers.TryGetValue(normalizedType, out var handler))
        {
            throw new InvalidOperationException($"Operation '{normalizedType}' is not registered.");
        }

        if (!_settings.IsCapabilityPackEnabled(handler.CapabilityPack))
        {
            throw new InvalidOperationException($"Capability pack '{handler.CapabilityPack}' is disabled.");
        }

        if (!_settings.IsOperationEnabled(handler.Type))
        {
            throw new InvalidOperationException($"Operation '{handler.Type}' is disabled.");
        }

        return handler;
    }

    public IReadOnlyList<DocumentAutomationCapability> GetCapabilities()
        => _handlers.Values
            .OrderBy(handler => handler.CapabilityPack)
            .ThenBy(handler => handler.Type)
            .Select(handler => new DocumentAutomationCapability
            {
                Type = handler.Type,
                CapabilityPack = handler.CapabilityPack,
                Enabled = _settings.IsCapabilityPackEnabled(handler.CapabilityPack)
                          && _settings.IsOperationEnabled(handler.Type)
            })
            .ToList();

    public IReadOnlyList<DocumentOperationDescriptor> GetOperationDescriptors()
        => _handlers.Values
            .OrderBy(handler => handler.CapabilityPack)
            .ThenBy(handler => handler.Type)
            .Select(handler =>
            {
                var descriptor = handler.Descriptor;
                descriptor.Enabled = _settings.IsCapabilityPackEnabled(handler.CapabilityPack)
                                     && _settings.IsOperationEnabled(handler.Type);
                return descriptor;
            })
            .ToList();

    public IReadOnlyList<CapabilityPackResponse> GetCapabilityPacks()
        => _capabilityPacks.Values
            .OrderBy(pack => pack.Name)
            .Select(pack => new CapabilityPackResponse
            {
                Name = pack.Name,
                Description = pack.Description,
                Enabled = _settings.IsCapabilityPackEnabled(pack.Name),
                OperationTypes = pack.OperationTypes
                    .OrderBy(operationType => operationType)
                    .ToList()
            })
            .ToList();

}
