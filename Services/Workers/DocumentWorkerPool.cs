using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services.Workers;

public sealed partial class DocumentWorkerPool : IHostedService, IAsyncDisposable
{
    private readonly DocumentWorkerPoolOptions options;
    private readonly IHostEnvironment environment;
    private readonly PathResolver paths;
    private readonly ILogger<DocumentWorkerPool> logger;
    private readonly TxTextControl.McpServer.Services.Admin.AutomationSettingsService? automationSettings;
    private readonly string allowedRoot;
    private readonly ObservableGauge<int> queueDepthGauge;
    private readonly ObservableGauge<int> activeCommandsGauge;
    private readonly ObservableGauge<int> healthyWorkersGauge;
    private readonly ConcurrentDictionary<string, SessionSequencer> sessionSequencers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AffinityEntry> affinities = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> revisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim availability = new(0);
    private readonly SemaphoreSlim queueSlots;
    private DocumentWorkerClient[] workers = [];
    private CancellationTokenSource? maintenanceCancellation;
    private Task? maintenanceTask;
    private volatile bool started;
    private int queueDepth;
    private int activeCommands;
    private int disposed;

    public DocumentWorkerPool(
        IOptions<DocumentWorkerPoolOptions> options,
        IHostEnvironment environment,
        PathResolver paths,
        ILogger<DocumentWorkerPool> logger,
        TxTextControl.McpServer.Services.Admin.AutomationSettingsService? automationSettings = null)
    {
        this.options = options.Value;
        this.options.Validate();
        this.environment = environment;
        this.paths = paths;
        this.logger = logger;
        this.automationSettings = automationSettings;
        allowedRoot = Path.GetFullPath(paths.GetBasePath());
        queueSlots = new SemaphoreSlim(this.options.QueueCapacity, this.options.QueueCapacity);
        queueDepthGauge = DocumentWorkerMetrics.CreateQueueDepthGauge(() => QueueDepth);
        activeCommandsGauge = DocumentWorkerMetrics.CreateActiveCommandsGauge(() => ActiveCommands);
        healthyWorkersGauge = DocumentWorkerMetrics.CreateHealthyWorkersGauge(
            () => workers.Count(worker => worker.IsHealthy));
    }

    public int WorkerCount => workers.Length;
    public int QueueDepth => Volatile.Read(ref queueDepth);
    public int ActiveCommands => Volatile.Read(ref activeCommands);

