using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using MacSign.Signing.Cms;
using MacSign.Signing.Credentials;
using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class SecurityHardeningTests
{
    [Fact]
    public void Verify_rejects_a_signature_whose_content_is_not_spc_indirect_data()
    {
        // A CMS carrying a correct SHA-256 SpcIndirectData payload, but under the WRONG
        // content-type OID (id-data, not SPC_INDIRECT_DATA). The embedded digest still matches
        // the file, so only an explicit content-type check stops this being reported VALID.
        byte[] pe = FixturePe.UnsignedBytes();
        byte[] spc = SpcEncoder.BuildPeIndirectData(new PeFormat().ComputeDigest(pe));

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Wrong CT", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        var content = new ContentInfo(new Oid("1.2.840.113549.1.7.1"), spc); // id-data, NOT SPC_INDIRECT_DATA
        var cms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        cms.ComputeSignature(signer);

        byte[] signed = new PeFormat().Embed(pe, cms.Encode());
        var r = SignatureVerifier.Verify(signed, "wrongct.dll");

        Assert.True(r.IsSigned);
        Assert.False(r.SignatureValid);
    }

    [Fact]
    public void PfxCredentialSigner_throws_cleanly_for_a_key_less_pkcs12()
    {
        // Guards the constructor's dispose-on-failure path: it must still surface the
        // documented error, not swallow it.
        using var tmp = new TempDir();
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Public Only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        using var publicOnly = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
        var pfx = Path.Combine(tmp.Path, "public-only.pfx");
        File.WriteAllBytes(pfx, publicOnly.Export(X509ContentType.Pkcs12, "pw"));

        Assert.Throws<InvalidOperationException>(() => new PfxCredentialSigner(pfx, "pw"));
    }
}
