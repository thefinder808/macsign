namespace MacSign.Signing;

/// <summary>
/// File extensions Authenticode can sign. PE images (<c>.exe</c>/<c>.dll</c>/<c>.sys</c>),
/// Windows Installer (<c>.msi</c>), and PowerShell (<c>.ps1</c>). Plain <c>.cmd</c>/<c>.bat</c>
/// are NOT Authenticode-signable and are intentionally absent.
///
/// NOTE: this lists what is signable *in principle*; the engine currently
/// implements PE and PowerShell (see <c>FormatRegistry</c>). Other signable
/// files (e.g. <c>.msi</c>) surface a clear "not yet implemented" message
/// rather than silently being skipped.
/// </summary>
public static class SignableExtensions
{
    public static readonly IReadOnlyList<string> All =
        [".exe", ".dll", ".sys", ".msi", ".ps1"];

    /// <summary>True if <paramref name="path"/> has an Authenticode-signable extension.</summary>
    public static bool IsSignable(string path) =>
        All.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
