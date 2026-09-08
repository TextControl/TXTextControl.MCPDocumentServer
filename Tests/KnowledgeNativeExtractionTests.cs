using System.Text;
using TXTextControl;
using TxTextControl.McpServer.Services;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Tests;

[Collection("Knowledge native license isolation")]
public sealed class KnowledgeNativeExtractionTests
{
    [Fact]
    public async Task ExtractorInitializesLicensingWithoutAnotherEngineOrTestFixture()
    {
        // Emulate the web parent process: its entry assembly does not carry the SDK license.
        ServerTextControl.EntryAssembly = typeof(KnowledgeNativeExtractionTests).Assembly;
        try
        {
            var blocks = await new KnowledgeDocumentExtractionService().ExtractAsync("check.rtf",
                Encoding.ASCII.GetBytes(@"{\rtf1\ansi Standalone extraction works.}"), CancellationToken.None);
            Assert.Contains(blocks, block => block.Text.Contains("Standalone extraction works.", StringComparison.Ordinal));
        }
        finally { TxTextControlLicensing.Configure(); }
    }
    [Fact]
    public async Task BinaryEndpointExtractsPdfAndRejectsMissingHeaderOrBrokenInput()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost("/mcp/knowledge/extract", KnowledgeExtractionEndpoint.ExtractAsync);
        await app.StartAsync();
        try
        {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            byte[] source = CreateDocument("pdf");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/mcp/knowledge/extract?format=pdf", new ByteArrayContent(source))).StatusCode);
            client.DefaultRequestHeaders.Add("X-TextControl-Knowledge", "1");
            var response = await client.PostAsync("/mcp/knowledge/extract?format=pdf", new ByteArrayContent(source));
            response.EnsureSuccessStatusCode();
            var extracted = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentExtractionResponse>())!;
            Assert.Equal(KnowledgeDocumentExtractionService.Version, extracted.Version);
            Assert.Contains(extracted.Blocks, block => block.Text.Contains("Riverbend", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/mcp/knowledge/extract?format=pdf", new ByteArrayContent([1, 2, 3]))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/mcp/knowledge/extract?format=exe", new ByteArrayContent(source))).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
    private static byte[] CreateDocument(string format)
    {
        // The test runner's executable is not the SDK-consuming assembly.
        ServerTextControl.EntryAssembly = typeof(KnowledgeDocumentExtractionService).Assembly;
        using var tx = new ServerTextControl();
        tx.Create();
        tx.Load("<html><body><h1>Approved policy</h1><p>Riverbend payment is due in thirty days. Office: München.</p><table><tr><td>Item</td><td>Amount</td></tr><tr><td>Consulting</td><td>4200 EUR</td></tr></table><p>Final confidentiality clause.</p></body></html>", StringStreamType.HTMLFormat);
        if (format is "rtf" or "html")
        {
            tx.Save(out string text, format == "rtf" ? StringStreamType.RichTextFormat : StringStreamType.HTMLFormat);
            return Encoding.UTF8.GetBytes(text);
        }
        tx.Save(out byte[] bytes, format switch { "pdf" => BinaryStreamType.AdobePDF, "docx" => BinaryStreamType.WordprocessingML, _ => BinaryStreamType.InternalUnicodeFormat });
        return bytes;
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    [InlineData("rtf")]
    [InlineData("tx")]
    [InlineData("html")]
    public async Task NativeFormatsExtractOnMcpHostAndPreserveSource(string format)
    {
        byte[] source = CreateDocument(format), original = source.ToArray();
        var blocks = await new KnowledgeDocumentExtractionService().ExtractAsync("policy." + format, source, CancellationToken.None);
        string text = string.Join("\n", blocks.Select(b => b.Text));
        Assert.Contains("Riverbend", text);
        Assert.Contains("München", text);
        Assert.Contains("4200", text);
        Assert.Contains("Final confidentiality", text);
        Assert.Equal(original, source);
        if (format != "pdf")
        {
            var row = Assert.Single(blocks, b => b.Locator.StartsWith("Table 1, row 2", StringComparison.Ordinal));
            Assert.Contains("Item", row.TableHeader);
            Assert.Contains("Consulting", row.Text);
            Assert.Single(blocks, b => b.Text.Contains("Consulting", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task MultiPagePdfAndSeparateControlRemainIndependent()
    {
        ServerTextControl.EntryAssembly = typeof(KnowledgeDocumentExtractionService).Assembly;
        using var editor = new ServerTextControl(); editor.Create();
        editor.Text = "Unchanged working document";
        using var reference = new ServerTextControl(); reference.Create();
        reference.Text = string.Join("\r\n", Enumerable.Range(1, 200).Select(i => "Reference policy line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        reference.Save(out byte[] pdf, BinaryStreamType.AdobePDF);
        var blocks = await new KnowledgeDocumentExtractionService().ExtractAsync("many-pages.pdf", pdf, CancellationToken.None);
        Assert.Contains(blocks, b => b.Text.Contains("line 200", StringComparison.Ordinal));
        Assert.DoesNotContain(blocks, b => b.Text.Contains("Unchanged working", StringComparison.Ordinal));
        Assert.Equal("Unchanged working document", editor.Text);
        using var blank = new ServerTextControl(); blank.Create();
        blank.Save(out byte[] empty, BinaryStreamType.AdobePDF);
        await Assert.ThrowsAsync<NotSupportedException>(() => new KnowledgeDocumentExtractionService().ExtractAsync("empty.pdf", empty, CancellationToken.None));
    }

    [Fact]
    public async Task LimitsAndCancellationAreEnforced()
    {
        byte[] source = CreateDocument("docx");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new KnowledgeDocumentExtractionService(10).ExtractAsync("policy.docx", source, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KnowledgeDocumentExtractionService().ExtractAsync("policy.pdf", source, new CancellationToken(true)));
        await Assert.ThrowsAsync<NotSupportedException>(() => new KnowledgeDocumentExtractionService().ExtractAsync("policy.exe", source, CancellationToken.None));
    }
}

[CollectionDefinition("Knowledge native license isolation", DisableParallelization = true)]
public sealed class KnowledgeNativeLicenseIsolationCollection;
