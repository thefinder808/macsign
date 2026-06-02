using System.Text;

namespace MacSign.Signing.Tests;

public class HygieneTests
{
    [Fact]
    public async Task Password_never_appears_in_signed_output_or_logs()
    {
        using var tmp = new TempDir();
        const string password = "S3cr3t-Hunter2-passphrase";
        var pfx = TestCerts.CreatePfx(tmp.Path, password);
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;

        var log = new ListProgress();
        var result = await signer.SignAsync(tmp.Path, dll, options, log);
        Assert.True(result.Success, result.Error);

        var signed = await File.ReadAllBytesAsync(dll);
        Assert.False(Bytes.Contains(signed, Encoding.UTF8.GetBytes(password)),
            "the PFX password must never end up in the signed file");

        foreach (var message in log.Messages)
            Assert.DoesNotContain(password, message);
    }
}
