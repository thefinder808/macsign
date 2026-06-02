using System.Security.Cryptography.Pkcs;
using MacSign.Signing.Formats.Pe;

namespace MacSign.Signing.Tests;

public class RoundTripTests
{
    [Fact]
    public async Task Sign_a_PE_then_extract_and_verify()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var original = await File.ReadAllBytesAsync(dll);

        var options = new SigningOptions
        {
            CertMode = CertMode.Pfx,
            PfxPath = pfx,
            Secret = TestCerts.Password,
            Description = "MacSign",
            Url = "https://example.com",
        };

        var signer = AuthenticodeSigner.TryCreate(options, out var error);
        Assert.NotNull(signer);
        Assert.Null(error);

        var result = await signer!.SignAsync(tmp.Path, dll, options);
        Assert.True(result.Success, result.Error);

        var signed = await File.ReadAllBytesAsync(dll);
        var pe = new PeFormat();

        Assert.True(pe.TryExtractSignature(signed, out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        cms.CheckSignature(verifySignatureOnly: true);
        Assert.Contains("MacSign Test", cms.SignerInfos[0].Certificate!.Subject);

        // The embedded SpcIndirectDataContent must match a fresh digest of the original bytes.
        var freshSpc = pe.BuildSpcIndirectData(pe.ComputeDigest(original));
        Assert.Equal(freshSpc, cms.ContentInfo.Content);
    }

    [Fact]
    public async Task Re_signing_skips_an_already_signed_file()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;

        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);
        var afterFirst = await File.ReadAllBytesAsync(dll);

        var log = new ListProgress();
        Assert.True((await signer.SignAsync(tmp.Path, dll, options, log)).Success);
        var afterSecond = await File.ReadAllBytesAsync(dll);

        Assert.Equal(afterFirst, afterSecond); // unchanged — the second run skipped it
        Assert.Contains(log.Messages, m => m.Contains("already signed", StringComparison.OrdinalIgnoreCase));
    }
}
