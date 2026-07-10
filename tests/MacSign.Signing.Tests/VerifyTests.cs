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

    // The signature block must be the file's tail. Code appended after
    // "# SIG # End signature block" is executed by PowerShell but sits outside the
    // hashed region and the extracted blob — so an unfixed verifier reports the
    // tampered script as VALID. The signed content must bind the whole file.
    [Fact]
    public async Task Rejects_ps1_with_code_appended_after_the_signature_block()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var script = Path.Combine(tmp.Path, "x.ps1");
        await File.WriteAllTextAsync(script, "Write-Host 'legit'\n");
        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, script, options)).Success);
        Assert.True(SignatureVerifier.Verify(script).SignatureValid); // baseline: the clean sign verifies

        // Inject PowerShell after the End marker — it runs, yet is not part of the signature.
        await File.AppendAllTextAsync(script, "Write-Host 'PWNED'\r\n");

        var r = SignatureVerifier.Verify(script);
        Assert.True(r.IsSigned);        // the real signature blob is still present…
        Assert.False(r.SignatureValid); // …but the trailing code isn't covered → not VALID
    }

    // Grafting a second, attacker-authored signature block after the real one is the
    // same class of attack (non-whitespace content follows the first End marker).
    [Fact]
    public async Task Rejects_ps1_with_a_second_signature_block_appended()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var script = Path.Combine(tmp.Path, "x.ps1");
        await File.WriteAllTextAsync(script, "Write-Host 'legit'\n");
        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, script, options)).Success);

        await File.AppendAllTextAsync(script,
            "# SIG # Begin signature block\r\n# QQ==\r\n# SIG # End signature block\r\n");

        var r = SignatureVerifier.Verify(script);
        Assert.True(r.IsSigned);
        Assert.False(r.SignatureValid);
    }

    // The block's own trailing line ending — and trailing blank lines, which are not
    // executable content — must NOT break a legitimate signature (no over-tightening).
    [Fact]
    public async Task Tolerates_trailing_whitespace_after_the_signature_block()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var script = Path.Combine(tmp.Path, "x.ps1");
        await File.WriteAllTextAsync(script, "Write-Host 'legit'\n");
        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, script, options)).Success);

        await File.AppendAllTextAsync(script, "\r\n\r\n   \r\n");

        Assert.True(SignatureVerifier.Verify(script).SignatureValid);
    }
}
