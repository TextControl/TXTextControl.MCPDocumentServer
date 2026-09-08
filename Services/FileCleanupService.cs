using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.IO;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services;

public sealed class FileCleanupService : BackgroundService
{
    private readonly DocumentSessionService _sessions;
    private readonly TimeSpan _maxAge;
    private readonly ILogger<FileCleanupService> _logger;

    public FileCleanupService(DocumentSessionService sessions, IOptions<McpServerOptions> options)
        : this(sessions, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<FileCleanupService>.Instance)
    {
    }

    public FileCleanupService(DocumentSessionService sessions, IOptions<McpServerOptions> options, ILogger<FileCleanupService> logger)
    {
        _sessions = sessions;
        _logger = logger;
        _maxAge = TimeSpan.FromHours(Math.Max(1, options.Value.SessionMaxAgeHours));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { _sessions.CleanupOlderThan(_maxAge); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temporary storage failure must not stop the MCP host.
                _logger.LogWarning(exception, "Session cleanup failed; it will retry on the next interval.");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
