using System.Buffers.Binary;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Workers;
using Xunit;

namespace TxTextControl.McpServer.Tests;

public sealed class DocumentWorkerProtocolTests
{
    [Fact]
    public async Task RoundTrip_PreservesVersionAndCorrelationId()
    {
        var expected = new DocumentWorkerRequest(
            DocumentWorkerProtocol.Version,
            "request-42",
            "get_text",
            "session-a",
            7,
            DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
            false,
            null,
            System.Text.Json.JsonSerializer.SerializeToElement(new { workingDocumentPath = "document.tx" }));
        await using var stream = new MemoryStream();

        await DocumentWorkerProtocol.WriteAsync(stream, expected, 4096, CancellationToken.None);
        stream.Position = 0;
        DocumentWorkerRequest? actual = await DocumentWorkerProtocol.ReadAsync<DocumentWorkerRequest>(
            stream,
            4096,
            CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal(DocumentWorkerProtocol.Version, actual.ProtocolVersion);
        Assert.Equal(expected.RequestId, actual.RequestId);
        Assert.Equal(expected.ExpectedRevision, actual.ExpectedRevision);
    }

    [Fact]
    public async Task Write_RejectsOversizedFrame()
    {
        await using var stream = new MemoryStream();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DocumentWorkerProtocol.WriteAsync(stream, new { text = new string('x', 128) }, 16, CancellationToken.None));

        Assert.Contains("exceeds", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_RejectsInvalidFrameLengthBeforeAllocatingPayload()
    {
        byte[] header = new byte[DocumentWorkerProtocol.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, 8192);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DocumentWorkerProtocol.ReadAsync<DocumentWorkerReady>(stream, 1024, CancellationToken.None));
    }

    [Fact]
    public void Options_RejectUnboundedOrInvalidConfiguration()
    {
        var options = new DocumentWorkerPoolOptions { WorkerCount = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}
