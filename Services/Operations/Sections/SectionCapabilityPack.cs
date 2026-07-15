using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class SectionCapabilityPack : ICapabilityPack
{
    public const string PackName = "Sections";
    public const string InsertSectionBreak = "insert_section_break";
    public const string SetSectionLayout = "set_section_layout";

    public string Name => PackName;

    public string Description =>
        "Configures document section layout such as page size, orientation, and page margins.";

    public IReadOnlyCollection<string> OperationTypes { get; } =
    [
        InsertSectionBreak,
        SetSectionLayout
    ];
}
