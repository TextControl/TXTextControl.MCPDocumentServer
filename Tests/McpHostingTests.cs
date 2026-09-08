using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Workers;
using Xunit;

namespace TxTextControl.McpServer.Tests;

public sealed class McpHostingTests
{
    [Fact]
    public async Task OrdinaryHostArgumentsDoNotEnterWorkerMode()
    {
        Assert.Null(await TextControlMcpWorker.RunIfRequestedAsync(["--urls", "http://127.0.0.1:0"]));
    }

    [Theory]
    [InlineData(true, typeof(WorkerPooledDocumentEngine))]
    [InlineData(false, typeof(ServerTextControlDocumentEngine))]
    public void RegistrationSelectsEngineWithoutStartingWorkers(bool pooled, Type engineType)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentWorkerPool:Enabled"] = pooled.ToString()
        });
        builder.Services.AddTextControlMcpServer(builder.Configuration);
        Assert.Equal(engineType, Assert.Single(builder.Services,
            descriptor => descriptor.ServiceType == typeof(ITxDocumentEngine)).ImplementationType);
        Assert.Equal(typeof(McpServerExtensions).Assembly, engineType.Assembly);
    }

    [Fact]
    public async Task AuthorizationAppliesToTransportAndDownloads()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTextControlMcpServer(builder.Configuration);
        await using var app = builder.Build();
        app.MapTextControlMcp().RequireAuthorization("Documents");
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().ToArray();
        Assert.Contains(routes, endpoint => endpoint.RoutePattern.RawText!.Contains("/mcp"));
        var export = Assert.Single(routes, endpoint => endpoint.RoutePattern.RawText!.Contains("/exports/"));
        Assert.Contains(export.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => policy.Policy == "Documents");
        Assert.All(routes.Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/mcp")),
            endpoint => Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                policy => policy.Policy == "Documents"));
    }

    [Fact]
    public async Task LibraryHostDiscoversToolsAndMapsDownloadsWithoutAdminUi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentWorkerPool:Enabled"] = "false",
            ["McpServer:BasePath"] = Path.Combine(Path.GetTempPath(), "tx-mcp-host-tests", Guid.NewGuid().ToString("N"))
        });
        builder.Services.AddTextControlMcpServer(builder.Configuration);
        await using var app = builder.Build();
        app.MapTextControlMcp();
        await app.StartAsync();
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses.Addresses)),
                Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
            using var response = await client.PostAsJsonAsync("/mcp",
                new { jsonrpc = "2.0", id = 1, method = "tools/list" });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var json = body.StartsWith("data:", StringComparison.Ordinal) || body.Contains("\ndata:")
                ? body.Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim()
                : body;
            using var document = JsonDocument.Parse(json);
            var tools = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()).ToArray();
            Assert.Contains("create_document_from_markdown", tools);
            Assert.Contains("convert_document", tools);
            Assert.Contains("set_document_style", tools);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/exports/missing/missing")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/exports/missing/{Guid.NewGuid():N}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/styles")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}
