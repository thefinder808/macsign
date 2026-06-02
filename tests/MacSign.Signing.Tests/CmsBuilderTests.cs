using System.Security.Cryptography.Pkcs;
using MacSign.Signing.Cms;
using MacSign.Signing.Credentials;

namespace MacSign.Signing.Tests;

public class CmsBuilderTests
{
    [Fact]
    public async Task Produces_authenticode_framed_signature()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        using var cred = new PfxCredentialSigner(pfx, TestCerts.Password);

        var fileDigest = new byte[32]; // dummy digest is fine for CMS structure checks
        var spc = SpcEncoder.BuildPeIndirectData(fileDigest);

        var pkcs7 = await new AuthenticodeCmsBuilder()
            .BuildAsync(spc, cred, new SigningOptions { Description = "MacSign", Url = "https://example.com" }, CancellationToken.None);

        var cms = new SignedCms();
        cms.Decode(pkcs7);

        // Encapsulated content type must be SPC_INDIRECT_DATA.
        Assert.Equal("1.3.6.1.4.1.311.2.1.4", cms.ContentInfo.ContentType.Value);

        // THE #1 risk: the eContent must be our raw SpcIndirectDataContent SEQUENCE,
        // not re-wrapped in an OCTET STRING. (Ultimate proof is signtool in CI.)
        Assert.Equal(spc, cms.ContentInfo.Content);

        Assert.Single(cms.SignerInfos);
        Assert.Equal("2.16.840.1.101.3.4.2.1", cms.SignerInfos[0].DigestAlgorithm.Value);

        // Signature is internally valid (no chain trust needed).
        cms.CheckSignature(verifySignatureOnly: true);

        // Our two Authenticode signed attributes are present.
        var attrOids = cms.SignerInfos[0].SignedAttributes
            .Cast<System.Security.Cryptography.CryptographicAttributeObject>()
            .Select(a => a.Oid.Value)
            .ToList();
        Assert.Contains("1.3.6.1.4.1.311.2.1.12", attrOids); // SpcSpOpusInfo
        Assert.Contains("1.3.6.1.4.1.311.2.1.11", attrOids); // SpcStatementType
    }
}
