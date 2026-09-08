namespace TxTextControl.McpServer.Models.Responses;

public sealed class DocumentExportResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string ExportId { get; set; } = string.Empty;
    public string Format { get; set; } = "pdf";
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public long ByteCount { get; set; }
    public string DownloadUri { get; set; } = string.Empty;
}

public sealed class DocumentExportFile
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
}
