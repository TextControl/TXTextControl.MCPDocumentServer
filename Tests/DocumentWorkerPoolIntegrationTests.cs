using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Workers;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Models.DocumentModel;
using Xunit;

namespace TxTextControl.McpServer.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DocumentWorkerPoolCollection
{
    public const string Name = "DocumentWorkerPool";
}

[Collection(DocumentWorkerPoolCollection.Name)]
public sealed class DocumentWorkerPoolIntegrationTests
{
    [Fact]
    public async Task Pool_PreservesOrderRecoversAfterCrashAndLeavesNoWorkers()
    {
        string root = Path.Combine(Path.GetTempPath(), "tx-mcp-pool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configured = new DocumentWorkerPoolOptions
        {
            Enabled = true,
            WorkerCount = 2,
            QueueCapacity = 16,
            StartupTimeoutSeconds = 30,
            CommandTimeoutSeconds = 30,
            SessionAffinityIdleSeconds = 2,
            MaximumHotSessions = 2,
            WorkerRestartLimit = 3,
            WorkerRestartBackoffMilliseconds = 10,
            WorkerExecutablePath = Path.Combine(AppContext.BaseDirectory, "TxTextControl.McpServer.dll"),
        };
        var resolver = new PathResolver(Microsoft.Extensions.Options.Options.Create(new McpServerOptions { BasePath = root }));
        var environment = new TestHostEnvironment { ContentRootPath = AppContext.BaseDirectory };
        var automation = new AutomationSettingsService(new DocumentAutomationOptions(),
            Path.Combine(root, "appsettings.json"), [], []);
        void SaveTitleColor(string color) => automation.SaveStylePresets("Body", new StyleRoleDefinition(),
            [new TextStyleDefinition { Name = "Title", ColorHex = color, FontSize = 30 },
             new TextStyleDefinition { Name = "Body", FontSize = 11 }], []);
        SaveTitleColor("#FF0000");
        await using var pool = new DocumentWorkerPool(
            Microsoft.Extensions.Options.Options.Create(configured),
            environment,
            resolver,
            NullLogger<DocumentWorkerPool>.Instance,
            automation);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        int[] processIds = [];
        try
        {
            await pool.StartAsync(timeout.Token);
            var engine = new WorkerPooledDocumentEngine(pool, Microsoft.Extensions.Options.Options.Create(configured));
            // Saving presets must affect an already-running worker's next creation.
            int[] originalWorkers = pool.GetStatus().Workers.Select(worker => worker.ProcessId).ToArray();
            var presetSessions = new DocumentSessionService(resolver);
            string firstPath = presetSessions.Create("preset-before").WorkingDocumentPath;
            engine.LoadMarkdownWithPresetStyles("# Preset test\n\nBody text.", firstPath);
            Assert.Equal("#FF0000", engine.GetDocumentStyleSnapshots(firstPath)
                .Single(style => style.Name == "Title").Text!.ColorHex, ignoreCase: true);
            SaveTitleColor("#008000");
            string secondPath = presetSessions.Create("preset-after").WorkingDocumentPath;
            engine.LoadMarkdownWithPresetStyles("# Preset test\n\nBody text.", secondPath);
            Assert.Equal("#008000", engine.GetDocumentStyleSnapshots(secondPath)
                .Single(style => style.Name == "Title").Text!.ColorHex, ignoreCase: true);
            Assert.Equal("#FF0000", engine.GetDocumentStyleSnapshots(firstPath)
                .Single(style => style.Name == "Title").Text!.ColorHex, ignoreCase: true);
            Assert.Equal(originalWorkers, pool.GetStatus().Workers.Select(worker => worker.ProcessId).ToArray());
            // The existing ordering test below requires enabled operation handlers.
            // Null lists mean all known operations in the worker.
            string sessionId = "ordered-session";
            var sessionStore = new DocumentSessionService(resolver);
            DocumentSession session = sessionStore.Create(sessionId);
            string directory = session.WorkingDirectory;
            string workingPath = session.WorkingDocumentPath;
            DocumentState state = engine.CreateEmpty(workingPath);

            var commands = new List<Task<DocumentState>>();
            for (int index = 0; index < 10; index++)
            {
                var request = new ApplyOperationsRequest
                {
                    Operations =
                    [
                        new DocumentOperation { Type = "append_paragraph", Text = $"ordered-{index}" },
                    ],
                };
                commands.Add(pool.ExecuteAsync<DocumentState>(
                    "apply_operations",
                    new { workingDocumentPath = workingPath, state, request },
                    sessionId,
                    mutation: true,
                    timeout.Token));
            }

            await Task.WhenAll(commands);
            string text = engine.GetText(workingPath);
            int previous = -1;
            for (int index = 0; index < 10; index++)
            {
                int current = text.IndexOf($"ordered-{index}", StringComparison.Ordinal);
                Assert.True(current > previous, $"Paragraph ordered-{index} was not applied in submission order.");
                previous = current;
            }

            DocumentWorkerPoolStatus beforeCrash = pool.GetStatus();
            processIds = beforeCrash.Workers.Select(worker => worker.ProcessId).ToArray();
            int affinityWorkerPid = beforeCrash.Workers
                .Single(worker => worker.HotSessionKey == sessionId)
                .ProcessId;
            using (Process worker = Process.GetProcessById(affinityWorkerPid))
            {
                worker.Kill(entireProcessTree: true);
                worker.WaitForExit(10_000);
            }

            string recoveredText = engine.GetText(workingPath);
            Assert.Equal(text, recoveredText);
            Assert.Contains(pool.GetStatus().Workers, worker => worker.RestartCount > 0 && worker.Healthy);
            Assert.True(File.Exists(Path.Combine(directory, DocumentWorkerProcess.CommitFileName)));

            var restartedSessionStore = new DocumentSessionService(resolver);
            DocumentState recoveredState = restartedSessionStore.LoadState(restartedSessionStore.Get(sessionId));
            Assert.Equal(11, recoveredState.Revision);
            Assert.Equal(workingPath, recoveredState.WorkingDocumentPath);

            await Task.Delay(TimeSpan.FromSeconds(3), timeout.Token);
            Assert.DoesNotContain(pool.GetStatus().Workers, worker => worker.HotSessionKey == sessionId);
        }
        finally
        {
            processIds = processIds
                .Concat(pool.GetStatus().Workers.Select(worker => worker.ProcessId))
                .Where(processId => processId > 0)
                .Distinct()
                .ToArray();
            await pool.StopAsync(CancellationToken.None);
            foreach (int processId in processIds)
            {
                Assert.False(IsRunning(processId), $"Worker process {processId} remained after shutdown.");
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TxTextControl.McpServer.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
