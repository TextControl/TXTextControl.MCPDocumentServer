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
    DocumentState LoadFromBase64(
        string base64Document,
        string workingDocumentPath,
        DocumentState? existingState = null,
        string? sourceFormat = null);
    DocumentPresetStyleEngineResult LoadMarkdownWithPresetStyles(
        string markdown,
        string workingDocumentPath);
    DocumentPresetStyleEngineResult ApplyPresetStyles(
        string workingDocumentPath,
        DocumentState state);
    DocumentState ConvertFromBase64ToFile(
        string base64Document,
        string? sourceFormat,
        string workingDocumentPath,
        string outputPath,
        string outputFormat);
    string GetAsBase64(string workingDocumentPath, string format);
    void ExportToFile(string workingDocumentPath, string outputPath, string format);
    DocumentState ApplyOperations(string workingDocumentPath, DocumentState state, ApplyOperationsRequest request);
    DocumentState FormatText(string workingDocumentPath, FormatTextRequest request);
    IReadOnlyList<string> GetParagraphs(string workingDocumentPath, int? start = null, int? end = null);
    IReadOnlyList<int> SearchText(string workingDocumentPath, string text = "", bool matchCase = false, bool wholeWord = false);
    IReadOnlyList<SearchTextRange> SearchTextRanges(string workingDocumentPath, string text = "", bool matchCase = false, bool wholeWord = false);
    string GetText(string workingDocumentPath);
    DocumentEditEngineResult EditDocument(string workingDocumentPath, EditDocumentRequest request);
    DocumentContentSnapshot GetContentSnapshot(string workingDocumentPath);
    IReadOnlyList<StyleInspection> GetDocumentStyleSnapshots(string workingDocumentPath);
    IReadOnlyList<TemplateMergeFieldInfo> GetTemplateMergeFields(string workingDocumentPath);
    IReadOnlyList<TemplateMergeBlockInfo> GetTemplateMergeBlocks(string workingDocumentPath);
    IReadOnlyList<TemplateFormFieldInfo> GetTemplateFormFields(string workingDocumentPath);
    TemplateContentSnapshot GetTemplateContentSnapshot(string workingDocumentPath);
    MergeTemplateEngineResult MergeTemplate(
        string workingDocumentPath,
        DocumentState state,
        MergeTemplateRequest request);
}
