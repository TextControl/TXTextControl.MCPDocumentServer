using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class MediaCapabilityPack : ICapabilityPack
{
    public const string PackName = "Media";
    public const string AppendImage = "append_image";

    public string Name => PackName;

    public string Description =>
        "Inserts TX Text Control supported image formats into a document.";

    public IReadOnlyCollection<string> OperationTypes { get; } =
    [
        AppendImage
    ];
}
