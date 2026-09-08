using System.Diagnostics.Metrics;

namespace TxTextControl.McpServer.Services.Workers;

internal static class DocumentWorkerMetrics
{
    private static readonly Meter Meter = new("TxTextControl.McpServer.DocumentWorkers", "1.0.0");

    public static readonly Counter<long> WorkersStarted = Meter.CreateCounter<long>("document.worker.started");
    public static readonly Counter<long> WorkerRestarts = Meter.CreateCounter<long>("document.worker.restarts");
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>("document.worker.failures");
    public static readonly Counter<long> Timeouts = Meter.CreateCounter<long>("document.worker.timeouts");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("document.worker.retries");
    public static readonly Counter<long> RecoveredCommits = Meter.CreateCounter<long>("document.worker.recovered_commits");
    public static readonly Counter<long> QueueRejected = Meter.CreateCounter<long>("document.worker.queue_rejected");
    public static readonly Counter<long> AffinityHits = Meter.CreateCounter<long>("document.worker.affinity_hits");
    public static readonly Counter<long> AffinityMisses = Meter.CreateCounter<long>("document.worker.affinity_misses");
    public static readonly Counter<long> AffinityEvictions = Meter.CreateCounter<long>("document.worker.affinity_evictions");
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("document.worker.cache_hits");
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("document.worker.cache_misses");
    public static readonly Histogram<double> StartupDuration = Meter.CreateHistogram<double>("document.worker.startup.duration", "ms");
    public static readonly Histogram<double> QueueDuration = Meter.CreateHistogram<double>("document.worker.queue.duration", "ms");
    public static readonly Histogram<double> LoadDuration = Meter.CreateHistogram<double>("document.worker.load.duration", "ms");
    public static readonly Histogram<double> ExecutionDuration = Meter.CreateHistogram<double>("document.worker.command.duration", "ms");
    public static readonly Histogram<double> SaveDuration = Meter.CreateHistogram<double>("document.worker.save.duration", "ms");
    public static readonly Histogram<double> SerializationDuration = Meter.CreateHistogram<double>("document.worker.serialization.duration", "ms");
    public static readonly Histogram<long> InputSize = Meter.CreateHistogram<long>("document.worker.input.size", "By");
    public static readonly Histogram<long> OutputSize = Meter.CreateHistogram<long>("document.worker.output.size", "By");

    public static KeyValuePair<string, object?> CommandTag(string command) => new("command", command);

    public static ObservableGauge<int> CreateQueueDepthGauge(Func<int> observe) =>
        Meter.CreateObservableGauge("document.worker.queue.depth", observe);

    public static ObservableGauge<int> CreateActiveCommandsGauge(Func<int> observe) =>
        Meter.CreateObservableGauge("document.worker.active_commands", observe);

    public static ObservableGauge<int> CreateHealthyWorkersGauge(Func<int> observe) =>
        Meter.CreateObservableGauge("document.worker.healthy", observe);
}
