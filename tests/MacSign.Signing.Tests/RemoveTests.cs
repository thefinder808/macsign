using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class RemoveTests
{
    [Fact]
    public async Task Remove_returns_a_signed_pe_to_unsigned()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);
        byte[] originalDigest = new PeFormat().ComputeDigest(await File.ReadAllBytesAsync(dll));

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);
        Assert.True(SignatureVerifier.Verify(dll).IsSigned);

        Assert.True(SignatureRemover.Remove(dll));

        Assert.False(SignatureVerifier.Verify(dll).IsSigned);
        // The stripped image hashes identically to the original unsigned image.
        Assert.Equal(
            Convert.ToHexString(originalDigest),
            Convert.ToHexString(new PeFormat().ComputeDigest(await File.ReadAllBytesAsync(dll))));
    }

    [Fact]
    public async Task Remove_returns_a_signed_ps1_to_unsigned()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var ps1 = Path.Combine(tmp.Path, "x.ps1");
        await File.WriteAllTextAsync(ps1, "Write-Host 'hi'\n");

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, ps1, options)).Success);
        Assert.True(SignatureVerifier.Verify(ps1).IsSigned);

        Assert.True(SignatureRemover.Remove(ps1));

        Assert.False(SignatureVerifier.Verify(ps1).IsSigned);
        Assert.Equal("Write-Host 'hi'\n", await File.ReadAllTextAsync(ps1));
    }

    [Fact]
    public async Task Remove_returns_a_signed_msi_to_unsigned()
    {
        MacSign.Signing.Msi.MsiBackend.Register();
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var msi = Path.Combine(tmp.Path, "test.msi");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "test.msi"), msi);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, msi, options)).Success);
        Assert.True(SignatureVerifier.Verify(msi).IsSigned);

        Assert.True(SignatureRemover.Remove(msi));

        Assert.False(SignatureVerifier.Verify(msi).IsSigned);
    }

    [Fact]
    public void Remove_on_an_unsigned_file_is_a_no_op()
    {
        using var tmp = new TempDir();
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var before = File.ReadAllBytes(dll);

        Assert.False(SignatureRemover.Remove(dll));
        Assert.Equal(before, File.ReadAllBytes(dll));
    }
}
