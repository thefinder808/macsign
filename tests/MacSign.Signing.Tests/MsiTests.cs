using MacSign.Signing.Msi;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class MsiTests
{
    private static string FixtureMsi =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "test.msi");

    [Fact]
    public async Task Sign_an_msi_then_verify()
    {
        MsiBackend.Register();

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var msi = Path.Combine(tmp.Path, "test.msi");
        File.Copy(FixtureMsi, msi);
        var originalSize = new FileInfo(msi).Length;

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password, Description = "MacSign" };
        var signer = AuthenticodeSigner.TryCreate(options, out var error);
        Assert.NotNull(signer);
        Assert.Null(error);

        var result = await signer!.SignAsync(tmp.Path, msi, options);
        Assert.True(result.Success, result.Error);

        // The signature embeds + verifies (integrity), and the file is still a compound file.
        var report = SignatureVerifier.Verify(msi);
        Assert.True(report.IsSigned);
        Assert.True(report.SignatureValid, report.Error);
        Assert.Contains("MacSign Test", report.SignerSubject!);
        Assert.True(new FileInfo(msi).Length > originalSize); // grew by the DigitalSignature stream
    }

    [Fact]
    public async Task Re_signing_an_msi_skips()
    {
        MsiBackend.Register();

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var msi = Path.Combine(tmp.Path, "test.msi");
        File.Copy(FixtureMsi, msi);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;

        Assert.True((await signer.SignAsync(tmp.Path, msi, options)).Success);
        var afterFirst = await File.ReadAllBytesAsync(msi);

        Assert.True((await signer.SignAsync(tmp.Path, msi, options)).Success);
        Assert.Equal(afterFirst, await File.ReadAllBytesAsync(msi)); // unchanged — skipped
    }
}
