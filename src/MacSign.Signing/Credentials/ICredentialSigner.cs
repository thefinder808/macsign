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

    /// <summary>
    /// Scope <paramref name="ct"/> over the synchronous calls the CMS layer is about to make on
    /// <see cref="SigningKey"/>, until the returned scope is disposed.
    /// <para>
    /// The key is an <see cref="AsymmetricAlgorithm"/> because that is the extension point
    /// <c>SignedCms.ComputeSignature</c> reaches through, and its <c>SignHash</c> takes no
    /// <see cref="CancellationToken"/>. A credential whose signing is a network round-trip
    /// (Azure) would therefore be uninterruptible — so it is handed the caller's token
    /// out-of-band here instead. In-process keys have nothing to cancel and return null,
    /// which <c>using</c> treats as a no-op scope.
    /// </para>
    /// <para>
    /// Scopes are not safe to open concurrently on one credential; a credential belongs to a
    /// single <c>SignAsync</c> call, which signs its files one at a time.
    /// </para>
    /// </summary>
    IDisposable? UseCancellation(CancellationToken ct) => null;

    /// <summary>
    /// The account that authenticated to obtain this credential, for a cloud-backed one.
    /// Null for an in-process key, where the certificate already is the identity.
    /// <para>
    /// Display only — derived from an unvalidated token, so it must never gate a decision.
    /// </para>
    /// </summary>
    string? AuthenticatedAs => null;
}
