using System.Security.Cryptography;
using MacSign.Signing.Azure;

namespace MacSign.Signing.Tests;

/// <summary>
/// The delegating RSA that fronts Azure Trusted Signing must verify the signature the
/// service returns against the leaf's public key before handing it to the CMS builder —
/// a garbled/wrong signature should fail loudly, not silently produce a broken artifact.
/// </summary>
public class AzureSelfVerifyTests
{
    [Fact]
    public void Rejects_a_signature_that_does_not_verify_under_the_public_key()
    {
        using var key = RSA.Create(2048);
        var pub = key.ExportParameters(false);
        // Delegate "signs" the wrong bytes → the returned signature won't verify for the digest.
        var bad = new TrustedSigningRsa(pub,
            (hash, _) => key.SignHash(new byte[hash.Length], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var digest = SHA256.HashData([1, 2, 3]);

        Assert.Throws<CryptographicException>(() =>
            bad.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Accepts_a_correct_signature()
    {
        using var key = RSA.Create(2048);
        var pub = key.ExportParameters(false);
        var good = new TrustedSigningRsa(pub,
            (hash, _) => key.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var digest = SHA256.HashData([1, 2, 3]);

        var sig = good.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.True(key.VerifyHash(digest, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
