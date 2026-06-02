using System.Security.Cryptography.Pkcs;
using System.Text;
using MacSign.Signing.Formats.Ps1;

namespace MacSign.Signing.Tests;

public class Ps1Tests
{
    private const string Content = "Write-Host 'hello from MacSign'\n";

    // SHA-256 over UTF-16LE(content), captured from osslsigncode 2.13 (which matches Windows).
    private const string KnownDigest = "ca39bcf4a2eba244bc64496ce64d59ad1673c08e871c1b8976b4e015a34b04b9";

    [Fact]
    public void Digest_matches_the_osslsigncode_reference()
    {
        var digest = new Ps1Format().ComputeDigest(Encoding.UTF8.GetBytes(Content));
        Assert.Equal(KnownDigest, Convert.ToHexString(digest).ToLowerInvariant());
    }

    [Fact]
    public async Task Sign_a_ps1_then_extract_and_verify()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var script = Path.Combine(tmp.Path, "install.ps1");
        await File.WriteAllTextAsync(script, Content); // UTF-8, no BOM
        var original = await File.ReadAllBytesAsync(script);

        var options = new SigningOptions
        {
            CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password, Description = "MacSign",
        };
        var signer = AuthenticodeSigner.TryCreate(options, out var err);
        Assert.NotNull(signer);
        Assert.Null(err);

        Assert.True((await signer!.SignAsync(tmp.Path, script, options)).Success);

        var signed = await File.ReadAllBytesAsync(script);
        Assert.Equal(original, signed[..original.Length]); // content above the block is untouched

        var ps1 = new Ps1Format();
        Assert.True(ps1.TryExtractSignature(signed, out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        cms.CheckSignature(verifySignatureOnly: true);
        Assert.Contains("MacSign Test", cms.SignerInfos[0].Certificate!.Subject);

        var freshSpc = ps1.BuildSpcIndirectData(ps1.ComputeDigest(original));
        Assert.Equal(freshSpc, cms.ContentInfo.Content);
    }

    [Fact]
    public async Task Re_signing_a_ps1_skips()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var script = Path.Combine(tmp.Path, "x.ps1");
        await File.WriteAllTextAsync(script, Content);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;

        Assert.True((await signer.SignAsync(tmp.Path, script, options)).Success);
        var afterFirst = await File.ReadAllBytesAsync(script);

        Assert.True((await signer.SignAsync(tmp.Path, script, options)).Success);
        Assert.Equal(afterFirst, await File.ReadAllBytesAsync(script));
    }
}
