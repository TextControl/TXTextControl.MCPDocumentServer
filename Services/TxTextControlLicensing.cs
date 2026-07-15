using System.Reflection;
using TXTextControl;

namespace TxTextControl.McpServer.Services;

/// <summary>
/// Configures TX Text Control to resolve its license from this wrapper assembly.
/// </summary>
public static class TxTextControlLicensing
{
    private static readonly Assembly LicenseAssembly = typeof(TxTextControlLicensing).Assembly;

    public static void Configure()
        => ServerTextControl.EntryAssembly = LicenseAssembly;
}
