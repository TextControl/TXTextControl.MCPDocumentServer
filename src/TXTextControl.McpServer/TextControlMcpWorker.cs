using TxTextControl.McpServer.Services.Workers;

namespace TxTextControl.McpServer;

/// <summary>Dispatches worker mode when the worker pool relaunches the consuming host.</summary>
public static class TextControlMcpWorker
{
    /// <summary>Runs worker mode and returns its exit code, or null for a normal host launch.</summary>
    /// <remarks>Call before building the web host or writing anything to standard output. Required for the default self-hosted worker pool.</remarks>
    public static async Task<int?> RunIfRequestedAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return DocumentWorkerProcess.IsWorker(args)
            ? await DocumentWorkerProcess.RunAsync(args).ConfigureAwait(false)
            : null;
    }
}
