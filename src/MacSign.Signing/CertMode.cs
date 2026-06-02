namespace MacSign.Signing;

/// <summary>
/// Which kind of code-signing credential to use. The enum carries all eventual
/// modes; Phase 1 wires only <see cref="Pfx"/>.
/// </summary>
public enum CertMode
{
    /// <summary>A PKCS#12 / <c>.pfx</c> file (self-signed, test, or legacy certs).</summary>
    Pfx,

    /// <summary>A PKCS#11 hardware token / HSM. (Not implemented in Phase 1.)</summary>
    Pkcs11,

    /// <summary>Azure Trusted Signing (cloud HSM). (Not implemented in Phase 1.)</summary>
    TrustedSigning,
}
