using MacSign.Signing.Credentials;

namespace MacSign.Signing;

/// <summary>
/// Registration hook for optional, out-of-process credential backends (e.g.
/// PKCS#11) whose native dependencies must stay out of the dependency-clean core.
/// The backend package calls into this to register itself; the core never
/// compile-time references it.
/// </summary>
public static class CredentialBackends
{
    /// <summary>Set by <c>MacSign.Signing.Pkcs11</c> to enable PKCS#11 signing.</summary>
    internal static Func<SigningOptions, ICredentialSigner>? Pkcs11Factory { get; set; }
}
