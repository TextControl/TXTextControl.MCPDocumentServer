using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class HeaderFooterCapabilityPack : ICapabilityPack
{
    public const string PackName = "HeaderFooter";
    public const string SetHeaderFooter = "set_header_footer";

    public string Name => PackName;

    public string Description =>
        "Creates and updates document headers and footers, including optional page numbers.";

    public IReadOnlyCollection<string> OperationTypes { get; } =
    [
        SetHeaderFooter
    ];
}
