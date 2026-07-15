using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TxTextControl.McpServer.Models;

namespace TxTextControl.McpServer.Services;

public sealed class DocumentSessionService
{
    private readonly ConcurrentDictionary<string, DocumentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly PathResolver _paths;

    public DocumentSessionService(PathResolver paths)
    {
        _paths = paths;
    }

    public DocumentSession Create(string? sessionId = null)
    {
        sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim();

        var workingDirectory = Path.Combine(_paths.GetSessionsRoot(), sessionId);
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
        });

        _sessions[session.SessionId] = session;
        return session;
    }

    public DocumentSession Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("A sessionId is required.");
        }

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
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
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
        var json = File.ReadAllText(session.StatePath);
        return JsonSerializer.Deserialize<DocumentState>(json)
            ?? new DocumentState { WorkingDocumentPath = session.WorkingDocumentPath };
    }

    public void SaveState(DocumentSession session, DocumentState state)
    {
        state.WorkingDocumentPath = session.WorkingDocumentPath;
        session.LastAccessUtc = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(session.StatePath, json);
    }

    public bool Delete(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);

        var workingDirectory = Path.Combine(_paths.GetSessionsRoot(), sessionId);
        if (!Directory.Exists(workingDirectory))
        {
            return false;
        }

        Directory.Delete(workingDirectory, recursive: true);
        return true;
    }

    public IEnumerable<string> CleanupOlderThan(TimeSpan age)
    {
        var removed = new List<string>();
        var threshold = DateTime.UtcNow - age;

        foreach (var directory in Directory.GetDirectories(_paths.GetSessionsRoot()))
        {
            var info = new DirectoryInfo(directory);
            if (info.LastWriteTimeUtc >= threshold)
            {
                continue;
            }

            try
            {
                var sessionId = Path.GetFileName(directory);
                Delete(sessionId);
                removed.Add(sessionId);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        return removed;
    }
}
