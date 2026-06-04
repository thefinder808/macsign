using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using MacSign.Signing;
using MacSign.Signing.Verification;
using Xunit;

namespace MacSign.App.Tests;

/// <summary>Engine fake that scripts the Windows sign and the post-sign verify, so the
/// Sign screen's "verified VALID after signing" claim can be exercised without real
/// crypto. (SignOneAsync/TryCreateSigner were made virtual for this seam; Verify already
/// was.)</summary>
internal sealed class FakeSignEngine : EngineService
{
    private readonly AuthenticodeSigner _signer;
    public Func<string, SignResult> SignResultFor = _ => SignResult.Ok();
    public Func<string, VerifyReport> VerifyFor = _ => new() { IsSigned = true, SignatureValid = true };
    public readonly List<string> Verified = new();

    public FakeSignEngine(AuthenticodeSigner signer) => _signer = signer;

    public override AuthenticodeSigner? TryCreateSigner(SigningOptions options, out string? error)
    {
        error = null;
        return _signer;
    }

    public override Task<SignResult> SignOneAsync(AuthenticodeSigner signer, string filePath,
        SigningOptions options, IProgress<string>? log, CancellationToken ct)
        => Task.FromResult(SignResultFor(filePath));

    public override VerifyReport Verify(string filePath)
    {
        Verified.Add(filePath);
        return VerifyFor(filePath);
    }
}

public class SignViewModelTests
{
    // A real throwaway self-signed signer: the fake's SignOneAsync ignores it, but the VM
    // needs a non-null signer to enter the loop and AuthenticodeSigner's ctor is private.
    private static AuthenticodeSigner ThrowawaySigner()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=MacSign Post-Sign Test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pfx = Path.Combine(Path.GetTempPath(), "macsign-postsign-" + Guid.NewGuid().ToString("N") + ".pfx");
        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, "pw"));
        return AuthenticodeSigner.TryCreate(
            new SigningOptions { CertMode = CertMode.Pfx, PfxPath = pfx, Secret = "pw" }, out var err)
            ?? throw new InvalidOperationException(err);
    }

    private static (SignViewModel Vm, FakeSignEngine Engine, FileItemViewModel Row) Setup()
    {
        var engine = new FakeSignEngine(ThrowawaySigner());
        var vm = new SignViewModel(engine) { PfxPath = "/tmp/cred.pfx" };
        var row = new FileItemViewModel("/tmp/postsign-a.dll", isSigned: false, sizeBytes: 1024);
        vm.Files.Add(row);
        return (vm, engine, row);
    }

    [Fact]
    public async Task Sign_then_verify_pass_marks_done_with_the_verified_banner()
    {
        var (vm, engine, row) = Setup();
        engine.SignResultFor = _ => SignResult.Ok();
        engine.VerifyFor = _ => new() { IsSigned = true, SignatureValid = true };

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(FileRunState.Done, row.RunState);
        Assert.False(vm.BannerIsError);
        Assert.Equal("Signed and verified VALID after signing.", vm.BannerDetail);
        Assert.Contains("/tmp/postsign-a.dll", engine.Verified);   // it really re-verified
    }

    [Fact]
    public async Task Sign_succeeds_but_verify_fails_does_not_mark_done()
    {
        var (vm, engine, row) = Setup();
        engine.SignResultFor = _ => SignResult.Ok();
        engine.VerifyFor = _ => new() { IsSigned = true, SignatureValid = false }; // signed but NOT valid

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(FileRunState.None, row.RunState);             // NOT reported as done
        Assert.True(vm.BannerIsError);
        Assert.Contains("did not verify", vm.BannerDetail);
    }

    [Fact]
    public async Task Sign_failure_skips_verify_and_keeps_existing_behavior()
    {
        var (vm, engine, row) = Setup();
        engine.SignResultFor = _ => SignResult.Fail("signing blew up");

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(FileRunState.None, row.RunState);
        Assert.True(vm.BannerIsError);
        Assert.Equal("signing blew up", vm.BannerDetail);
        Assert.Empty(engine.Verified);                            // verify never attempted
    }
}
