using MacSign.Signing.Formats;

namespace MacSign.Signing;

/// <summary>
/// Registration hook for optional file-format backends whose dependencies must
/// stay out of the dependency-clean core (e.g. MSI, which needs a CFBF library).
/// The backend package registers itself; the core never references it.
/// </summary>
public static class FormatBackends
{
    /// <summary>Set by <c>MacSign.Signing.Msi</c> to enable MSI signing.</summary>
    internal static ISignatureFormat? Msi { get; set; }
}
