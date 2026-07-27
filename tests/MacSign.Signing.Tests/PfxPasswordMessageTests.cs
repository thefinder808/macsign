namespace MacSign.Signing.Tests;

/// <summary>
/// Defect 11 (PLAN-profile-fixes.md, Commit 4): a wrong or missing PFX password used to
/// surface as the raw platform <see cref="System.Security.Cryptography.CryptographicException"/>
/// text, which on Windows literally reads "The specified network password is not correct" —
/// meaningless in a code-signing context. These tests drive through the public path a user
/// actually hits (<see cref="AuthenticodeSigner.TryCreate"/> + <see cref="AuthenticodeSigner.SignAsync"/>)
/// rather than poking <c>PfxCredentialSigner</c> directly.
/// </summary>
public class PfxPasswordMessageTests
{
    [Fact]
    public async Task Wrong_password_reports_our_message_not_the_platform_one()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path, password: "correct-password");
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = "wrong-password" };
        var signer = AuthenticodeSigner.TryCreate(options, out var createError);
        Assert.NotNull(signer);
        Assert.Null(createError);

        var result = await signer!.SignAsync(tmp.Path, dll, options);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("password may be wrong", result.Error);
        Assert.DoesNotContain("network password", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_password_on_a_protected_pfx_points_at_the_missing_password()
    {
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path, password: "correct-password");
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = null };
        var signer = AuthenticodeSigner.TryCreate(options, out var createError);
        Assert.NotNull(signer);
        Assert.Null(createError);

        var result = await signer!.SignAsync(tmp.Path, dll, options);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("supply the password", result.Error);
        Assert.DoesNotContain("network password", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Password_less_pfx_still_signs()
    {
        // Guards against "simplifying" the fix back into a required-password check: an
        // unprotected PFX is a supported credential (nullable password, optional CLI
        // --password), and this must keep working.
        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path, password: string.Empty);
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = null };
        var signer = AuthenticodeSigner.TryCreate(options, out var createError);
        Assert.NotNull(signer);
        Assert.Null(createError);

        var result = await signer!.SignAsync(tmp.Path, dll, options);

        Assert.True(result.Success, result.Error);
    }
}
