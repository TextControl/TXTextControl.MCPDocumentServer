using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services.Operations;

public interface IDocumentOperationHandler
{
    string Type { get; }
    string CapabilityPack { get; }
    DocumentOperationDescriptor Descriptor { get; }
    OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index);
}
