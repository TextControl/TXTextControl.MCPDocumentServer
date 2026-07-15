using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public interface ICapabilityPack
{
    string Name { get; }
    string Description { get; }
    IReadOnlyCollection<string> OperationTypes { get; }
}
