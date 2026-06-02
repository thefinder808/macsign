using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class DualSignatureTests
{
    [Fact]
    public async Task Verify_reports_every_signer_on_a_co_signed_pe()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path, subject: "Signer One");
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);

        // Add a second co-signer over the same content, then re-embed onto the original bytes.
        var pe = new PeFormat();
        Assert.True(pe.TryExtractSignature(await File.ReadAllBytesAsync(dll), out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        using var rsa2 = RSA.Create(2048);
        var req2 = new CertificateRequest("CN=Signer Two", rsa2, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert2 = req2.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert2)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = X509IncludeOption.EndCertOnly,
        });
        await File.WriteAllBytesAsync(dll, pe.Embed(FixturePe.UnsignedBytes(), cms.Encode()));

        var r = SignatureVerifier.Verify(dll);

        Assert.True(r.SignatureValid, r.Error);
        Assert.Equal(2, r.Signers.Count);
        Assert.All(r.Signers, s => Assert.True(s.SignatureValid));
        Assert.Contains(r.Signers, s => s.Subject!.Contains("Signer One"));
        Assert.Contains(r.Signers, s => s.Subject!.Contains("Signer Two"));
    }
}
