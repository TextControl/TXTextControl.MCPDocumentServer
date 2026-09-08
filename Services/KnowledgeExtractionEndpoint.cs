using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services;

/// <summary>Binary, host-controlled ingestion endpoint; not a model-facing tool.</summary>
public static class KnowledgeExtractionEndpoint
{
    public const int MaximumFileBytes = 32 * 1024 * 1024;
    public static async Task<IResult> ExtractAsync(HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (context.Request.Headers["X-TextControl-Knowledge"] != "1") return Results.BadRequest(new { error = "Missing knowledge extraction header." });
        string format = context.Request.Query["format"].ToString().ToLowerInvariant();
        if (format is not ("pdf" or "docx" or "rtf" or "tx" or "html" or "htm")) return Results.BadRequest(new { error = "Unsupported extraction format." });
        if (context.Request.ContentLength > MaximumFileBytes) return Results.StatusCode(413);
        using var input = new MemoryStream();
        byte[] buffer = new byte[81920]; int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (input.Length + count > MaximumFileBytes) return Results.StatusCode(413);
            await input.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (input.Length == 0) return Results.BadRequest(new { error = "The document is empty." });
        try
        {
            var blocks = await new KnowledgeDocumentExtractionService().ExtractAsync("reference." + format, input.ToArray(), ct);
            return Results.Ok(new KnowledgeDocumentExtractionResponse(KnowledgeDocumentExtractionService.Version, blocks));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (System.ComponentModel.LicenseException)
        {
            return Results.Json(new { error = "MCP document-engine licensing initialization failed. Check the MCP host's licensed engine deployment." }, statusCode: 503);
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            return Results.Json(new { error = "MCP document engine is unavailable. Check native dependencies and platform compatibility on the MCP host." }, statusCode: 503);
        }
        catch (Exception)
        {
            // No source text, native paths or licensing details are returned to callers.
            return Results.UnprocessableEntity(new { error = "Document extraction failed. Check format, size, document-engine availability or OCR requirements." });
        }
    }
}
