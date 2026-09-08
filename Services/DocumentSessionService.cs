using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Services.Workers;

namespace TxTextControl.McpServer.Services;

public sealed class DocumentSessionService
{
    private static readonly JsonSerializerOptions StateSerializerOptions = new();
    private readonly ConcurrentDictionary<string, DocumentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly PathResolver _paths;
    private readonly object lifecycleGate = new();

    public DocumentSessionService(PathResolver paths)
    {
        _paths = paths;
    }

    public DocumentSession Create(string? sessionId = null)
    {
        lock (lifecycleGate) return CreateCore(sessionId);
    }

    private DocumentSession CreateCore(string? sessionId)
    {
        sessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : ValidateSessionId(sessionId);

        var workingDirectory = Path.Combine(_paths.GetSessionsRoot(), sessionId);
        if (Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"Session '{sessionId}' already exists; load it instead of overwriting it.");
        Directory.CreateDirectory(workingDirectory);

        var session = new DocumentSession
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectory,
            StatePath = Path.Combine(workingDirectory, "document.state.json"),
            WorkingDocumentPath = Path.Combine(workingDirectory, "document.tx"),
            CreatedUtc = DateTime.UtcNow,
            LastAccessUtc = DateTime.UtcNow
        };

        SaveState(session, new DocumentState
        {
            WorkingDocumentPath = session.WorkingDocumentPath
        }, documentChanged: false);

        _sessions[session.SessionId] = session;
        return session;
    }

    public DocumentSession Get(string sessionId)
    {
        // Restoring one session must produce one shared SyncRoot for all concurrent requests.
        lock (lifecycleGate) return GetCore(sessionId);
    }

    private DocumentSession GetCore(string sessionId)
    {
        sessionId = ValidateSessionId(sessionId);

        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            existing.LastAccessUtc = DateTime.UtcNow;
            return existing;
        }

        var workingDirectory = Path.Combine(_paths.GetSessionsRoot(), sessionId);
        var statePath = Path.Combine(workingDirectory, "document.state.json");
        var workingDocumentPath = Path.Combine(workingDirectory, "document.tx");

        if (!File.Exists(statePath))
        {
            throw new FileNotFoundException($"Session '{sessionId}' was not found.", statePath);
        }

        var restored = new DocumentSession
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectory,
            StatePath = statePath,
            WorkingDocumentPath = workingDocumentPath,
            CreatedUtc = Directory.GetCreationTimeUtc(workingDirectory),
            LastAccessUtc = DateTime.UtcNow
        };

        _sessions[restored.SessionId] = restored;
        return restored;
    }

    public DocumentState LoadState(DocumentSession session)
    {
        lock (session.SyncRoot)
        {
            if (session.CachedState is not null)
            {
                return session.CachedState;
            }

            var json = File.ReadAllText(session.StatePath);
            session.CachedState = JsonSerializer.Deserialize<DocumentState>(json, StateSerializerOptions)
                ?? new DocumentState { WorkingDocumentPath = session.WorkingDocumentPath };
            session.CachedState = RestoreCommittedState(session, session.CachedState);
            return session.CachedState;
        }
    }

    public void SaveState(DocumentSession session, DocumentState state, bool documentChanged = true)
    {
        lock (session.SyncRoot)
        {
            long currentRevision = session.CachedState?.Revision ?? 0;
            state.Revision = documentChanged
                ? Math.Max(state.Revision, currentRevision) + 1
                : Math.Max(state.Revision, currentRevision);
            state.WorkingDocumentPath = session.WorkingDocumentPath;
            session.LastAccessUtc = DateTime.UtcNow;
            session.CachedState = state;
            if (documentChanged)
            {
                session.CachedContent = state.ContentSnapshot;
                session.CachedTemplate = null;
            }

            var json = JsonSerializer.Serialize(state, StateSerializerOptions);
            string temporaryPath = Path.Combine(
                session.WorkingDirectory,
                $".{Path.GetFileName(session.StatePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, session.StatePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    public bool Delete(string sessionId)
    {
        sessionId = ValidateSessionId(sessionId);
        lock (lifecycleGate)
        {
            _sessions.TryGetValue(sessionId, out var session);
            bool entered = session is not null && Monitor.TryEnter(session.SyncRoot);
            if (session is not null && !entered)
                throw new InvalidOperationException("The document session is in use. Retry deletion after the operation finishes.");
            try { return DeleteCore(sessionId); }
            finally { if (entered) Monitor.Exit(session!.SyncRoot); }
        }
    }

    private bool DeleteCore(string sessionId)
    {
        var workingDirectory = Path.Combine(_paths.GetSessionsRoot(), sessionId);
        if (!Directory.Exists(workingDirectory))
        {
            _sessions.TryRemove(sessionId, out _);
            return false;
        }

        if ((File.GetAttributes(workingDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Session deletion does not follow symbolic links or junctions.");
        Directory.Delete(workingDirectory, recursive: true);
        _sessions.TryRemove(sessionId, out _);
        return true;
    }

    public IEnumerable<string> CleanupOlderThan(TimeSpan age)
    {
        var removed = new List<string>();
        var threshold = DateTime.UtcNow - age;

        foreach (var directory in Directory.GetDirectories(_paths.GetSessionsRoot()))
        {
            try
            {
                lock (lifecycleGate)
                {
                    var info = new DirectoryInfo(directory);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTimeUtc >= threshold)
                        continue;
                    var sessionId = ValidateSessionId(Path.GetFileName(directory));
                    _sessions.TryGetValue(sessionId, out var session);
                    if (session is not null && session.LastAccessUtc >= threshold) continue;
                    bool entered = session is not null && Monitor.TryEnter(session.SyncRoot);
                    if (session is not null && !entered) continue;
                    try
                    {
                        if (DeleteCore(sessionId)) removed.Add(sessionId);
                    }
                    finally { if (entered) Monitor.Exit(session!.SyncRoot); }
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        return removed;
    }

    private static string ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("A sessionId is required.");
        }

        string value = sessionId.Trim();
        if (value.Length > 128 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "A sessionId may contain only ASCII letters, digits, '-' and '_', up to 128 characters.",
                nameof(sessionId));
        }

        return value;
    }

    private static DocumentState RestoreCommittedState(DocumentSession session, DocumentState persistedState)
    {
        string commitPath = Path.Combine(session.WorkingDirectory, DocumentWorkerProcess.CommitFileName);
        try
        {
            DocumentWorkerResponse? commit = JsonSerializer.Deserialize<DocumentWorkerResponse>(
                File.ReadAllBytes(commitPath),
                DocumentWorkerProtocol.JsonOptions);
            if (commit is not { Success: true, NewRevision: long committedRevision, Result: JsonElement result }
                || committedRevision <= persistedState.Revision)
            {
                return persistedState;
            }

            JsonElement stateElement = result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("state", out JsonElement nestedState)
                    ? nestedState
                    : result;
            DocumentState? committedState = stateElement.Deserialize<DocumentState>(DocumentWorkerProtocol.JsonOptions);
            if (committedState is null)
            {
                return persistedState;
            }

            committedState.Revision = committedRevision;
            committedState.WorkingDocumentPath = session.WorkingDocumentPath;
            return committedState;
        }
        catch (IOException)
        {
            return persistedState;
        }
        catch (JsonException)
        {
            return persistedState;
        }
    }
}
