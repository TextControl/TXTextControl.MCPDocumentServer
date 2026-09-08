using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using Xunit;

namespace TxTextControl.McpServer.Tests;

public sealed class DocumentSessionServiceTests
{
    [Fact]
    public void CleanupPreservesRecentlyReadDocumentsAndRemovesIdleDocuments()
    {
        WithSessions((service, root) =>
        {
            var active = service.Create("active");
            var idle = service.Create("idle");
            DateTime old = DateTime.UtcNow.AddDays(-2);
            active.LastAccessUtc = idle.LastAccessUtc = old;
            Directory.SetLastWriteTimeUtc(active.WorkingDirectory, old);
            Directory.SetLastWriteTimeUtc(idle.WorkingDirectory, old);
            service.Get("active"); // Reading does not change directory modification time.
            Assert.Equal(new[] { "idle" }, service.CleanupOlderThan(TimeSpan.FromDays(1)));
            Assert.True(Directory.Exists(active.WorkingDirectory));
            Assert.False(Directory.Exists(idle.WorkingDirectory));
        });
    }

    [Fact]
    public void CleanupSkipsSessionsWithAnOperationInProgress()
    {
        WithSessions((service, root) =>
        {
            var session = service.Create("busy");
            session.LastAccessUtc = DateTime.UtcNow.AddDays(-2);
            Directory.SetLastWriteTimeUtc(session.WorkingDirectory, session.LastAccessUtc);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var operation = Task.Run(() => { lock (session.SyncRoot) { entered.Set(); release.Wait(); } });
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                Assert.Empty(service.CleanupOlderThan(TimeSpan.FromDays(1)));
                Assert.Throws<InvalidOperationException>(() => service.Delete(session.SessionId));
                Assert.True(Directory.Exists(session.WorkingDirectory));
            }
            finally { release.Set(); operation.GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void DuplicateCreationDoesNotOverwritePersistedState()
    {
        WithSessions((service, root) =>
        {
            var session = service.Create("existing");
            string before = File.ReadAllText(session.StatePath);
            Assert.Throws<InvalidOperationException>(() => service.Create("existing"));
            Assert.Equal(before, File.ReadAllText(session.StatePath));
            Assert.Same(session, service.Get("existing"));
        });
    }

    [Fact]
    public void ConcurrentRestoreReturnsOneSharedSessionLock()
    {
        WithSessions((service, root) =>
        {
            service.Create("restore");
            var restoredService = new DocumentSessionService(new PathResolver(
                Microsoft.Extensions.Options.Options.Create(new McpServerOptions { BasePath = root })));
            var sessions = new TxTextControl.McpServer.Models.DocumentSession[64];
            Parallel.For(0, sessions.Length, index => sessions[index] = restoredService.Get("restore"));
            Assert.All(sessions, session => Assert.Same(sessions[0], session));
        });
    }

    private static void WithSessions(Action<DocumentSessionService, string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "tx-mcp-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            test(new DocumentSessionService(new PathResolver(Microsoft.Extensions.Options.Options.Create(
                new McpServerOptions { BasePath = root }))), root);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("session/child")]
    [InlineData("session.child")]
    public void CreateRejectsUnsafeSessionIds(string sessionId)
    {
        string root = Path.Combine(Path.GetTempPath(), "tx-mcp-session-tests", Guid.NewGuid().ToString("N"));
        var service = new DocumentSessionService(
            new PathResolver(Microsoft.Extensions.Options.Options.Create(
                new McpServerOptions { BasePath = root })));

        Assert.Throws<ArgumentException>(() => service.Create(sessionId));
    }

    [Theory]
    [InlineData("session-1")]
    [InlineData("session_2")]
    [InlineData("ABC123")]
    public void CreateAcceptsPortableSessionIds(string sessionId)
    {
        string root = Path.Combine(Path.GetTempPath(), "tx-mcp-session-tests", Guid.NewGuid().ToString("N"));
        var service = new DocumentSessionService(
            new PathResolver(Microsoft.Extensions.Options.Options.Create(
                new McpServerOptions { BasePath = root })));

        Assert.Equal(sessionId, service.Create(sessionId).SessionId);
    }
}
