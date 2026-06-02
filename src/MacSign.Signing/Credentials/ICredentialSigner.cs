using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Credentials;

/// <summary>
/// A code-signing credential: a public leaf certificate plus the signing key.
/// The key is an <see cref="AsymmetricAlgorithm"/> so it can be either in-process
/// (PFX) or a delegating wrapper over a key that never leaves a token/cloud
/// (PKCS#11, Azure). The CMS layer hands it to <c>CmsSigner</c> unchanged.
/// </summary>
internal interface ICredentialSigner : IDisposable
{
    /// <summary>The signing (leaf) certificate. For token/cloud keys this has no in-proc private key.</summary>
    X509Certificate2 Certificate { get; }

    /// <summary>The signing key — in-proc (PFX) or delegating to a token/cloud.</summary>
    AsymmetricAlgorithm SigningKey { get; }

    /// <summary>Intermediate certificates to embed (excludes leaf and root). May be empty.</summary>
    IReadOnlyList<X509Certificate2> Chain { get; }
}
