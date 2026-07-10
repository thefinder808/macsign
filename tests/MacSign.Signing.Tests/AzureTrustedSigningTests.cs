using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MacSign.Signing.Azure;
using MacSign.Signing.Cms;
using MacSign.Signing.Credentials;
using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Verification;

namespace MacSign.Signing.Tests;

/// <summary>
/// Proves the Azure Trusted Signing backend offline — no Azure account needed. The
/// network boundary is stubbed; everything else (REST client, delegating RSA, CMS
/// assembly) is the real production code. The headline test shows the delegated path is
/// byte-for-byte identical to the in-proc path, which is already signtool-proven in CI.
/// </summary>
public class AzureTrustedSigningTests
{
    private const string Endpoint = "https://eus.codesigning.azure.net";
    private const string Account = "acct";
    private const string Profile = "profile";

    // ── Transport: request shape + long-running-operation poll ─────────────────

    [Fact]
    public async Task Posts_the_digest_then_polls_and_returns_the_signature()
    {
        using var key = RSA.Create(2048);
        string? postUri = null, postAuth = null, postBody = null;
        bool polled = false;

        var handler = new StubHandler(async req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                postUri = req.RequestUri!.AbsoluteUri;
                postAuth = req.Headers.GetValues("Authorization").Single();
                postBody = await req.Content!.ReadAsStringAsync();
                // Accepted → long-running operation in progress.
                return Json(HttpStatusCode.Accepted, new { operationId = "op-123", status = "InProgress" });
            }

            polled = true;
            var digest = Convert.FromBase64String(JsonDocument.Parse(postBody!).RootElement.GetProperty("digest").GetString()!);
            var sig = key.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Json(HttpStatusCode.OK, new { operationId = "op-123", status = "Succeeded", signature = Convert.ToBase64String(sig) });
        });

        using var client = new TrustedSigningClient(
            Endpoint + "/", Account, Profile, new FakeToken("tok-abc"), handler, pollInterval: TimeSpan.Zero);

        var digest = SHA256.HashData([1, 2, 3]);
        var result = await client.SignDigestAsync(digest, "RS256", default);

        // Request shape.
        Assert.Contains("/codesigningaccounts/acct/certificateprofiles/profile/sign", postUri);
        Assert.Contains("api-version=2022-06-15-preview", postUri);
        Assert.Equal("Bearer tok-abc", postAuth);
        using var doc = JsonDocument.Parse(postBody!);
        Assert.Equal("RS256", doc.RootElement.GetProperty("signatureAlgorithm").GetString());
        Assert.Equal(Convert.ToBase64String(digest), doc.RootElement.GetProperty("digest").GetString());

        // It polled the operation, and the returned signature is genuine.
        Assert.True(polled);
        Assert.True(key.VerifyHash(digest, result.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task Surfaces_the_rbac_role_hint_on_403()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("AuthorizationFailed"),
        }));
        using var client = new TrustedSigningClient(Endpoint, Account, Profile, new FakeToken("t"), handler, TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.SignDigestAsync(SHA256.HashData([1]), "RS256", default));

        Assert.Contains("Artifact Signing Certificate Profile Signer", ex.Message);
        Assert.Contains("2837e146-70d7-4cfd-ad55-7efa6464f958", ex.Message);
    }

    // ── The contract: delegated path == in-proc path (signtool-proven) ─────────

    [Fact]
    public async Task Delegated_path_is_byte_identical_to_the_in_proc_path()
    {
        using var key = RSA.Create(2048);
        using var certWithKey = SelfSignedCodeSigningCert(key, out var signingCert);

        // Fake Trusted Signing endpoint: sign the posted digest with the local key and
        // return the public cert as the chain. POST resolves synchronously (Succeeded).
        var handler = LocalKeyEndpoint(key, signingCert);

        var options = new SigningOptions
        {
            CertMode = CertMode.TrustedSigning,
            TrustedSigningEndpoint = Endpoint,
            TrustedSigningAccount = Account,
            TrustedSigningProfile = Profile,
            Description = "MacSign",
            Url = "https://example.com",
        };

        var fileBytes = FixturePe.UnsignedBytes();
        var format = new PeFormat();
        var spc = format.BuildSpcIndirectData(format.ComputeDigest(fileBytes));

        // Delegated (Azure) path.
        using var azure = new AzureTrustedSigner(Endpoint, Account, Profile, new FakeToken("tok"), handler);
        var azureCms = await new AuthenticodeCmsBuilder().BuildAsync(spc, azure, options, default);

        // In-proc path with the SAME key + cert.
        using var inProc = new InProcCredential(certWithKey);
        var inProcCms = await new AuthenticodeCmsBuilder().BuildAsync(spc, inProc, options, default);

        // Both are valid Authenticode CMS blobs…
        foreach (var pkcs7 in new[] { azureCms, inProcCms })
        {
            var cms = new SignedCms();
            cms.Decode(pkcs7);
            cms.CheckSignature(verifySignatureOnly: true);
            Assert.Contains("MacSign Azure Test", cms.SignerInfos[0].Certificate!.Subject);
        }

        // …and byte-for-byte equal. Same signed attributes + deterministic RSA PKCS#1,
        // no timestamp → identical bytes. The in-proc path is signtool-proven, so the
        // delegated path inherits that proof.
        Assert.Equal(inProcCms, azureCms);
    }

    [Fact]
    public async Task Signs_a_PE_end_to_end_through_the_trusted_signing_certmode()
    {
        using var key = RSA.Create(2048);
        using var certWithKey = SelfSignedCodeSigningCert(key, out var signingCert);
        var handler = LocalKeyEndpoint(key, signingCert);

        // Register a factory that builds the real AzureTrustedSigner over the fake endpoint.
        CredentialBackends.TrustedSigningFactory = opts =>
            new AzureTrustedSigner(opts.TrustedSigningEndpoint!, opts.TrustedSigningAccount!,
                opts.TrustedSigningProfile!, new FakeToken("tok"), handler);

        using var tmp = new TempDir();
        var dll = FixturePe.CopyToTemp(tmp.Path);
        var options = new SigningOptions
        {
            CertMode = CertMode.TrustedSigning,
            TrustedSigningEndpoint = Endpoint,
            TrustedSigningAccount = Account,
            TrustedSigningProfile = Profile,
            Description = "MacSign Azure",
        };

        var signer = AuthenticodeSigner.TryCreate(options, out var error);
        Assert.NotNull(signer);
        Assert.Null(error);

        Assert.True((await signer!.SignAsync(tmp.Path, dll, options)).Success);

        var r = SignatureVerifier.Verify(dll);
        Assert.True(r.IsSigned);
        Assert.True(r.SignatureValid, r.Error);
        Assert.Contains("MacSign Azure Test", r.SignerSubject!);
    }

    // A malicious/redirecting response must not steer the token-bearing poll off-host.
    [Fact]
    public async Task Does_not_follow_a_cross_host_poll_url_from_the_response()
    {
        using var key = RSA.Create(2048);
        var polledHosts = new List<string>();
        string? postBody = null;

        var handler = new StubHandler(async req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                postBody = await req.Content!.ReadAsStringAsync();
                var resp = Json(HttpStatusCode.Accepted, new { operationId = "op-1", status = "InProgress" });
                resp.Headers.TryAddWithoutValidation("Operation-Location", "https://evil.example.com/steal-token");
                return resp;
            }
            polledHosts.Add(req.RequestUri!.Host);
            var digest = Convert.FromBase64String(JsonDocument.Parse(postBody!).RootElement.GetProperty("digest").GetString()!);
            var sig = key.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Json(HttpStatusCode.OK, new { status = "Succeeded", signature = Convert.ToBase64String(sig) });
        });

        using var client = new TrustedSigningClient(
            Endpoint, Account, Profile, new FakeToken("tok"), handler, pollInterval: TimeSpan.Zero);

        await client.SignDigestAsync(SHA256.HashData([1, 2, 3]), "RS256", default);

        // The poll fell back to the same-host operationId URL; the attacker host was never contacted.
        Assert.NotEmpty(polledHosts);
        Assert.All(polledHosts, h => Assert.Equal("eus.codesigning.azure.net", h));
        Assert.DoesNotContain("evil.example.com", polledHosts);
    }

    // A hostile/compromised endpoint can't exhaust memory: the response body is capped.
    [Fact]
    public async Task Caps_the_response_body_size()
    {
        using var key = RSA.Create(2048);
        string? postBody = null;

        var handler = new StubHandler(async req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                postBody = await req.Content!.ReadAsStringAsync();
                return Json(HttpStatusCode.Accepted, new { operationId = "op", status = "InProgress" });
            }
            // A valid, complete Succeeded response — but padded well past the 1 MB cap, so it
            // would succeed without the cap and must now be rejected on read.
            var digest = Convert.FromBase64String(JsonDocument.Parse(postBody!).RootElement.GetProperty("digest").GetString()!);
            var sig = key.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var json = JsonSerializer.Serialize(new
            {
                status = "Succeeded",
                signature = Convert.ToBase64String(sig),
                pad = new string('x', 2 * 1024 * 1024),
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        using var client = new TrustedSigningClient(
            Endpoint, Account, Profile, new FakeToken("t"), handler, pollInterval: TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.SignDigestAsync(SHA256.HashData([1, 2, 3]), "RS256", default));
    }

    [Fact]
    public void TryCreate_reports_missing_trusted_signing_options()
    {
        AzureBackend.Register();
        var signer = AuthenticodeSigner.TryCreate(
            new SigningOptions { CertMode = CertMode.TrustedSigning, TrustedSigningEndpoint = Endpoint },
            out var error);

        Assert.Null(signer);
        Assert.Contains("--trusted-signing-account", error);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static X509Certificate2 SelfSignedCodeSigningCert(RSA key, out string signingCertificate)
    {
        var req = new CertificateRequest("CN=MacSign Azure Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") }, true)); // id-kp-codeSigning

        var now = DateTimeOffset.UtcNow;
        var cert = req.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(2));

        // Mirror the live Trusted Signing wire format: base64( base64( DER PKCS#7 chain ) ).
        var pkcs7 = new X509Certificate2Collection(cert).Export(X509ContentType.Pkcs7)!;
        signingCertificate = Convert.ToBase64String(Encoding.ASCII.GetBytes(Convert.ToBase64String(pkcs7)));
        return cert;
    }

    /// <summary>A fake sign endpoint that signs the posted digest with a local key.</summary>
    private static StubHandler LocalKeyEndpoint(RSA key, string signingCertificate) => new(async req =>
    {
        var body = await req.Content!.ReadAsStringAsync();
        var digest = Convert.FromBase64String(JsonDocument.Parse(body).RootElement.GetProperty("digest").GetString()!);
        var sig = key.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Json(HttpStatusCode.OK, new
        {
            status = "Succeeded",
            signature = Convert.ToBase64String(sig),
            signingCertificate,
        });
    });

    private static HttpResponseMessage Json(HttpStatusCode code, object body) => new(code)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}

/// <summary>Routes every request through a caller-supplied responder.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => responder(request);
}

/// <summary>A token provider that returns a fixed token (no Azure round-trip).</summary>
internal sealed class FakeToken(string token) : IAzureTokenProvider
{
    public Task<string> GetTokenAsync(CancellationToken ct) => Task.FromResult(token);
}

/// <summary>An in-process credential over a cert that holds its own RSA private key.</summary>
internal sealed class InProcCredential : ICredentialSigner
{
    public InProcCredential(X509Certificate2 certWithKey)
    {
        Certificate = certWithKey;
        SigningKey = certWithKey.GetRSAPrivateKey()!;
    }

    public X509Certificate2 Certificate { get; }
    public AsymmetricAlgorithm SigningKey { get; }
    public IReadOnlyList<X509Certificate2> Chain => [];

    public void Dispose() => SigningKey.Dispose();
}