    public DocumentWorkerPoolStatus GetStatus() => new(
        options.Enabled,
        started,
        workers.Length,
        QueueDepth,
        ActiveCommands,
        workers.Select(worker => new DocumentWorkerStatus(
            worker.WorkerIndex,
            worker.ProcessId,
            worker.IsHealthy,
            worker.HotSessionKey,
            worker.RestartCount,
            worker.WorkerIndex < options.InteractiveWorkerCount,
            worker.WorkingSetBytes,
            worker.LastUsedUtc)).ToArray());

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || started)
        {
            return;
        }

        workers = Enumerable.Range(0, options.WorkerCount)
            .Select(index => new DocumentWorkerClient(index, environment.ContentRootPath, allowedRoot, options))
            .ToArray();
        try
        {
            DocumentWorkerReady[] readyWorkers = await Task.WhenAll(
                workers.Select(worker => worker.StartAsync(cancellationToken))).ConfigureAwait(false);
            for (int index = 0; index < workers.Length; index++)
            {
                DocumentWorkerClient worker = workers[index];
                DocumentWorkerReady ready = readyWorkers[index];
                DocumentWorkerMetrics.WorkersStarted.Add(1);
                DocumentWorkerMetrics.StartupDuration.Record(ready.InitializationMilliseconds);
                LogWorkerStarted(logger, worker.WorkerIndex, ready.ProcessId, ready.InitializationMilliseconds);
            }

            started = true;
            maintenanceCancellation = new CancellationTokenSource();
            maintenanceTask = MaintainAffinityAsync(maintenanceCancellation.Token);
        }
        catch
        {
            await StopWorkersAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        started = false;
        CancellationTokenSource? stoppingMaintenance = Interlocked.Exchange(ref maintenanceCancellation, null);
        Task? stoppingTask = Interlocked.Exchange(ref maintenanceTask, null);
        if (stoppingMaintenance is not null)
        {
            await stoppingMaintenance.CancelAsync().ConfigureAwait(false);
        }

        if (stoppingTask is not null)
        {
            try
            {
                await stoppingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        stoppingMaintenance?.Dispose();

        await StopWorkersAsync().ConfigureAwait(false);
    }

    internal T Execute<T>(
        string command,
        object payload,
        string? sessionKey,
        bool mutation,
        CancellationToken cancellationToken = default)
        => ExecuteAsync<T>(command, payload, sessionKey, mutation, cancellationToken)
            .GetAwaiter().GetResult();

    internal async Task<T> ExecuteAsync<T>(
        string command,
        object payload,
        string? sessionKey,
        bool mutation,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("The document worker pool is disabled.");
        }

        if (!started)
        {
            throw new InvalidOperationException("The document worker pool has not started.");
        }

        if (!queueSlots.Wait(0))
        {
            DocumentWorkerMetrics.QueueRejected.Add(1);
            throw new InvalidOperationException(
                $"The document worker queue is full ({options.QueueCapacity} commands). Retry shortly.");
        }

        Interlocked.Increment(ref queueDepth);
        SessionLease? sessionLease = sessionKey is null
            ? null
            : sessionSequencers.GetOrAdd(sessionKey, _ => new SessionSequencer()).Enqueue();
        bool sessionLeaseEntered = false;
        bool removedFromQueue = false;
        Stopwatch queueWait = Stopwatch.StartNew();
        try
        {
            if (sessionLease is not null)
            {
                await sessionLease.WaitAsync(cancellationToken).ConfigureAwait(false);
                sessionLeaseEntered = true;
            }

            bool interactive = IsInteractiveCommand(command);
            DocumentWorkerClient worker = await AcquireWorkerAsync(sessionKey, interactive, cancellationToken).ConfigureAwait(false);
            queueWait.Stop();
            Interlocked.Decrement(ref queueDepth);
            removedFromQueue = true;
            Interlocked.Increment(ref activeCommands);
            try
            {
                long? expectedRevision = sessionKey is null
                    ? null
                    : revisions.GetOrAdd(sessionKey, key => ReadDurableRevision(key, payload));
                var request = new DocumentWorkerRequest(
                    DocumentWorkerProtocol.Version,
                    Guid.NewGuid().ToString("N"),
                    command,
                    sessionKey,
                    expectedRevision,
                    DateTimeOffset.UtcNow.AddSeconds(options.CommandTimeoutSeconds).ToUnixTimeMilliseconds(),
                    mutation,
                    mutation ? Guid.NewGuid().ToString("N") : null,
                    JsonSerializer.SerializeToElement(payload, payload.GetType(), DocumentWorkerProtocol.JsonOptions),
                    automationSettings?.GetWorkerSnapshot());
                KeyValuePair<string, object?> commandTag = DocumentWorkerMetrics.CommandTag(command);
                DocumentWorkerMetrics.InputSize.Record(
                    System.Text.Encoding.UTF8.GetByteCount(request.Payload.GetRawText()),
                    commandTag);
                DocumentWorkerResponse response = await ExecuteOnWorkerAsync(worker, request, mutation, cancellationToken)
                    .ConfigureAwait(false);
                if (!response.Success)
                {
                    DocumentWorkerMetrics.Failures.Add(1, DocumentWorkerMetrics.CommandTag(command));
                    logger.LogError(
                        "Document worker {WorkerIndex} returned {ErrorCode} for {Command}: {Details}",
                        worker.WorkerIndex,
                        response.Error?.Code,
                        command,
                        response.Error?.Details);
                    throw new DocumentWorkerCommandException(
                        response.Error?.Code ?? "engine_error",
                        response.Error?.Message ?? "The document worker command failed.");
                }

                if (mutation && sessionKey is not null && response.NewRevision.HasValue)
                {
                    revisions[sessionKey] = response.NewRevision.Value;
                }

                if (sessionKey is not null)
                {
                    RecordAffinity(sessionKey, worker);
                }

                LogCommandCompleted(
                    logger,
                    command,
                    worker.WorkerIndex,
                    queueWait.ElapsedMilliseconds,
                    response.Timing.ExecutionMilliseconds);
                DocumentWorkerMetrics.QueueDuration.Record(queueWait.Elapsed.TotalMilliseconds, commandTag);
                DocumentWorkerMetrics.ExecutionDuration.Record(response.Timing.ExecutionMilliseconds, commandTag);
                DocumentWorkerMetrics.LoadDuration.Record(response.Timing.DocumentLoadMilliseconds, commandTag);
                DocumentWorkerMetrics.SaveDuration.Record(response.Timing.SaveMilliseconds, commandTag);
                DocumentWorkerMetrics.SerializationDuration.Record(response.Timing.SerializationMilliseconds, commandTag);
                DocumentWorkerMetrics.OutputSize.Record(
                    response.Result is JsonElement measuredResult
                        ? System.Text.Encoding.UTF8.GetByteCount(measuredResult.GetRawText())
                        : 0,
                    commandTag);
                if (response.Timing.SessionCacheHit == true)
                {
                    DocumentWorkerMetrics.CacheHits.Add(1, commandTag);
                }
                else if (response.Timing.SessionCacheHit == false)
                {
                    DocumentWorkerMetrics.CacheMisses.Add(1, commandTag);
                }

                if (response.Result is not JsonElement result)
                {
                    throw new InvalidDataException("The document worker returned no result.");
                }

                return result.Deserialize<T>(DocumentWorkerProtocol.JsonOptions)!;
            }
            finally
            {
                Interlocked.Decrement(ref activeCommands);
                worker.LastUsedUtc = DateTime.UtcNow;
                worker.ExecutionGate.Release();
                availability.Release();
            }
        }
        finally
        {
            if (!removedFromQueue)
            {
                Interlocked.Decrement(ref queueDepth);
            }

            sessionLease?.Complete(sessionLeaseEntered);

            queueSlots.Release();
        }
    }

    private async Task<DocumentWorkerResponse> ExecuteOnWorkerAsync(
        DocumentWorkerClient worker,
        DocumentWorkerRequest request,
        bool mutation,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CreateDeadline(cancellationToken);
        try
        {
            return await worker.ExecuteAsync(request, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            KeyValuePair<string, object?> commandTag = DocumentWorkerMetrics.CommandTag(request.Command);
            DocumentWorkerMetrics.Failures.Add(1, commandTag);
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                DocumentWorkerMetrics.Timeouts.Add(1, commandTag);
            }

            LogWorkerFailed(logger, worker.WorkerIndex, request.Command, exception);
            // Once a framed request has been written, cancellation can leave a late
            // response on the stream. Replace the worker before releasing its gate so
            // a subsequent command can never consume the canceled command's response.
            await worker.RestartAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (mutation)
            {
                if (TryReadCommittedMutation(request, out DocumentWorkerResponse? committed))
                {
                    DocumentWorkerMetrics.RecoveredCommits.Add(1, commandTag);
                    return committed!;
                }

                throw new InvalidOperationException(
                    "The document worker stopped during a mutation. The command was not retried because its commit status is ambiguous; inspect the session and retry explicitly.",
                    exception);
            }

            using CancellationTokenSource retryDeadline = CreateDeadline(cancellationToken);
            DocumentWorkerMetrics.Retries.Add(1, commandTag);
            DocumentWorkerRequest retry = request with
            {
                RequestId = Guid.NewGuid().ToString("N"),
                DeadlineUnixMilliseconds = DateTimeOffset.UtcNow
                    .AddSeconds(options.CommandTimeoutSeconds)
                    .ToUnixTimeMilliseconds(),
            };
            return await worker.ExecuteAsync(retry, retryDeadline.Token).ConfigureAwait(false);
        }
    }

    private bool TryReadCommittedMutation(
        DocumentWorkerRequest request,
        out DocumentWorkerResponse? response)
    {
        response = null;
        if (request.MutationId is null
            || !request.Payload.TryGetProperty("workingDocumentPath", out JsonElement pathProperty))
        {
            return false;
        }

        string? workingDocumentPath = pathProperty.GetString();
        if (workingDocumentPath is null || !IsManagedPath(workingDocumentPath))
        {
            return false;
        }

        string? directory = workingDocumentPath is null ? null : Path.GetDirectoryName(workingDocumentPath);
        if (directory is null)
        {
            return false;
        }

        string commitPath = Path.Combine(directory, DocumentWorkerProcess.CommitFileName);
        try
        {
            DocumentWorkerResponse? committed = JsonSerializer.Deserialize<DocumentWorkerResponse>(
                File.ReadAllBytes(commitPath),
                DocumentWorkerProtocol.JsonOptions);
            if (committed is { Success: true }
                && string.Equals(committed.MutationId, request.MutationId, StringComparison.Ordinal))
            {
                response = committed;
                return true;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private CancellationTokenSource CreateDeadline(CancellationToken cancellationToken)
    {
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.CommandTimeoutSeconds));
        return deadline;
    }

    private async Task<DocumentWorkerClient> AcquireWorkerAsync(
        string? sessionKey,
        bool interactive,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (sessionKey is not null
                && affinities.TryGetValue(sessionKey, out AffinityEntry? affinity)
                && DateTime.UtcNow - affinity.LastUsedUtc <= TimeSpan.FromSeconds(options.SessionAffinityIdleSeconds))
            {
                DocumentWorkerClient preferred = workers[affinity.WorkerIndex];
                if ((interactive || preferred.WorkerIndex >= options.InteractiveWorkerCount)
                    && preferred.ExecutionGate.Wait(0))
                {
                    DocumentWorkerMetrics.AffinityHits.Add(1);
                    return preferred;
                }
            }

            IEnumerable<DocumentWorkerClient> candidates = interactive
                ? workers
                : workers.Skip(options.InteractiveWorkerCount);
            foreach (DocumentWorkerClient worker in candidates.OrderBy(candidate => candidate.LastUsedUtc))
            {
                if (worker.ExecutionGate.Wait(0))
                {
                    if (sessionKey is not null)
                    {
                        DocumentWorkerMetrics.AffinityMisses.Add(1);
                    }

                    return worker;
                }
            }

            await availability.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsInteractiveCommand(string command) => command is
        "apply_operations" or
        "format_text" or
        "get_paragraphs" or
        "search_text" or
        "search_ranges" or
        "get_text" or
        "edit_document" or
        "content_snapshot" or
        "template_fields" or
        "template_blocks" or
        "template_form_fields" or
        "template_snapshot";

    private void RecordAffinity(string sessionKey, DocumentWorkerClient worker)
    {
        if (options.MaximumHotSessions == 0)
        {
            affinities.Clear();
            worker.HotSessionKey = null;
            return;
        }

        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, AffinityEntry> existing in affinities
            .Where(entry => entry.Value.WorkerIndex == worker.WorkerIndex
                && !entry.Key.Equals(sessionKey, StringComparison.OrdinalIgnoreCase)))
        {
            if (affinities.TryRemove(existing.Key, out _))
            {
                DocumentWorkerMetrics.AffinityEvictions.Add(1);
            }
        }

        worker.HotSessionKey = sessionKey;
        affinities[sessionKey] = new AffinityEntry(worker.WorkerIndex, now);
        int maximum = options.MaximumHotSessions;
        if (affinities.Count <= maximum)
        {
            return;
        }

        foreach (KeyValuePair<string, AffinityEntry> stale in affinities
            .OrderBy(entry => entry.Value.LastUsedUtc)
            .Take(affinities.Count - maximum))
        {
            if (affinities.TryRemove(stale.Key, out _))
            {
                DocumentWorkerMetrics.AffinityEvictions.Add(1);
            }
        }
    }

    private async Task MaintainAffinityAsync(CancellationToken cancellationToken)
    {
        TimeSpan idleTimeout = TimeSpan.FromSeconds(options.SessionAffinityIdleSeconds);
        TimeSpan interval = TimeSpan.FromSeconds(Math.Clamp(options.SessionAffinityIdleSeconds / 2, 1, 30));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            DateTime threshold = DateTime.UtcNow - idleTimeout;
            foreach (DocumentWorkerClient worker in workers)
            {
                string? sessionKey = worker.HotSessionKey;
                if (sessionKey is null || worker.LastUsedUtc > threshold || !worker.ExecutionGate.Wait(0))
                {
                    continue;
                }

                try
                {
                    var request = new DocumentWorkerRequest(
                        DocumentWorkerProtocol.Version,
                        Guid.NewGuid().ToString("N"),
                        "unload",
                        null,
                        null,
                        DateTimeOffset.UtcNow.AddSeconds(options.CommandTimeoutSeconds).ToUnixTimeMilliseconds(),
                        false,
                        null,
                        JsonSerializer.SerializeToElement(new { }, DocumentWorkerProtocol.JsonOptions));
                    using CancellationTokenSource deadline = CreateDeadline(cancellationToken);
                    DocumentWorkerResponse response = await worker.ExecuteAsync(request, deadline.Token).ConfigureAwait(false);
                    if (response.Success)
                    {
                        worker.HotSessionKey = null;
                        if (affinities.TryRemove(sessionKey, out _))
                        {
                            DocumentWorkerMetrics.AffinityEvictions.Add(1);
                        }
                    }
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    LogWorkerFailed(logger, worker.WorkerIndex, "unload", exception);
                    await worker.RestartAsync(cancellationToken).ConfigureAwait(false);
                    worker.HotSessionKey = null;
                    affinities.TryRemove(sessionKey, out _);
                }
                finally
                {
                    worker.ExecutionGate.Release();
                    availability.Release();
                }
            }
        }
    }

    private static long ReadDurableRevision(string sessionKey, object payload)
    {
        long durableRevision = 0;
        JsonElement payloadJson = JsonSerializer.SerializeToElement(
            payload,
            payload.GetType(),
            DocumentWorkerProtocol.JsonOptions);
        if (payloadJson.TryGetProperty("state", out JsonElement state)
            && state.ValueKind == JsonValueKind.Object
            && state.TryGetProperty("revision", out JsonElement revision)
            && revision.TryGetInt64(out long payloadRevision))
        {
            durableRevision = payloadRevision;
        }

        string? workingDocumentPath = payloadJson.TryGetProperty("workingDocumentPath", out JsonElement path)
            ? path.GetString()
            : null;
        string? directory = workingDocumentPath is null ? null : Path.GetDirectoryName(workingDocumentPath);
        if (directory is null)
        {
            return durableRevision;
        }

        string statePath = Path.Combine(directory, "document.state.json");
        try
        {
            using JsonDocument stateDocument = JsonDocument.Parse(File.ReadAllBytes(statePath));
            if (stateDocument.RootElement.TryGetProperty("Revision", out JsonElement persistedRevision)
                && persistedRevision.TryGetInt64(out long value))
            {
                durableRevision = Math.Max(durableRevision, value);
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        string commitPath = Path.Combine(directory, DocumentWorkerProcess.CommitFileName);
        try
        {
            DocumentWorkerResponse? commit = JsonSerializer.Deserialize<DocumentWorkerResponse>(
                File.ReadAllBytes(commitPath),
                DocumentWorkerProtocol.JsonOptions);
            if (commit is { Success: true, NewRevision: long committedRevision })
            {
                durableRevision = Math.Max(durableRevision, committedRevision);
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return durableRevision;
    }

    private bool IsManagedPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootPrefix = allowedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(rootPrefix, comparison);
    }

    private async Task StopWorkersAsync()
    {
        DocumentWorkerClient[] stopping = workers;
        workers = [];
        foreach (DocumentWorkerClient worker in stopping)
        {
            await worker.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        availability.Dispose();
        queueSlots.Dispose();
    }

    private sealed record AffinityEntry(int WorkerIndex, DateTime LastUsedUtc);

    private sealed class SessionSequencer
    {
        private readonly object syncRoot = new();
        private Task tail = Task.CompletedTask;

        public SessionLease Enqueue()
        {
            lock (syncRoot)
            {
                Task predecessor = tail;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                tail = completion.Task;
                return new SessionLease(predecessor, completion);
            }
        }
    }

    private sealed class SessionLease(Task predecessor, TaskCompletionSource completion)
    {
        public Task WaitAsync(CancellationToken cancellationToken) => predecessor.WaitAsync(cancellationToken);

        public void Complete(bool entered)
        {
            if (entered || predecessor.IsCompleted)
            {
                completion.TrySetResult();
                return;
            }

            _ = predecessor.ContinueWith(
                static (_, state) => ((TaskCompletionSource)state!).TrySetResult(),
                completion,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Document worker {WorkerIndex} started as PID {ProcessId} in {InitializationMilliseconds} ms.")]
    private static partial void LogWorkerStarted(ILogger logger, int workerIndex, int processId, long initializationMilliseconds);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug,
        Message = "Document command {Command} completed on worker {WorkerIndex}; queue {QueueMilliseconds} ms, execution {ExecutionMilliseconds} ms.")]
    private static partial void LogCommandCompleted(ILogger logger, string command, int workerIndex, long queueMilliseconds, long executionMilliseconds);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "Document worker {WorkerIndex} failed while running {Command}; restarting it.")]
    private static partial void LogWorkerFailed(ILogger logger, int workerIndex, string command, Exception exception);
}

public sealed class DocumentWorkerCommandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record DocumentWorkerPoolStatus(
    bool Enabled,
    bool Started,
    int WorkerCount,
    int QueueDepth,
    int ActiveCommands,
    IReadOnlyList<DocumentWorkerStatus> Workers);

public sealed record DocumentWorkerStatus(
    int WorkerIndex,
    int ProcessId,
    bool Healthy,
    string? HotSessionKey,
    int RestartCount,
    bool ReservedForInteractiveCommands,
    long WorkingSetBytes,
    DateTime LastUsedUtc);
