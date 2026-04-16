using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services;

public sealed class FileCleanupService : BackgroundService
{
    private readonly DocumentSessionService _sessions;
    private readonly TimeSpan _maxAge;

    public FileCleanupService(DocumentSessionService sessions, IOptions<McpServerOptions> options)
    {
        _sessions = sessions;
        _maxAge = TimeSpan.FromHours(Math.Max(1, options.Value.SessionMaxAgeHours));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _sessions.CleanupOlderThan(_maxAge);
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
