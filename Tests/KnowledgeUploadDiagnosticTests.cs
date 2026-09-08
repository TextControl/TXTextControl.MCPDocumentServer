using TxTextControl.McpServer.Services;
using Xunit;

namespace TxTextControl.McpServer.Tests;

[Collection("Knowledge native license isolation")]
public sealed class KnowledgeUploadDiagnosticTests
{
    [SkippableFact]
    public async Task UploadedPdfCanBeExtracted()
    {
        var directory = Environment.GetEnvironmentVariable("TX_KNOWLEDGE_DIAGNOSTIC_DIRECTORY");
        Skip.If(string.IsNullOrEmpty(directory), "Opt-in local upload diagnostic.");
        foreach (var path in Directory.GetFiles(directory!, "*.pdf"))
        {
            var blocks = await new KnowledgeDocumentExtractionService().ExtractAsync(path, await File.ReadAllBytesAsync(path), CancellationToken.None);
            Assert.NotEmpty(blocks);
        }
    }
}
