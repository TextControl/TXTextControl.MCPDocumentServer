using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services.Admin;

/// <summary>Reads and persists the MCP server settings presented by the admin UI.</summary>
public sealed class ServerSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly McpServerOptions server;
    private readonly DocumentWorkerPoolOptions workers;
    private readonly DocumentAutomationOptions automation;
    private readonly AdminOptions admin;
    private readonly string settingsPath;
    private readonly object gate = new();
    private string allowedHosts;
    private string defaultLogLevel;
    private string aspNetLogLevel;

    public ServerSettingsService(
        IOptions<McpServerOptions> server,
        IOptions<DocumentWorkerPoolOptions> workers,
        IOptions<DocumentAutomationOptions> automation,
        IOptions<AdminOptions> admin,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        this.server = server.Value;
        this.workers = workers.Value;
        this.automation = automation.Value;
        this.admin = admin.Value;
        allowedHosts = configuration["AllowedHosts"] ?? "*";
        defaultLogLevel = configuration["Logging:LogLevel:Default"] ?? "Information";
        aspNetLogLevel = configuration["Logging:LogLevel:Microsoft.AspNetCore"] ?? "Warning";
        settingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");
    }

    public ServerSettingsSnapshot Get()
    {
        PageLayoutDefinition layout = automation.DefaultPageLayout ?? new PageLayoutDefinition
        {
            PageSize = "Letter",
            Orientation = "portrait",
            Unit = "in",
            MarginLeft = 0.8f,
            MarginRight = 0.8f,
            MarginTop = 0.75f,
            MarginBottom = 0.75f,
        };
        return new ServerSettingsSnapshot
        {
            ServerName = server.Name,
            BasePath = server.BasePath,
            SessionMaxAgeHours = server.SessionMaxAgeHours,
            AllowedHosts = allowedHosts,
            WorkerPoolEnabled = workers.Enabled,
            WorkerCount = workers.WorkerCount,
            InteractiveWorkerCount = workers.InteractiveWorkerCount,
            QueueCapacity = workers.QueueCapacity,
            StartupTimeoutSeconds = workers.StartupTimeoutSeconds,
            CommandTimeoutSeconds = workers.CommandTimeoutSeconds,
            SessionAffinityIdleSeconds = workers.SessionAffinityIdleSeconds,
            MaximumHotSessions = workers.MaximumHotSessions,
            WorkerRestartLimit = workers.WorkerRestartLimit,
            WorkerRestartBackoffMilliseconds = workers.WorkerRestartBackoffMilliseconds,
            MaximumInputMegabytes = workers.MaximumInputMegabytes,
            MaximumOutputMegabytes = workers.MaximumOutputMegabytes,
            WorkerExecutablePath = workers.WorkerExecutablePath,
            PageSize = layout.PageSize ?? "Letter",
            Orientation = layout.Orientation ?? "portrait",
            PageUnit = layout.Unit ?? "in",
            MarginLeft = layout.MarginLeft,
            MarginRight = layout.MarginRight,
            MarginTop = layout.MarginTop,
            MarginBottom = layout.MarginBottom,
            AdminUsername = admin.Username,
            DefaultLogLevel = defaultLogLevel,
            AspNetLogLevel = aspNetLogLevel,
        };
    }

    public void Save(ServerSettingsSnapshot value, string? newAdminPassword)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ServerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.BasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.AllowedHosts);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.AdminUsername);
        ArgumentOutOfRangeException.ThrowIfLessThan(value.SessionMaxAgeHours, 1);

        var workerSettings = new DocumentWorkerPoolOptions
        {
            Enabled = value.WorkerPoolEnabled,
            WorkerCount = value.WorkerCount,
            InteractiveWorkerCount = value.InteractiveWorkerCount,
            QueueCapacity = value.QueueCapacity,
            StartupTimeoutSeconds = value.StartupTimeoutSeconds,
            CommandTimeoutSeconds = value.CommandTimeoutSeconds,
            SessionAffinityIdleSeconds = value.SessionAffinityIdleSeconds,
            MaximumHotSessions = value.MaximumHotSessions,
            WorkerRestartLimit = value.WorkerRestartLimit,
            WorkerRestartBackoffMilliseconds = value.WorkerRestartBackoffMilliseconds,
            MaximumInputMegabytes = value.MaximumInputMegabytes,
            MaximumOutputMegabytes = value.MaximumOutputMegabytes,
            WorkerExecutablePath = string.IsNullOrWhiteSpace(value.WorkerExecutablePath)
                ? null
                : value.WorkerExecutablePath.Trim(),
        };
        workerSettings.Validate();

        var pageLayout = new PageLayoutDefinition
        {
            PageSize = string.IsNullOrWhiteSpace(value.PageSize) ? "Letter" : value.PageSize.Trim(),
            Orientation = string.IsNullOrWhiteSpace(value.Orientation) ? "portrait" : value.Orientation.Trim(),
            Unit = string.IsNullOrWhiteSpace(value.PageUnit) ? "in" : value.PageUnit.Trim(),
            MarginLeft = value.MarginLeft,
            MarginRight = value.MarginRight,
            MarginTop = value.MarginTop,
            MarginBottom = value.MarginBottom,
        };

        lock (gate)
        {
            server.Name = value.ServerName.Trim();
            server.BasePath = value.BasePath.Trim();
            server.SessionMaxAgeHours = value.SessionMaxAgeHours;
            allowedHosts = value.AllowedHosts.Trim();
            defaultLogLevel = NormalizeLogLevel(value.DefaultLogLevel, "Information");
            aspNetLogLevel = NormalizeLogLevel(value.AspNetLogLevel, "Warning");
            Copy(workerSettings, workers);
            automation.DefaultPageLayout = pageLayout;
            admin.Username = value.AdminUsername.Trim();
            if (!string.IsNullOrWhiteSpace(newAdminPassword))
            {
                admin.Password = newAdminPassword;
            }

            JsonObject root = LoadSettings();
            root["AllowedHosts"] = allowedHosts;
            root[McpServerOptions.SectionName] = JsonSerializer.SerializeToNode(server, SerializerOptions);
            root[DocumentWorkerPoolOptions.SectionName] = JsonSerializer.SerializeToNode(workers, SerializerOptions);

            JsonObject automationNode = root[DocumentAutomationOptions.SectionName] as JsonObject ?? [];
            automationNode["DefaultPageLayout"] = JsonSerializer.SerializeToNode(pageLayout, SerializerOptions);
            root[DocumentAutomationOptions.SectionName] = automationNode;

            JsonObject adminNode = root[AdminOptions.SectionName] as JsonObject ?? [];
            adminNode["Username"] = admin.Username;
            adminNode["Password"] = admin.Password;
            root[AdminOptions.SectionName] = adminNode;

            JsonObject logging = root["Logging"] as JsonObject ?? [];
            JsonObject levels = logging["LogLevel"] as JsonObject ?? [];
            levels["Default"] = defaultLogLevel;
            levels["Microsoft.AspNetCore"] = aspNetLogLevel;
            logging["LogLevel"] = levels;
            root["Logging"] = logging;

            File.WriteAllText(settingsPath, root.ToJsonString(SerializerOptions) + Environment.NewLine);
        }
    }

    private JsonObject LoadSettings()
    {
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        string json = File.ReadAllText(settingsPath);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonNode.Parse(json) as JsonObject
              ?? throw new InvalidOperationException("appsettings.json must contain a JSON object.");
    }

    private static string NormalizeLogLevel(string? value, string fallback)
    {
        string[] levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];
        return levels.FirstOrDefault(level => level.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static void Copy(DocumentWorkerPoolOptions source, DocumentWorkerPoolOptions target)
    {
        target.Enabled = source.Enabled;
        target.WorkerCount = source.WorkerCount;
        target.InteractiveWorkerCount = source.InteractiveWorkerCount;
        target.QueueCapacity = source.QueueCapacity;
        target.StartupTimeoutSeconds = source.StartupTimeoutSeconds;
        target.CommandTimeoutSeconds = source.CommandTimeoutSeconds;
        target.SessionAffinityIdleSeconds = source.SessionAffinityIdleSeconds;
        target.MaximumHotSessions = source.MaximumHotSessions;
        target.WorkerRestartLimit = source.WorkerRestartLimit;
        target.WorkerRestartBackoffMilliseconds = source.WorkerRestartBackoffMilliseconds;
        target.MaximumInputMegabytes = source.MaximumInputMegabytes;
        target.MaximumOutputMegabytes = source.MaximumOutputMegabytes;
        target.WorkerExecutablePath = source.WorkerExecutablePath;
    }
}

