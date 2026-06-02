using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class EngineBugTests
{
    [Fact]
    public void Checksum_ignores_the_checksum_field_bytes_even_at_an_odd_offset()
    {
        // Two images identical except for the 4 CheckSum bytes. Compute must treat that field
        // as zero regardless of the offset's parity, so both yield the same checksum.
        var a = new byte[64];
        var b = (byte[])a.Clone();
        const int oddOffset = 13;
        for (int i = oddOffset; i < oddOffset + 4; i++) b[i] = 0xFF;

        Assert.Equal(PeChecksum.Compute(a, oddOffset), PeChecksum.Compute(b, oddOffset));
    }

    [Fact]
    public async Task SignAll_signs_the_good_files_and_reports_the_failed_one()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var good = FixturePe.CopyToTemp(tmp.Path);
        var bad = Path.Combine(tmp.Path, "bad.dll");
        await File.WriteAllBytesAsync(bad, new byte[] { 1, 2, 3, 4 }); // signable extension, not a PE

        var options = new SigningOptions
        {
            CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password, SignAllSignableFiles = true,
        };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        var result = await signer.SignAsync(tmp.Path, tmp.Path, options);

        Assert.True(SignatureVerifier.Verify(good).SignatureValid); // the good file got signed…
        Assert.False(result.Success);                               // …and the batch reports a failure…
        Assert.Contains("bad.dll", result.Error!);                  // …naming the file that failed.
    }

    [Fact]
    public async Task Signing_preserves_the_original_file_permissions()
    {
        if (OperatingSystem.IsWindows()) return;

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead; // 0640
        File.SetUnixFileMode(dll, mode);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = TestCerts.Password };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        Assert.True((await signer.SignAsync(tmp.Path, dll, options)).Success);

        Assert.Equal(mode, File.GetUnixFileMode(dll));
    }
}
