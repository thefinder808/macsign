using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using MacSign.Signing.Formats.Pe;

namespace MacSign.Signing.Tests;

public class TimestampTests
{
    private const string Tsa = "http://timestamp.digicert.com";
    private const string Rfc3161Oid = "1.3.6.1.4.1.311.3.3.1";

    [Fact]
    public async Task No_timestamp_url_means_no_timestamp_attribute()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);

        Assert.Null(FindTimestamp(Decode(dll).SignerInfos[0]));
    }

    [Fact]
    public async Task Timestamped_signature_carries_a_valid_rfc3161_token()
    {
        if (!await Net.CanReachAsync(Tsa))
            return; // offline / TSA unreachable — skip (xUnit 2.x has no dynamic skip)

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions
        {
            CertMode = CertMode.Pfx,
            PfxPath = pfx,
            Secret = TestCerts.Password,
            TimestampUrl = Tsa,
        };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        var result = await signer.SignAsync(tmp.Path, dll, options);
        Assert.True(result.Success, result.Error);

        var cms = Decode(dll);
        cms.CheckSignature(verifySignatureOnly: true); // signature still valid with the timestamp attached

        var attr = FindTimestamp(cms.SignerInfos[0]);
        Assert.NotNull(attr);

        Assert.True(Rfc3161TimestampToken.TryDecode(attr!.RawData, out var token, out _));
        var stampedAt = token!.TokenInfo.Timestamp;
        Assert.True((DateTimeOffset.UtcNow - stampedAt).Duration() < TimeSpan.FromHours(1),
            $"timestamp {stampedAt:o} is not recent");
    }

    private static SignedCms Decode(string dll)
    {
        var pe = new PeFormat();
        Assert.True(pe.TryExtractSignature(File.ReadAllBytes(dll), out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        return cms;
    }

    private static AsnEncodedData? FindTimestamp(SignerInfo signer)
    {
        foreach (CryptographicAttributeObject attr in signer.UnsignedAttributes)
            if (attr.Oid.Value == Rfc3161Oid)
                return attr.Values[0];
        return null;
    }
}