public sealed class ServerSettingsSnapshot
{
    public string ServerName { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public int SessionMaxAgeHours { get; set; }
    public string AllowedHosts { get; set; } = string.Empty;
    public bool WorkerPoolEnabled { get; set; }
    public int WorkerCount { get; set; }
    public int InteractiveWorkerCount { get; set; }
    public int QueueCapacity { get; set; }
    public int StartupTimeoutSeconds { get; set; }
    public int CommandTimeoutSeconds { get; set; }
    public int SessionAffinityIdleSeconds { get; set; }
    public int MaximumHotSessions { get; set; }
    public int WorkerRestartLimit { get; set; }
    public int WorkerRestartBackoffMilliseconds { get; set; }
    public int MaximumInputMegabytes { get; set; }
    public int MaximumOutputMegabytes { get; set; }
    public string? WorkerExecutablePath { get; set; }
    public string PageSize { get; set; } = "Letter";
    public string Orientation { get; set; } = "portrait";
    public string PageUnit { get; set; } = "in";
    public float? MarginLeft { get; set; }
    public float? MarginRight { get; set; }
    public float? MarginTop { get; set; }
    public float? MarginBottom { get; set; }
    public string AdminUsername { get; set; } = string.Empty;
    public string DefaultLogLevel { get; set; } = "Information";
    public string AspNetLogLevel { get; set; } = "Warning";
}
