using System;
using System.IO;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Tools;

internal static class ToolErrorMapper
{
    public static ToolErrorResponse Map(Exception ex)
        => ex switch
        {
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
}
