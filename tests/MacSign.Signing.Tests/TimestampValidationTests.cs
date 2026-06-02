using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

/// <summary>
/// The RFC3161 timestamp shown by <c>verify</c> must be cryptographically validated, not
/// merely decoded — the unsigned-attribute bag isn't covered by the signature, so a token
/// grafted from an unrelated signature must not be reported as this file's signing time.
/// </summary>
public class TimestampValidationTests
{
    private const string Tsa = "http://timestamp.digicert.com";
    private const string Rfc3161Oid = "1.3.6.1.4.1.311.3.3.1";

    [Fact]
    public async Task Verify_reports_a_validated_timestamp()
    {
        if (!await Net.CanReachAsync(Tsa)) return; // offline — skip

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var options = new SigningOptions
        {
            CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password, TimestampUrl = Tsa,
        };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);

        var r = SignatureVerifier.Verify(dll);

        Assert.True(r.SignatureValid, r.Error);
        Assert.NotNull(r.Timestamp); // a real, valid TSA token is still surfaced
    }

    [Fact]
    public async Task Verify_ignores_a_timestamp_grafted_from_another_signature()
    {
        if (!await Net.CanReachAsync(Tsa)) return; // offline — skip

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);

        // File 1: signed WITH a real timestamp → harvest its valid RFC3161 token.
        var dll1 = FixturePe.CopyToTemp(tmp.Path);
        var opt1 = new SigningOptions
        {
            CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password, TimestampUrl = Tsa,
        };
        var s1 = AuthenticodeSigner.TryCreate(opt1, out _)!;
        Assert.True((await s1.SignAsync(tmp.Path, dll1, opt1)).Success);
        byte[] token1 = HarvestTimestamp(await File.ReadAllBytesAsync(dll1));

        // File 2: DIFFERENT content, signed WITHOUT a timestamp → a different signature/imprint.
        byte[] body2 = FixturePe.UnsignedBytes();
        body2[600] ^= 0xFF;
        var dll2 = Path.Combine(tmp.Path, "other.dll");
        await File.WriteAllBytesAsync(dll2, body2);
        var opt2 = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var s2 = AuthenticodeSigner.TryCreate(opt2, out _)!;
        Assert.True((await s2.SignAsync(tmp.Path, dll2, opt2)).Success);

        // Graft file 1's valid token onto file 2's signature, then re-embed.
        var pe = new PeFormat();
        Assert.True(pe.TryExtractSignature(await File.ReadAllBytesAsync(dll2), out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        cms.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData(new Oid(Rfc3161Oid), token1));
        await File.WriteAllBytesAsync(dll2, pe.Embed(body2, cms.Encode()));

        var r = SignatureVerifier.Verify(dll2);

        Assert.True(r.IsSigned);
        Assert.True(r.SignatureValid, r.Error); // the signature itself is still intact…
        Assert.Null(r.Timestamp);               // …but the grafted timestamp is not this file's
    }

    private static byte[] HarvestTimestamp(byte[] signedPe)
    {
        var pe = new PeFormat();
        Assert.True(pe.TryExtractSignature(signedPe, out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        foreach (CryptographicAttributeObject a in cms.SignerInfos[0].UnsignedAttributes)
            if (a.Oid.Value == Rfc3161Oid)
                return a.Values[0].RawData;
        throw new Xunit.Sdk.XunitException("expected a timestamp attribute on the reference file");
    }
}
