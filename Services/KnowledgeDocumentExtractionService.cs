using System.Globalization;
using System.Text;
using TXTextControl;
using TxTextControl.McpServer.Models.Responses;

namespace TxTextControl.McpServer.Services;

/// <summary>Extracts uploaded reference documents on the MCP host using an isolated, short-lived engine.</summary>
/// <remarks>No persistent document/editor session or LLM is involved.</remarks>
public sealed class KnowledgeDocumentExtractionService
{
    // Bound native import concurrency across stores. A control is created, used and disposed
    // on the same worker thread; cancellation never disposes a control during a native call.
    private static readonly SemaphoreSlim NativeGate = new(1, 1);
    private readonly int maximumCharacters;

    public KnowledgeDocumentExtractionService(int maximumCharacters = 2_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        this.maximumCharacters = maximumCharacters;
    }

    public const string Version = "mcp-tx34-structure-v1";

    public async Task<IReadOnlyList<KnowledgeExtractionBlock>> ExtractAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();
        string format = Path.GetExtension(fileName).ToLowerInvariant();
        if (format is not (".tx" or ".rtf" or ".docx" or ".pdf" or ".html" or ".htm"))
            throw new NotSupportedException("Unsupported reference format.");
        await NativeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ExtractNative(format, content, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally { NativeGate.Release(); }
    }

    private KnowledgeExtractionBlock[] ExtractNative(string format, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        // The web parent can extract before any worker/document engine has been created.
        // Resolve the SDK license from the MCP core assembly, just as the normal engine does.
        TxTextControlLicensing.Configure();
        using var tx = new ServerTextControl();
        tx.Create();
        // Search-oriented PDF import includes text without reconstructing page layout.
        // Do not fetch linked images or import embedded PDF attachments.
        var settings = new LoadSettings { LoadImages = false, PDFImportSettings = PDFImportSettings.GenerateLines };
        byte[] bytes = content.ToArray();
        switch (format)
        {
            case ".tx": tx.Load(bytes, BinaryStreamType.InternalUnicodeFormat, settings); break;
            case ".docx": tx.Load(bytes, BinaryStreamType.WordprocessingML, settings); break;
            case ".pdf": tx.Load(bytes, BinaryStreamType.AdobePDF, settings); break;
            case ".rtf": tx.Load(Encoding.Latin1.GetString(bytes), StringStreamType.RichTextFormat, settings); break;
            default: tx.Load(new UTF8Encoding(false, true).GetString(bytes), StringStreamType.HTMLFormat, settings); break;
        }
        ct.ThrowIfCancellationRequested();
        List<(int Position, KnowledgeExtractionBlock Block)> blocks = [];
        List<(int Start, int End)> cells = [];
        long characters = 0;
        void Add(int position, KnowledgeExtractionBlock block)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(block.Text)) return;
            characters += block.Text.Length + (block.Heading?.Length ?? 0) + (block.TableHeader?.Length ?? 0);
            if (characters > maximumCharacters || blocks.Count >= 100_000)
                throw new InvalidOperationException("Reference extraction limit exceeded.");
            blocks.Add((position, block));
        }

        int tableNumber = 0;
        foreach (Table table in tx.Tables)
        {
            tableNumber++;
            string? header = null;
            for (int row = 1; row <= table.Rows.Count; row++)
            {
                ct.ThrowIfCancellationRequested();
                List<string> values = [];
                int start = int.MaxValue;
                for (int column = 1; column <= table.Columns.Count; column++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (cells.Count >= 100_000) throw new InvalidOperationException("Reference cell limit exceeded.");
                    var cell = table.Cells.GetItem(row, column);
                    start = Math.Min(start, cell.Start);
                    cells.Add((cell.Start, cell.Start + cell.Length));
                    values.Add(cell.Text.TrimEnd('\r', '\n'));
                }
                string text = string.Join(" | ", values);
                if (row == 1) header = text;
                Add(start, new(text, FormattableString.Invariant($"Table {tableNumber}, row {row}"), TableHeader: row == 1 ? null : header));
            }
        }
        string? heading = null;
        // Binary-search sorted ranges, including overlapping merged cells. Avoid scanning
        // every table cell for each paragraph in large, table-heavy references.
        cells.Sort((left, right) => left.Start.CompareTo(right.Start));
        int[] starts = cells.Select(cell => cell.Start).ToArray();
        int[] ends = new int[cells.Count];
        for (int i = 0; i < cells.Count; i++) ends[i] = Math.Max(cells[i].End, i == 0 ? 0 : ends[i - 1]);
        int paragraphNumber = 0;
        foreach (Paragraph paragraph in tx.Paragraphs)
        {
            paragraphNumber++;
            ct.ThrowIfCancellationRequested();
            int low = 0, high = starts.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (starts[middle] <= paragraph.Start) low = middle + 1; else high = middle;
            }
            if (low > 0 && paragraph.Start < ends[low - 1]) continue;
            string text = paragraph.Text.TrimEnd('\r', '\n');
            if (paragraph.FormattingStyle?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true)
                heading = text;
            Add(paragraph.Start, new(text, (format == ".pdf" ? "PDF text line " : "Paragraph ") + paragraphNumber.ToString(CultureInfo.InvariantCulture), heading));
        }
        if (blocks.Count == 0) throw new NotSupportedException("No readable text. Scanned PDFs require local OCR.");
        return blocks.OrderBy(b => b.Position).Select(b => b.Block).ToArray();
    }
}
