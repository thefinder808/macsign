using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MacSign.Signing.Credentials;

namespace MacSign.Signing.Azure;

/// <summary>
/// A credential whose private key lives in Azure Trusted Signing. The key never enters
/// this process: <see cref="SigningKey"/> is a delegating RSA that POSTs each digest to
/// the Trusted Signing sign endpoint. Trusted Signing has no separate "get certificate"
/// call, so the leaf + chain are discovered on construction by signing a throwaway
/// 32-byte digest — every sign response carries the signing certificate.
/// </summary>
internal sealed class AzureTrustedSigner : ICredentialSigner
{
    private const string AuthenticodeAlgorithm = "RS256"; // RSA + SHA-256

    private readonly TrustedSigningClient _client;
    private readonly X509Certificate2 _certificate;
    private readonly List<X509Certificate2> _chain = [];
    private readonly RSA _signingKey;

    public AzureTrustedSigner(
        string endpoint, string account, string profile, IAzureTokenProvider tokens,
        HttpMessageHandler? handler = null, TimeSpan? pollInterval = null)
    {
        _client = new TrustedSigningClient(endpoint, account, profile, tokens, handler, pollInterval);

        // Discover the signing certificate by signing a throwaway digest; the signature
        // is discarded and only the returned certificate chain is kept.
        var probe = SignDigest(new byte[32], AuthenticodeAlgorithm);
        if (string.IsNullOrWhiteSpace(probe.SigningCertificate))
            throw new InvalidOperationException("Trusted Signing did not return a signing certificate.");

        var certs = CertificateChain.Parse(probe.SigningCertificate!);
        if (certs.Count == 0)
            throw new InvalidOperationException("Trusted Signing returned an empty certificate chain.");

        // The leaf is the end-entity: the cert that is not the issuer of any other in the
        // chain (don't assume PKCS#7 ordering).
        _certificate = certs.FirstOrDefault(c => !certs.Any(other =>
            !ReferenceEquals(other, c) && other.IssuerName.RawData.AsSpan().SequenceEqual(c.SubjectName.RawData)))
            ?? certs[0];

        // Embed intermediates only — exclude the leaf and the self-signed root.
        foreach (var cert in certs)
        {
            if (ReferenceEquals(cert, _certificate)) continue;
            bool isRoot = cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData);
            if (!isRoot) _chain.Add(cert);
        }

        RSAParameters publicParameters;
        using (var leafRsa = _certificate.GetRSAPublicKey()
            ?? throw new InvalidOperationException("The Trusted Signing leaf certificate is not RSA (only RSA profiles are supported)."))
        {
            publicParameters = leafRsa.ExportParameters(false);
        }

        _signingKey = new TrustedSigningRsa(publicParameters, (hash, alg) => SignDigest(hash, alg).Signature);
    }

    public X509Certificate2 Certificate => _certificate;

    public AsymmetricAlgorithm SigningKey => _signingKey;

    public IReadOnlyList<X509Certificate2> Chain => _chain;

    /// <summary>
    /// Bridge the CMS layer's synchronous signing call to the async REST client. Safe
    /// here: the CLI is a console app with no synchronization context to deadlock on.
    /// </summary>
    private TrustedSigningResult SignDigest(byte[] digest, string algorithm) =>
        _client.SignDigestAsync(digest, algorithm, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();

    public void Dispose()
    {
        _signingKey.Dispose();
        _certificate.Dispose();
        foreach (var cert in _chain) cert.Dispose();
        _client.Dispose();
    }
}
