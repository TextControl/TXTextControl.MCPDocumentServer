namespace TxTextControl.McpServer.Options;

/// <summary>Configures the bounded out-of-process document engine worker pool.</summary>
public sealed class DocumentWorkerPoolOptions
{
    public const string SectionName = "DocumentWorkerPool";

    public bool Enabled { get; set; } = true;
    public int WorkerCount { get; set; } = 2;
    public int InteractiveWorkerCount { get; set; }
    public int QueueCapacity { get; set; } = 128;
    public int StartupTimeoutSeconds { get; set; } = 30;
    public int CommandTimeoutSeconds { get; set; } = 180;
    public int SessionAffinityIdleSeconds { get; set; } = 120;
    public int MaximumHotSessions { get; set; } = 2;
    public int WorkerRestartLimit { get; set; } = 5;
    public int WorkerRestartBackoffMilliseconds { get; set; } = 500;
    public int MaximumInputMegabytes { get; set; } = 64;
    public int MaximumOutputMegabytes { get; set; } = 128;
    public string? WorkerExecutablePath { get; set; }

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(WorkerCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(WorkerCount, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(InteractiveWorkerCount, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(InteractiveWorkerCount, WorkerCount - 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, WorkerCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(StartupTimeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(CommandTimeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(SessionAffinityIdleSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumHotSessions, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(WorkerRestartLimit, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(WorkerRestartBackoffMilliseconds, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumInputMegabytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumOutputMegabytes, 1);
        if (!string.IsNullOrWhiteSpace(WorkerExecutablePath)
            && !File.Exists(Path.GetFullPath(WorkerExecutablePath)))
        {
            throw new FileNotFoundException("The configured document worker executable was not found.", WorkerExecutablePath);
        }
    }
}
