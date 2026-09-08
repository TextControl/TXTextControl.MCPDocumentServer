using System.Buffers.Binary;
using System.Text.Json;

namespace TxTextControl.McpServer.Services.Workers;

internal static class DocumentWorkerProtocol
{
    public const int Version = 1;
    public const int HeaderSize = sizeof(int);

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task WriteAsync<T>(
        Stream stream,
        T message,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > maximumBytes)
        {
            throw new InvalidOperationException($"Document worker message exceeds the {maximumBytes} byte limit.");
        }

        byte[] header = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[HeaderSize];
        int headerBytes = await ReadAtMostAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0)
        {
            return default;
        }

        if (headerBytes != HeaderSize)
        {
            throw new EndOfStreamException("Document worker protocol header ended unexpectedly.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maximumBytes)
        {
            throw new InvalidDataException($"Document worker frame length {length} is invalid.");
        }

        byte[] payload = new byte[length];
        int payloadBytes = await ReadAtMostAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != length)
        {
            throw new EndOfStreamException("Document worker protocol payload ended unexpectedly.");
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new InvalidDataException("Document worker returned an empty protocol object.");
    }

    private static async Task<int> ReadAtMostAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = await stream.ReadAsync(destination[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}

internal sealed record DocumentWorkerReady(
    int ProtocolVersion,
    int ProcessId,
    long InitializationMilliseconds);

internal sealed record DocumentWorkerRequest(
    int ProtocolVersion,
    string RequestId,
    string Command,
    string? SessionKey,
    long? ExpectedRevision,
    long DeadlineUnixMilliseconds,
    bool Mutation,
    string? MutationId,
    JsonElement Payload,
    TxTextControl.McpServer.Options.DocumentAutomationOptions? AutomationSettings = null);

internal sealed record DocumentWorkerResponse(
    int ProtocolVersion,
    string RequestId,
    bool Success,
    JsonElement? Result,
    long? NewRevision,
    string? MutationId,
    DocumentWorkerError? Error,
    DocumentWorkerTiming Timing);

internal sealed record DocumentWorkerError(string Code, string Message, string? Details);

internal sealed record DocumentWorkerTiming(
    long DocumentLoadMilliseconds,
    long ExecutionMilliseconds,
    long SaveMilliseconds,
    long SerializationMilliseconds,
    bool? SessionCacheHit);
