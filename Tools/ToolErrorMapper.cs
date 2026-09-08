using System;
using System.IO;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services.Workers;

namespace TxTextControl.McpServer.Tools;

internal static class ToolErrorMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static CallToolResult Map(Exception ex)
    {
        ToolErrorResponse error = ex switch
        {
            DocumentWorkerCommandException workerError => new ToolErrorResponse
            {
                Code = workerError.Code,
                Message = workerError.Message
            },
            System.ComponentModel.LicenseException => new ToolErrorResponse
            {
                Code = "license_error",
                Message = "TX Text Control license is missing or invalid."
            },
            ArgumentException => new ToolErrorResponse
            {
                Code = "invalid_argument",
                Message = ex.Message
            },
            FileNotFoundException => new ToolErrorResponse
            {
                Code = "not_found",
                Message = ex.Message
            },
            InvalidOperationException => new ToolErrorResponse
            {
                Code = "invalid_operation",
                Message = ex.Message
            },
            _ => new ToolErrorResponse
            {
                Code = "internal_error",
                Message = ex.Message
            }
        };

        JsonElement structured = JsonSerializer.SerializeToElement(error, JsonOptions);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = structured,
            Content =
            [
                new TextContentBlock
                {
                    Text = structured.GetRawText()
                }
            ]
        };
    }
}
