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

    /// <summary>
    /// The token covering whatever the credential is signing right now, set by
    /// <see cref="UseCancellation"/>. It is a field rather than a parameter because the call
    /// arrives through <see cref="RSA.SignHash(byte[], HashAlgorithmName, RSASignaturePadding)"/>,
    /// which has nowhere to carry one.
    /// </summary>
    private CancellationToken _operationCt;

    public AzureTrustedSigner(
        string endpoint, string account, string profile, IAzureTokenProvider tokens,
        HttpMessageHandler? handler = null, TimeSpan? pollInterval = null,
        CancellationToken ct = default)
    {
        _client = new TrustedSigningClient(endpoint, account, profile, tokens, handler, pollInterval);

        // The probe below is a REST round-trip like any other sign, so it needs a token too —
        // and it runs before anyone can call UseCancellation on us. It stays in place afterwards
        // as the fallback for a caller that never opens a scope.
        _operationCt = ct;

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
    /// Adopt the caller's cancellation token until the returned scope is disposed. Signing here
    /// is a REST round-trip plus a poll loop — minutes, in the worst case — and none of it is
    /// interruptible unless the token gets in this way. See <see cref="ICredentialSigner"/>.
    /// </summary>
    public IDisposable? UseCancellation(CancellationToken ct) => new OperationScope(this, ct);

    /// <summary>
    /// Bridge the CMS layer's synchronous signing call to the async REST client. Safe
    /// here: the CLI is a console app with no synchronization context to deadlock on.
    /// </summary>
    private TrustedSigningResult SignDigest(byte[] digest, string algorithm) =>
        _client.SignDigestAsync(digest, algorithm, _operationCt)
            .ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>Restores the previous token on dispose, so scopes nest without surprises.</summary>
    private sealed class OperationScope : IDisposable
    {
        private readonly AzureTrustedSigner _owner;
        private readonly CancellationToken _previous;

        public OperationScope(AzureTrustedSigner owner, CancellationToken ct)
        {
            _owner = owner;
            _previous = owner._operationCt;
            owner._operationCt = ct;
        }

        public void Dispose() => _owner._operationCt = _previous;
    }

    public void Dispose()
    {
        _signingKey.Dispose();
        _certificate.Dispose();
        foreach (var cert in _chain) cert.Dispose();
        _client.Dispose();
    }
}
