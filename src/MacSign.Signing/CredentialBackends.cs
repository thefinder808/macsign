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
    /// <summary>
    /// Builds a credential. The token covers construction itself, which for a cloud backend is a
    /// network round-trip: Trusted Signing has no "get certificate" call, so its credential
    /// discovers its own chain by signing a throwaway digest before it can be handed back.
    /// </summary>
    internal delegate ICredentialSigner CredentialFactory(SigningOptions options, CancellationToken ct);

    /// <summary>Set by <c>MacSign.Signing.Pkcs11</c> to enable PKCS#11 signing.</summary>
    internal static CredentialFactory? Pkcs11Factory { get; set; }

    /// <summary>Set by <c>MacSign.Signing.Azure</c> to enable Azure Trusted Signing.</summary>
    internal static CredentialFactory? TrustedSigningFactory { get; set; }
}
