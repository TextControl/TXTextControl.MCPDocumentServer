using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services;

internal static class DocumentModelQualityNormalizer
{
    private static readonly HashSet<string> KnownSectionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Executive Summary", "Overview", "Details", "Background", "Objectives", "Benefits",
        "Next Steps", "Recommendations", "Payment Terms", "Terms and Conditions", "Notes"
    };

    private static readonly HashSet<string> InvoiceMetadataLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "From", "Seller", "Vendor", "To", "Bill To", "Customer", "Customer Name",
        "Invoice Number", "Invoice No", "Date", "Invoice Date", "Due Date"
    };

    private static readonly HashSet<string> InvoiceTotalLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Subtotal", "Tax", "Total", "Amount Due", "Balance Due"
    };

    public static void Normalize(
        DocumentModel.Document document,
        DocumentAutomationOptions? options,
        List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(warnings);

        RepairDelimitedTableRows(document, warnings);
        if (LooksLikeInvoice(document))
        {
            EnsureInvoiceTitle(document, options, warnings);
            ValidateAndCleanInvoiceTables(document, warnings);
            ApplyInvoiceProfile(document, options, warnings);
        }

        ApplySemanticParagraphRoles(document);
    }

    public static void ValidateNewDraftOperations(ApplyOperationsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.CreateIfMissing || !string.IsNullOrWhiteSpace(request.SessionId))
        {
            return;
        }

        List<string> paragraphTexts = request.Operations
            .Where(operation => string.Equals(
                operation.Type,
                BasicTextCapabilityPack.AppendParagraph,
                StringComparison.OrdinalIgnoreCase))
            .Select(GetOperationText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        bool hasTable = request.Operations.Any(operation => string.Equals(
            operation.Type,
            TableCapabilityPack.AppendTable,
            StringComparison.OrdinalIgnoreCase));

        if (HasStrongInvoiceSignals(paragraphTexts) && !hasTable)
        {
            throw new ArgumentException(
                "A complete new invoice cannot be created as paragraph-only apply_operations. "
                + "First call list_document_recipes and use the matching server-owned invoice recipe. "
                + "For a custom invoice, retry with create_document and include one real table whose rows contain separate cells "
                + "for Description, Quantity, Unit Price, and Amount. Do not export the failed draft.",
                nameof(request));
        }
    }

    private static void RepairDelimitedTableRows(
        DocumentModel.Document document,
        List<string> warnings)
    {
        int recoveredRows = 0;
        foreach (DocumentModel.Section section in document.Sections)
        {
            for (int blockIndex = 0; blockIndex < section.Blocks.Count; blockIndex++)
            {
                DocumentModel.Table? table = section.Blocks[blockIndex].Table;
                if (table is null || table.Rows.Count == 0)
                {
                    continue;
                }

                int columnCount = table.Rows.Max(row => row.Cells.Count);
                if (columnCount < 2)
                {
                    continue;
                }

                for (int rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
                {
                    DocumentModel.TableRow row = table.Rows[rowIndex];
                    if (row.Cells.Count == 1
                        && TryParseDelimitedRow(GetCellText(row.Cells[0]), columnCount, out string[] cells))
                    {
                        row.Cells = CreateCells(cells);
                        recoveredRows++;
                    }
                }

                while (blockIndex + 1 < section.Blocks.Count)
                {
                    DocumentModel.DocumentBlock candidate = section.Blocks[blockIndex + 1];
                    if (!string.Equals(candidate.Type, "paragraph", StringComparison.OrdinalIgnoreCase)
                        || !TryParseDelimitedRow(GetParagraphText(candidate.Paragraph), columnCount, out string[] cells))
                    {
                        break;
                    }

                    table.Rows.Add(new DocumentModel.TableRow
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Cells = CreateCells(cells)
                    });
                    section.Blocks.RemoveAt(blockIndex + 1);
                    recoveredRows++;
                }
            }
        }

        if (recoveredRows > 0)
        {
            warnings.Add($"Recovered {recoveredRows} pipe-delimited model row(s) as real table rows and applied the configured table defaults.");
        }
    }

    private static void ApplySemanticParagraphRoles(DocumentModel.Document document)
    {
        foreach (DocumentModel.Paragraph paragraph in document.Sections
                     .SelectMany(section => section.Blocks)
                     .Select(block => block.Paragraph)
                     .OfType<DocumentModel.Paragraph>())
        {
            if (!string.IsNullOrWhiteSpace(paragraph.Role)
                || !string.IsNullOrWhiteSpace(paragraph.StyleName))
            {
                continue;
            }

            string text = GetParagraphText(paragraph).Trim().TrimEnd(':').Trim();
            if (KnownSectionHeadings.Contains(text))
            {
                paragraph.Role = "heading2";
            }
        }
    }

    private static void ApplyInvoiceProfile(
        DocumentModel.Document document,
        DocumentAutomationOptions? options,
        List<string> warnings)
    {
        string accentColor = options?.StylePresets
            .FirstOrDefault(style => string.Equals(
                style.Name,
                options.StyleRoles?.Title,
                StringComparison.OrdinalIgnoreCase))
            ?.ColorHex ?? "#1F4E79";

        foreach (DocumentModel.Section section in document.Sections)
        {
            List<DocumentModel.DocumentBlock> normalizedBlocks = [];
            foreach (DocumentModel.DocumentBlock block in section.Blocks)
            {
                DocumentModel.Paragraph? paragraph = block.Paragraph;
                if (paragraph is null
                    || !TrySplitLabel(GetParagraphText(paragraph), out string label, out string value))
                {
                    normalizedBlocks.Add(block);
                    continue;
                }

                string normalizedLabel = NormalizeLabel(label);
                if (normalizedLabel.Equals("Payment Terms", StringComparison.OrdinalIgnoreCase))
                {
                    normalizedBlocks.Add(CreateParagraphBlock("Payment Terms", "heading2"));
                    normalizedBlocks.Add(CreateParagraphBlock(value, "body"));
                    continue;
                }

                if (InvoiceMetadataLabels.Contains(normalizedLabel))
                {
                    if (!HasInlineFormatting(paragraph))
                    {
                        SetLabeledRuns(paragraph, label, value, accentColor, emphasizeValue: false);
                    }

                    paragraph.Role ??= "body";
                    paragraph.ParagraphStyle ??= new DocumentModel.ParagraphStyleDefinition
                    {
                        SpaceAfter = 2,
                        Unit = "pt"
                    };
                }
                else if (InvoiceTotalLabels.Contains(normalizedLabel))
                {
                    bool isGrandTotal = normalizedLabel.Equals("Total", StringComparison.OrdinalIgnoreCase)
                        || normalizedLabel.Equals("Amount Due", StringComparison.OrdinalIgnoreCase)
                        || normalizedLabel.Equals("Balance Due", StringComparison.OrdinalIgnoreCase);
                    if (!HasInlineFormatting(paragraph))
                    {
                        SetLabeledRuns(paragraph, label, value, accentColor, emphasizeValue: isGrandTotal);
                    }

                    paragraph.Role ??= "body";
                    paragraph.Alignment ??= "right";
                    paragraph.ParagraphStyle ??= new DocumentModel.ParagraphStyleDefinition
                    {
                        Alignment = "right",
                        SpaceBefore = isGrandTotal ? 6 : 1,
                        SpaceAfter = isGrandTotal ? 8 : 1,
                        Unit = "pt"
                    };
                }

                normalizedBlocks.Add(block);
            }

            section.Blocks = normalizedBlocks;
        }

        foreach (DocumentModel.Table table in document.Sections
                     .SelectMany(section => section.Blocks)
                     .Select(block => block.Table)
                     .OfType<DocumentModel.Table>())
        {
            ApplyInvoiceTableProfile(table);
        }

        warnings.Add("Applied the server-owned professional invoice quality profile to presentation properties the model left unspecified.");
    }

    private static void ValidateAndCleanInvoiceTables(
        DocumentModel.Document document,
        List<string> warnings)
    {
        List<DocumentModel.Table> invoiceTables = document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => block.Table)
            .OfType<DocumentModel.Table>()
            .Where(IsInvoiceLineItemTable)
            .ToList();

        if (invoiceTables.Count == 0)
        {
            throw new ArgumentException(
                "A professional invoice requires one real line-item table. Add a table with a header row and separate cells for "
                + "Description, Quantity, Unit Price, and Amount; add each requested item as a complete row. "
                + "Do not represent line items as paragraphs.");
        }

        foreach (DocumentModel.Table table in invoiceTables)
        {
            int expectedColumns = table.Rows[0].Cells.Count;
            if (table.Rows.Count < 2)
            {
                throw new ArgumentException(
                    "The invoice line-item table has only a header. Add at least one complete line-item row before exporting.");
            }

            int removedRows = 0;
            while (table.Rows.Count > 1 && table.Rows[^1].Cells.All(cell => string.IsNullOrWhiteSpace(GetCellText(cell))))
            {
                table.Rows.RemoveAt(table.Rows.Count - 1);
                removedRows++;
            }

            if (removedRows > 0)
            {
                warnings.Add($"Removed {removedRows} empty trailing invoice table row(s).");
            }

            for (int rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
            {
                DocumentModel.TableRow row = table.Rows[rowIndex];
                string[] values = row.Cells.Select(cell => GetCellText(cell).Trim()).ToArray();
                if (values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                if (values.Length != expectedColumns
                    || values.Any(string.IsNullOrWhiteSpace)
                    || values.Skip(1).Any(StartsWithOrphanedSeparator))
                {
                    throw new ArgumentException(
                        $"Invoice table row {rowIndex} is malformed. Supply exactly {expectedColumns} non-empty cells matching the header. "
                        + "For a four-column table use: description, quantity, unit price, amount. Example: "
                        + "[\"Professional Services\", \"15\", \"$150.00\", \"$2,250.00\"].");
                }
            }
        }
    }

    private static bool IsInvoiceLineItemTable(DocumentModel.Table table)
    {
        if (table.Rows.Count == 0)
        {
            return false;
        }

        string[] headers = table.Rows[0].Cells.Select(cell => GetCellText(cell).Trim()).ToArray();
        return headers.Length switch
        {
            4 => IsHeader(headers[0], "description", "item")
                 && IsHeader(headers[1], "quantity", "qty")
                 && IsHeader(headers[2], "unit price", "price", "rate")
                 && IsHeader(headers[3], "amount", "line total", "total"),
            5 => IsHeader(headers[2], "quantity", "qty")
                 && IsHeader(headers[3], "unit price", "price", "rate")
                 && IsHeader(headers[4], "amount", "line total", "total"),
            _ => false
        };
    }

    private static bool StartsWithOrphanedSeparator(string value)
        => value.TrimStart().StartsWith(",", StringComparison.Ordinal)
           || value.TrimStart().StartsWith(";", StringComparison.Ordinal);

    private static void ApplyInvoiceTableProfile(DocumentModel.Table table)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        string[] headers = table.Rows[0].Cells
            .Select(cell => GetCellText(cell).Trim())
            .ToArray();
        if (headers.Length == 4
            && IsHeader(headers[0], "description", "item")
            && IsHeader(headers[1], "quantity", "qty")
            && IsHeader(headers[2], "unit price", "price", "rate")
            && IsHeader(headers[3], "amount", "line total", "total"))
        {
            if (table.ColumnWidths.Count == 0)
            {
                table.ColumnWidths = [255, 68, 84, 90];
                table.ColumnWidthUnit = "pt";
            }
        }
        else if (headers.Length == 5
                 && IsHeader(headers[2], "quantity", "qty")
                 && IsHeader(headers[3], "unit price", "price", "rate")
                 && IsHeader(headers[4], "amount", "line total", "total"))
        {
            if (table.ColumnWidths.Count == 0)
            {
                table.ColumnWidths = [72, 187, 58, 85, 95];
                table.ColumnWidthUnit = "pt";
            }
        }

        HashSet<int> numericColumns = headers
            .Select((header, index) => new { Header = header.Trim().ToLowerInvariant(), Index = index })
            .Where(item => item.Header is "quantity" or "qty" or "unit price" or "price" or "rate" or "amount" or "line total" or "total")
            .Select(item => item.Index)
            .ToHashSet();

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < table.Rows[rowIndex].Cells.Count; columnIndex++)
            {
                DocumentModel.TableCell cell = table.Rows[rowIndex].Cells[columnIndex];
                cell.CellStyle ??= new DocumentModel.CellStyleDefinition();
                cell.CellStyle.VerticalAlignment ??= "center";
                if (numericColumns.Contains(columnIndex))
                {
                    cell.CellStyle.HorizontalAlignment ??= rowIndex == 0 ? "center" : "right";
                }
            }
        }
    }

    private static void SetLabeledRuns(
        DocumentModel.Paragraph paragraph,
        string label,
        string value,
        string accentColor,
        bool emphasizeValue)
    {
        DocumentModel.TextStyleDefinition labelStyle = new()
        {
            Bold = true,
            ColorHex = accentColor
        };
        DocumentModel.TextStyleDefinition? valueStyle = emphasizeValue
            ? new DocumentModel.TextStyleDefinition
            {
                Bold = true,
                FontSize = 12,
                FontSizeUnit = "pt",
                ColorHex = accentColor
            }
            : null;

        paragraph.Text = null;
        paragraph.Runs =
        [
            new DocumentModel.Run
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = $"{label.Trim()}: ",
                Style = emphasizeValue ? valueStyle : labelStyle
            },
            new DocumentModel.Run
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = value.Trim(),
                Style = valueStyle
            }
        ];
    }

    private static DocumentModel.DocumentBlock CreateParagraphBlock(string text, string role)
        => new()
        {
            Type = "paragraph",
            Paragraph = new DocumentModel.Paragraph
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = role,
                Text = text.Trim()
            }
        };

    private static bool LooksLikeInvoice(DocumentModel.Document document)
    {
        string title = document.Title ?? string.Empty;
        if (title.Contains("invoice", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        List<DocumentModel.Table> tables = document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => block.Table)
            .OfType<DocumentModel.Table>()
            .ToList();
        if (tables.Any(IsInvoiceLineItemTable))
        {
            return true;
        }

        List<string> paragraphs = document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => GetParagraphText(block.Paragraph))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return paragraphs.Take(3).Any(text => text.Contains("invoice", StringComparison.OrdinalIgnoreCase))
               || HasStrongInvoiceSignals(paragraphs);
    }

    private static bool HasStrongInvoiceSignals(IEnumerable<string> paragraphs)
    {
        int lineItems = 0;
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string paragraph in paragraphs)
        {
            string text = paragraph.Trim();
            if (text.StartsWith("Line Item", StringComparison.OrdinalIgnoreCase))
            {
                lineItems++;
            }

            if (TrySplitLabel(text, out string label, out _))
            {
                labels.Add(NormalizeLabel(label));
            }
        }

        int totals = InvoiceTotalLabels.Count(labels.Contains);
        bool hasPaymentTerms = labels.Contains("Payment Terms");
        return (lineItems >= 2 && totals >= 2)
               || (totals >= 3 && hasPaymentTerms);
    }

    private static void EnsureInvoiceTitle(
        DocumentModel.Document document,
        DocumentAutomationOptions? options,
        List<string> warnings)
    {
        string titleStyleName = options?.StyleRoles?.Title ?? "Title";
        List<DocumentModel.Paragraph> paragraphs = document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => block.Paragraph)
            .OfType<DocumentModel.Paragraph>()
            .ToList();

        bool hasVisibleInvoiceTitle = false;
        foreach (DocumentModel.Paragraph paragraph in paragraphs)
        {
            string text = GetParagraphText(paragraph).Trim();
            if (text.Equals("Invoice", StringComparison.OrdinalIgnoreCase))
            {
                paragraph.Role ??= "title";
                hasVisibleInvoiceTitle = true;
            }

            if (text.StartsWith("Line Item", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(paragraph.Role, "title", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(paragraph.StyleName, titleStyleName, StringComparison.OrdinalIgnoreCase)))
            {
                paragraph.Role = "body";
                paragraph.StyleName = null;
                warnings.Add("Corrected a line item that the model incorrectly marked as the document title.");
            }
        }

        if (string.IsNullOrWhiteSpace(document.Title) && !hasVisibleInvoiceTitle)
        {
            document.Title = "INVOICE";
            warnings.Add("Added the missing visible invoice title using the configured title style.");
        }
    }

    private static bool HasInlineFormatting(DocumentModel.Paragraph paragraph)
        => paragraph.Runs.Any(run => run.Style is not null || !string.IsNullOrWhiteSpace(run.StyleName));

    private static bool TryParseDelimitedRow(string text, int columnCount, out string[] cells)
    {
        cells = [];
        if (string.IsNullOrWhiteSpace(text) || !text.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        List<string> values = text.Split('|').Select(value => value.Trim()).ToList();
        if (values.Count > 0 && values[0].Length == 0)
        {
            values.RemoveAt(0);
        }

        if (values.Count > 0 && values[^1].Length == 0)
        {
            values.RemoveAt(values.Count - 1);
        }

        if (values.Count != columnCount || values.All(IsMarkdownSeparator))
        {
            return false;
        }

        cells = values.ToArray();
        return true;
    }

    private static bool IsMarkdownSeparator(string value)
    {
        string candidate = value.Trim().Trim(':');
        return candidate.Length >= 3 && candidate.All(character => character == '-');
    }

    private static bool TrySplitLabel(string text, out string label, out string value)
    {
        int separator = text.IndexOf(':');
        if (separator <= 0 || separator >= text.Length - 1)
        {
            label = string.Empty;
            value = string.Empty;
            return false;
        }

        label = text[..separator].Trim();
        value = text[(separator + 1)..].Trim();
        return label.Length > 0 && value.Length > 0;
    }

    private static string NormalizeLabel(string value)
    {
        int parenthesis = value.IndexOf('(');
        return (parenthesis > 0 ? value[..parenthesis] : value).Trim();
    }

    private static bool IsHeader(string actual, params string[] candidates)
        => candidates.Any(candidate => actual.Equals(candidate, StringComparison.OrdinalIgnoreCase));

    private static List<DocumentModel.TableCell> CreateCells(IEnumerable<string> values)
        => values.Select(value => new DocumentModel.TableCell
        {
            Id = Guid.NewGuid().ToString("N"),
            Blocks = [CreateParagraphBlock(value, "body")]
        }).ToList();

    private static string GetCellText(DocumentModel.TableCell cell)
        => string.Join("\n", cell.Blocks.Select(block => GetParagraphText(block.Paragraph)));

    private static string GetParagraphText(DocumentModel.Paragraph? paragraph)
        => paragraph is null
            ? string.Empty
            : paragraph.Runs.Count > 0
                ? string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty))
                : paragraph.Text ?? string.Empty;

    private static string GetOperationText(DocumentOperation operation)
        => operation.Runs.Count > 0
            ? string.Concat(operation.Runs.Select(run => run.Text ?? string.Empty))
            : operation.Text ?? string.Empty;
}
