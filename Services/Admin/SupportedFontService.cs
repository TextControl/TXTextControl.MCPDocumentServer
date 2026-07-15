using System;
using System.Collections.Generic;
using System.Linq;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Admin;

public sealed class SupportedFontService
{
    private readonly Lazy<IReadOnlyList<string>> _fonts = new(LoadFonts);

    public IReadOnlyList<string> GetSupportedFonts()
        => _fonts.Value;

    private static IReadOnlyList<string> LoadFonts()
    {
        try
        {
            TxTextControlLicensing.Configure();
            using var tx = new ServerTextControl();
            tx.Create();

            return (tx.GetSupportedFonts() ?? [])
                .Where(font => !string.IsNullOrWhiteSpace(font))
                .Select(font => font.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(font => font, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return
            [
                "Arial",
                "Calibri",
                "Liberation Sans",
                "Times New Roman"
            ];
        }
    }
}
