using System.Collections.Generic;

namespace TxTextControl.McpServer.Models.Responses;

/// <summary>Lossless, bounded blocks for host-controlled ingestion; locators are not editor offsets.</summary>
public sealed record KnowledgeExtractionBlock(string Text, string Locator, string? Heading = null, string? TableHeader = null);
public sealed record KnowledgeDocumentExtractionResponse(string Version, IReadOnlyList<KnowledgeExtractionBlock> Blocks);
public sealed record KnowledgeExtractionResponse(IReadOnlyList<KnowledgeExtractionBlock> Blocks, int? NextBlock);
