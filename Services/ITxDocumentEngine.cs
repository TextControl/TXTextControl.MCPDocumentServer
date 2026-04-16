using System;
using System.Collections.Generic;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Simplified document engine interface for simplified implementation.
/// </summary>
public interface ITxDocumentEngine
{
    DocumentState CreateEmpty(string workingDocumentPath);
    DocumentState LoadFromBase64(string base64Document, string workingDocumentPath);
    string GetAsBase64(string workingDocumentPath, string format);
    DocumentState FormatText(string workingDocumentPath, FormatTextRequest request);
    IReadOnlyList<string> GetParagraphs(string workingDocumentPath, int? start = null, int? end = null);
    IReadOnlyList<int> SearchText(string workingDocumentPath, string text = "", bool matchCase = false, bool wholeWord = false);
    IReadOnlyList<SearchTextRange> SearchTextRanges(string workingDocumentPath, string text = "", bool matchCase = false, bool wholeWord = false);
    string GetText(string workingDocumentPath);
}
