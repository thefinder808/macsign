using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class VerifyTests
{
    [Fact]
    public async Task Reports_a_valid_signature()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);

        var r = SignatureVerifier.Verify(dll);

        Assert.True(r.IsSigned);
        Assert.True(r.SignatureValid, r.Error);
        Assert.Contains("MacSign Test", r.SignerSubject!);
        Assert.Null(r.Timestamp);
        // Self-signed → not trusted on macOS, reported honestly rather than as a failure.
        Assert.False(r.ChainTrusted);
        Assert.NotNull(r.ChainNote);
    }

    [Fact]
    public void Reports_unsigned()
    {
        using var tmp = new TempDir();
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var r = SignatureVerifier.Verify(dll);

        Assert.False(r.IsSigned);
        Assert.False(r.SignatureValid);
    }

    [Fact]
    public async Task Detects_tampering()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);

        // Flip a byte in the hashed body (well before the appended cert table).
        var bytes = await File.ReadAllBytesAsync(dll);
        bytes[512] ^= 0xFF;
        await File.WriteAllBytesAsync(dll, bytes);

        var r = SignatureVerifier.Verify(dll);

        Assert.True(r.IsSigned);        // the signature blob is still present…
        Assert.False(r.SignatureValid); // …but the file no longer matches the signed digest
    }

    [Fact]
    public async Task Verifies_a_signed_ps1()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var script = Path.Combine(tmp.Path, "x.ps1");
        await File.WriteAllTextAsync(script, "Write-Host 'verify me'\n");
        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, script, options)).Success);

        var r = SignatureVerifier.Verify(script);

        Assert.True(r.IsSigned);
        Assert.True(r.SignatureValid, r.Error);
    }
}
