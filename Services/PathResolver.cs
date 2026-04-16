using System;
using System.IO;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Services;

public sealed class PathResolver
{
    private readonly string _basePath;

    public PathResolver(IOptions<McpServerOptions> options)
    {
        _basePath = Path.GetFullPath(options.Value.BasePath);
    }

    public string GetBasePath()
    {
        Directory.CreateDirectory(_basePath);
        return _basePath;
    }

    public string GetTemplatesRoot()
    {
        var path = Path.Combine(GetBasePath(), "templates");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetDataRoot()
    {
        var path = Path.Combine(GetBasePath(), "data");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetOutputRoot()
    {
        var path = Path.Combine(GetBasePath(), "output");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetSessionsRoot()
    {
        var path = Path.Combine(GetBasePath(), "sessions");
        Directory.CreateDirectory(path);
        return path;
    }
}
