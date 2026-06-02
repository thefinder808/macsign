using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Credentials;

/// <summary>
/// Loads a signing certificate + private key from a PKCS#12 / <c>.pfx</c> file.
/// The password is consumed in-process during load and never persisted, logged,
/// or placed on a command line.
/// </summary>
internal sealed class PfxCredentialSigner : ICredentialSigner
{
    private readonly X509Certificate2 _leaf;
    private readonly List<X509Certificate2> _chain = [];

    public PfxCredentialSigner(string pfxPath, string? password)
    {
        var bytes = File.ReadAllBytes(pfxPath);

        // NOTE: EphemeralKeySet is unsupported on macOS (throws), so we use the
        // default key set. The collection loader lets us separate leaf from chain.
        var bag = X509CertificateLoader.LoadPkcs12Collection(
            bytes, password, X509KeyStorageFlags.DefaultKeySet);

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
    }

    public X509Certificate2 Certificate => _leaf;

    public IReadOnlyList<X509Certificate2> Chain => _chain;

    public void Dispose()
    {
        _leaf.Dispose();
        foreach (var cert in _chain) cert.Dispose();
    }
}
