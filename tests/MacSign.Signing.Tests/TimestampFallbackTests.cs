using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

public class TimestampFallbackTests
{
    private const string Tsa = "http://timestamp.digicert.com";

    [Fact]
    public async Task Timestamping_falls_back_to_the_next_server_when_the_first_is_unreachable()
    {
        if (!await Net.CanReachAsync(Tsa)) return; // offline — skip

        using var tmp = new TempDir();
        var pfx = TestCerts.CreatePfx(tmp.Path);
        var dll = FixturePe.CopyToTemp(tmp.Path);

        var options = new SigningOptions
        {
            CertMode = CertMode.Pfx,
            PfxPath = pfx,
            Secret = TestCerts.Password,
            // A comma-separated list: the first server is unreachable, so signing must fall
            // through to the second rather than failing the whole operation.
            TimestampUrl = "http://127.0.0.1:1/unreachable," + Tsa,
        };
        var signer = AuthenticodeSigner.TryCreate(options, out _)!;
        var result = await signer.SignAsync(tmp.Path, dll, options);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(SignatureVerifier.Verify(dll).Timestamp);
    }
}
