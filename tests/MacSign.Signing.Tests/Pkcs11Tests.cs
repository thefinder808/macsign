using System.Security.Cryptography.Pkcs;
using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Pkcs11;

namespace MacSign.Signing.Tests;

public class Pkcs11Tests
{
    [Fact]
    public void Pkcs11_mode_with_a_missing_module_fails_clearly()
    {
        Pkcs11Backend.Register();
        var signer = AuthenticodeSigner.TryCreate(
            new SigningOptions { CertMode = CertMode.Pkcs11, Pkcs11ModulePath = "/no/such/module.so" },
            out var error);

        Assert.Null(signer);
        Assert.Contains("module not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_a_PE_with_a_SoftHSM_token()
    {
        using var hsm = await SoftHsm.TryProvisionAsync();
        if (hsm is null)
            return; // softhsm2 / opensc not installed — skip

        Pkcs11Backend.Register();

        var dll = FixturePe.CopyToTemp(hsm.Dir);
        var options = new SigningOptions
        {
            CertMode = CertMode.Pkcs11,
            Pkcs11ModulePath = hsm.ModulePath,
            Secret = SoftHsm.Pin,
            Description = "MacSign HSM",
        };

        var signer = AuthenticodeSigner.TryCreate(options, out var error);
        Assert.NotNull(signer);
        Assert.Null(error);

        var result = await signer!.SignAsync(hsm.Dir, dll, options);
        Assert.True(result.Success, result.Error);

        // The signature (made with a key that never left the token) verifies.
        var pe = new PeFormat();
        Assert.True(pe.TryExtractSignature(await File.ReadAllBytesAsync(dll), out var pkcs7));
        var cms = new SignedCms();
        cms.Decode(pkcs7);
        cms.CheckSignature(verifySignatureOnly: true);
        Assert.Contains("MacSign Test", cms.SignerInfos[0].Certificate!.Subject);
    }
}
