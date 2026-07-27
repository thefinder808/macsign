using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Credentials;

/// <summary>
/// Loads a signing certificate + private key from a PKCS#12 / <c>.pfx</c> file.
/// The password is consumed in-process during load and is never persisted or logged;
/// prefer supplying it via <c>--password-env</c> rather than a plaintext argument.
/// </summary>
internal sealed class PfxCredentialSigner : ICredentialSigner
{
    private readonly X509Certificate2 _leaf;
    private readonly AsymmetricAlgorithm _signingKey;
    private readonly List<X509Certificate2> _chain = [];

    public PfxCredentialSigner(string pfxPath, string? password)
    {
        var bytes = File.ReadAllBytes(pfxPath);

        // NOTE: EphemeralKeySet is unsupported on macOS (throws), so we use the
        // default key set. The collection loader lets us separate leaf from chain.
        X509Certificate2Collection bag;
        try
        {
            bag = X509CertificateLoader.LoadPkcs12Collection(
                bytes, password, X509KeyStorageFlags.DefaultKeySet);
        }
        catch (CryptographicException ex)
        {
            // The platform's own message is unhelpful in a code-signing context (on
            // Windows it's literally "The specified network password is not correct").
            // Wrap it with the original as InnerException so nothing is lost.
            throw new CryptographicException(
                string.IsNullOrEmpty(password)
                    ? "Could not open the PFX. If it is password-protected, supply the password."
                    : "Could not open the PFX — the password may be wrong, or the file may be corrupt.",
                ex);
        }

        try
        {
            X509Certificate2? leaf = null;
            foreach (var cert in bag)
            {
                if (leaf is null && cert.HasPrivateKey)
                    leaf = cert;
                else
                    _chain.Add(cert);
            }

            _leaf = leaf
                ?? throw new InvalidOperationException("The PFX contains no certificate with a private key.");

            _signingKey = (AsymmetricAlgorithm?)_leaf.GetRSAPrivateKey() ?? _leaf.GetECDsaPrivateKey()
                ?? throw new InvalidOperationException("The PFX certificate has no usable RSA/ECDSA private key.");
        }
        catch
        {
            // Don't leak the loaded certs' native key handles if construction fails partway.
            foreach (var cert in bag) cert.Dispose();
            throw;
        }
    }

    public X509Certificate2 Certificate => _leaf;

    public AsymmetricAlgorithm SigningKey => _signingKey;

    public IReadOnlyList<X509Certificate2> Chain => _chain;

    public void Dispose()
    {
        _signingKey.Dispose();
        _leaf.Dispose();
        foreach (var cert in _chain) cert.Dispose();
    }
}
