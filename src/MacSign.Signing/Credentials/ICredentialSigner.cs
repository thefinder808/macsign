using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Credentials;

/// <summary>
/// A code-signing credential. Phase 1 exposes an in-proc signing certificate
/// (PFX). The interface is intentionally minimal; later backends (PKCS#11, Azure)
/// will add a delegated "sign this digest" path for keys that never enter the
/// process.
/// </summary>
internal interface ICredentialSigner : IDisposable
{
    /// <summary>The signing (leaf) certificate, including its private key.</summary>
    X509Certificate2 Certificate { get; }

    /// <summary>Intermediate certificates to embed (excludes leaf and root). May be empty.</summary>
    IReadOnlyList<X509Certificate2> Chain { get; }
}
