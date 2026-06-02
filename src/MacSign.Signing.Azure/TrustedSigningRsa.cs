using System.Security.Cryptography;

namespace MacSign.Signing.Azure;

/// <summary>
/// An <see cref="RSA"/> whose private operation is delegated to Azure Trusted Signing.
/// The public parameters come from the leaf certificate; the sign callback receives the
/// <b>already-computed</b> hash and must NOT re-hash it. This is the exact extension
/// point the BCL <c>SignedCms.ComputeSignature</c> invokes — the same seam the PKCS#11
/// token uses — so the CMS layer treats a cloud key identically to an in-proc one.
/// </summary>
internal sealed class TrustedSigningRsa : RSA
{
    private readonly RSAParameters _publicParameters;
    private readonly Func<byte[], string, byte[]> _signHash; // (hash, jwaAlgorithmId) -> signature

    public TrustedSigningRsa(RSAParameters publicParameters, Func<byte[], string, byte[]> signHash)
    {
        _publicParameters = publicParameters;
        _signHash = signHash;
        KeySizeValue = (publicParameters.Modulus?.Length ?? 0) * 8;
    }

    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters)
            throw new CryptographicException("The Trusted Signing private key cannot leave Azure.");
        return _publicParameters;
    }

    public override void ImportParameters(RSAParameters parameters) =>
        throw new NotSupportedException("This RSA key is backed by Azure Trusted Signing and is import-only via its certificate.");

    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        _signHash(hash, AlgorithmId(hashAlgorithm, padding));

    public override bool TrySignHash(
        ReadOnlySpan<byte> hash, Span<byte> destination, HashAlgorithmName hashAlgorithm,
        RSASignaturePadding padding, out int bytesWritten)
    {
        var signature = _signHash(hash.ToArray(), AlgorithmId(hashAlgorithm, padding));
        if (signature.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }
        signature.CopyTo(destination);
        bytesWritten = signature.Length;
        return true;
    }

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        using var verifier = Create();
        verifier.ImportParameters(_publicParameters);
        return verifier.VerifyHash(hash, signature, hashAlgorithm, padding);
    }

    // SignData decomposes to HashData + SignHash. Hashing the (small) buffer in-process
    // is correct: it's CMS's SignedAttributes, not the artifact — the artifact's digest
    // was already computed by the format layer. The CMS path calls SignHash directly, so
    // this is only here to keep the RSA well-formed for any SignData caller.
    protected override byte[] HashData(byte[] data, int offset, int count, HashAlgorithmName hashAlgorithm)
    {
        using var hasher = CreateHasher(hashAlgorithm);
        return hasher.ComputeHash(data, offset, count);
    }

    protected override byte[] HashData(Stream data, HashAlgorithmName hashAlgorithm)
    {
        using var hasher = CreateHasher(hashAlgorithm);
        return hasher.ComputeHash(data);
    }

    private static HashAlgorithm CreateHasher(HashAlgorithmName name) => name.Name switch
    {
        nameof(HashAlgorithmName.SHA256) => SHA256.Create(),
        nameof(HashAlgorithmName.SHA384) => SHA384.Create(),
        nameof(HashAlgorithmName.SHA512) => SHA512.Create(),
        _ => throw new NotSupportedException($"Unsupported hash algorithm: {name.Name}"),
    };

    /// <summary>Map (hash, padding) to the JWA id Trusted Signing expects.</summary>
    private static string AlgorithmId(HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        bool pss = padding == RSASignaturePadding.Pss;
        return hashAlgorithm.Name switch
        {
            nameof(HashAlgorithmName.SHA256) => pss ? "PS256" : "RS256",
            nameof(HashAlgorithmName.SHA384) => pss ? "PS384" : "RS384",
            nameof(HashAlgorithmName.SHA512) => pss ? "PS512" : "RS512",
            _ => throw new NotSupportedException($"Unsupported signature hash: {hashAlgorithm.Name}"),
        };
    }
}
