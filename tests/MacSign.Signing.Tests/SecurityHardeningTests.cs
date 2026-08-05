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
    public void SigningOptions_ToString_redacts_secrets()
    {
        var o = new SigningOptions
        {
            PfxPath = "/tmp/cert.pfx",
            Secret = "hunter2-password",
            TrustedSigningAccessToken = "eyJ.super.secret.jwt",
        };

        var s = o.ToString();

        Assert.DoesNotContain("hunter2-password", s);      // the PFX password must not leak
        Assert.DoesNotContain("eyJ.super.secret.jwt", s);  // nor the Azure token
        Assert.Contains("/tmp/cert.pfx", s);               // non-secret fields stay useful
        Assert.Contains("Secret = (set)", s);              // presence is shown, value is not
    }

    [Fact]
    public void SigningOptions_ToString_shows_the_tenant_and_source_but_redacts_the_auth_record()
    {
        var o = new SigningOptions
        {
            TrustedSigningTenantId = "11111111-2222-3333-4444-555555555555",
            TrustedSigningCredentialSource = TrustedSigningCredentialSource.InteractiveBrowser,
            TrustedSigningAuthRecord = """{"username":"someone@contoso.com"}""",
        };

        var s = o.ToString();

        // Which tenant and which credential source were used is the whole point of this
        // feature — "I couldn't tell which identity signed" is the bug being fixed — so
        // both must survive into a diagnostic line.
        Assert.Contains("11111111-2222-3333-4444-555555555555", s);
        Assert.Contains("InteractiveBrowser", s);

        // The record holds no token, but it does embed the signed-in UPN. Show presence,
        // not content, so a logged options dump doesn't leak who the operator is.
        Assert.DoesNotContain("someone@contoso.com", s);
        Assert.Contains("TrustedSigningAuthRecord = (set)", s);
    }

    [Fact]
    public void SigningOptions_ToString_accounts_for_every_property()
    {
        // PrintMembers is a hand-maintained allow-list, so a property added to the record is
        // silently absent from ToString() until someone remembers to edit it — a diagnostic
        // quietly goes missing and nothing fails. Enumerate the real property set rather than
        // trusting that memory. Redacted fields still print "Name = (null)", so every property
        // must appear either way; the two tests above pin which ones show a value.
        var s = new SigningOptions().ToString();

        foreach (var p in typeof(SigningOptions).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            Assert.Contains(p.Name + " = ", s);
    }

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
