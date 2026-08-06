using Azure.Core;
using Azure.Identity;
using MacSign.Signing.Azure;

namespace MacSign.Signing.Tests;

/// <summary>
/// Which Microsoft Entra identity signs. These assert the *decision* — the options objects
/// handed to Azure.Identity — instead of constructing live credentials, deliberately: CI runs
/// on a macOS runner, and a real <c>InteractiveBrowserCredential</c> backed by a persistent
/// cache reaches for that machine's keychain. Building an options object touches nothing.
/// </summary>
public class AzureCredentialSelectionTests
{
    // ── The default chain ──────────────────────────────────────────────────────

    [Fact]
    public void Default_source_passes_the_tenant_id_to_the_default_chain()
    {
        // A domain, not a GUID — both are valid tenant identifiers, so this must not be
        // GUID-validated anywhere along the way.
        var opts = AzureCredentialFactory.BuildDefaultOptions(
            new SigningOptions { TrustedSigningTenantId = "contoso.onmicrosoft.com" });

        Assert.Equal("contoso.onmicrosoft.com", opts.TenantId);
    }

    [Fact]
    public void Default_source_still_cannot_open_a_browser()
    {
        // The whole safety story: signing never prompts. Azure.Identity already defaults this
        // to true, but it is set explicitly so a future default change can't quietly introduce
        // a browser popup into the middle of a signing batch.
        var opts = AzureCredentialFactory.BuildDefaultOptions(new SigningOptions());

        Assert.True(opts.ExcludeInteractiveBrowserCredential);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_tenant_id_is_treated_as_unset(string? blank)
    {
        // Handing Azure.Identity an empty string is not the same as handing it nothing —
        // null means "use the credential's own default tenant".
        var opts = AzureCredentialFactory.BuildDefaultOptions(
            new SigningOptions { TrustedSigningTenantId = blank });

        Assert.Null(opts.TenantId);
    }

    [Fact]
    public void A_padded_tenant_id_is_trimmed()
    {
        var opts = AzureCredentialFactory.BuildDefaultOptions(
            new SigningOptions { TrustedSigningTenantId = "  11111111-2222-3333-4444-555555555555\n" });

        Assert.Equal("11111111-2222-3333-4444-555555555555", opts.TenantId);
    }

    // ── The browser sign-in ────────────────────────────────────────────────────

    [Fact]
    public void Interactive_source_never_prompts_while_signing()
    {
        // The load-bearing assertion. AuthenticodeSigner.SignAsync builds a credential per
        // file, so a credential that may prompt would open one browser window PER FILE on a
        // cold cache. With this set, a cache miss throws AuthenticationRequiredException and
        // the run fails once, with something the user can act on.
        var opts = AzureCredentialFactory.BuildInteractiveOptions(new SigningOptions());

        Assert.True(opts.DisableAutomaticAuthentication);
    }

    [Fact]
    public void Interactive_source_names_its_own_token_cache()
    {
        // An unnamed cache uses a shared default name, which would mean reading and writing
        // some other Azure tool's MSAL cache on the same machine.
        var opts = AzureCredentialFactory.BuildInteractiveOptions(new SigningOptions());

        Assert.Equal("macsign", opts.TokenCachePersistenceOptions?.Name);
    }

    [Fact]
    public void Interactive_source_never_falls_back_to_unencrypted_storage()
    {
        // Unencrypted storage writes refresh tokens to a plaintext file — straight through
        // this repo's "never persists secrets" invariant. Degrading to no persistence is the
        // only acceptable fallback.
        var opts = AzureCredentialFactory.BuildInteractiveOptions(new SigningOptions());

        Assert.False(opts.TokenCachePersistenceOptions?.UnsafeAllowUnencryptedStorage);
    }

    [Fact]
    public void Interactive_source_replays_the_recorded_account()
    {
        var opts = AzureCredentialFactory.BuildInteractiveOptions(
            new SigningOptions { TrustedSigningAuthRecord = Record("someone@contoso.com", "tenant-a") });

        Assert.Equal("someone@contoso.com", opts.AuthenticationRecord?.Username);
    }

    [Fact]
    public void A_corrupt_sign_in_record_is_ignored_rather_than_thrown()
    {
        // settings.json is user-editable and survives app upgrades. A mangled record must
        // degrade to "not signed in", never crash the Sign screen on startup.
        var opts = AzureCredentialFactory.BuildInteractiveOptions(
            new SigningOptions { TrustedSigningAuthRecord = "{not json" });

        Assert.Null(opts.AuthenticationRecord);
    }

    // ── The persisted shape ────────────────────────────────────────────────────

    [Fact]
    public void An_authentication_record_round_trips_through_its_persisted_fields()
    {
        // We store the five named fields in settings.json rather than an opaque blob, so a
        // reviewer can see at a glance that no token is persisted. That means depending on
        // AuthenticationRecord's serialized shape — this test fails loudly if it ever changes.
        var record = AzureCredentialFactory.ReadRecord(Record("someone@contoso.com", "tenant-a"));

        Assert.NotNull(record);
        Assert.Equal("someone@contoso.com", record!.Username);
        Assert.Equal("tenant-a", record.TenantId);
        Assert.Equal("home-account-id", record.HomeAccountId);
        Assert.Equal("client-id", record.ClientId);
        Assert.Equal("https://login.microsoftonline.com/tenant-a", record.Authority);
    }

    // ── Credential reuse ───────────────────────────────────────────────────────

    [Fact]
    public void The_same_configuration_reuses_one_credential()
    {
        // AuthenticodeSigner builds a credential per FILE and disposes it, so a 50-file run
        // constructed 50 of them — and on the default path each one shells out to `az` twice
        // (the cert-discovery probe, then the real sign), i.e. ~100 subprocesses for one run.
        // What's cached is the TokenCredential, never the ICredentialSigner: that one IS
        // disposed after each file, so caching it would blow up on file two.
        var options = new SigningOptions { TrustedSigningTenantId = "cache-reuse-tenant" };

        Assert.Same(AzureCredentialFactory.Create(options), AzureCredentialFactory.Create(options));
    }

    [Fact]
    public void A_different_tenant_never_shares_a_credential()
    {
        var a = AzureCredentialFactory.Create(new SigningOptions { TrustedSigningTenantId = "cache-tenant-a" });
        var b = AzureCredentialFactory.Create(new SigningOptions { TrustedSigningTenantId = "cache-tenant-b" });

        Assert.NotSame(a, b);
    }

    [Fact]
    public void The_cache_key_separates_every_field_that_picks_an_identity()
    {
        // Asserted on the key rather than by building credentials, so the interactive arm is
        // covered without an InteractiveBrowserCredential reaching for the runner's keychain.
        var baseline = new SigningOptions { TrustedSigningTenantId = "t", TrustedSigningAuthRecord = "r" };
        var key = AzureCredentialFactory.KeyFor(baseline);

        Assert.Equal(key, AzureCredentialFactory.KeyFor(baseline with { Description = "irrelevant" }));
        Assert.NotEqual(key, AzureCredentialFactory.KeyFor(baseline with { TrustedSigningTenantId = "other" }));
        Assert.NotEqual(key, AzureCredentialFactory.KeyFor(baseline with { TrustedSigningAuthRecord = "other" }));
        Assert.NotEqual(key, AzureCredentialFactory.KeyFor(
            baseline with { TrustedSigningCredentialSource = TrustedSigningCredentialSource.InteractiveBrowser }));
    }

    [Fact]
    public void A_blank_tenant_and_an_absent_one_are_the_same_entry()
    {
        // The key has to normalise the way BuildDefaultOptions does, or "  " and null become
        // two cache entries for one credential — a silent halving of the benefit.
        Assert.Equal(
            AzureCredentialFactory.KeyFor(new SigningOptions { TrustedSigningTenantId = "   " }),
            AzureCredentialFactory.KeyFor(new SigningOptions()));
    }

    // ── The explicit sign-in ───────────────────────────────────────────────────

    [Fact]
    public void Signing_in_is_the_one_place_a_browser_may_open()
    {
        // The exact inverse of Interactive_source_never_prompts_while_signing. Automatic
        // authentication stays on here because this path only runs from a user gesture.
        var opts = AzureSignIn.BuildOptions(tenantId: null);

        Assert.False(opts.DisableAutomaticAuthentication);
    }

    [Fact]
    public void Signing_in_writes_to_the_same_cache_signing_reads()
    {
        // If these two names ever diverge, sign-in appears to succeed and then every
        // subsequent sign reports "not signed in" — with nothing on screen to explain why.
        Assert.Equal(
            AzureCredentialFactory.BuildInteractiveOptions(new SigningOptions()).TokenCachePersistenceOptions?.Name,
            AzureSignIn.BuildOptions(tenantId: null).TokenCachePersistenceOptions?.Name);
    }

    [Fact]
    public void Signing_in_targets_the_requested_tenant()
    {
        Assert.Equal("contoso.onmicrosoft.com", AzureSignIn.BuildOptions("  contoso.onmicrosoft.com ").TenantId);
    }

    [Fact]
    public void A_sign_in_result_persists_account_fields_and_nothing_token_shaped()
    {
        // The reason we can store this in settings.json at all. The repo's invariant is that
        // persisted data holds no secrets *by construction*, so assert the shape rather than
        // trusting Azure.Identity to keep it that way.
        var record = AzureCredentialFactory.ReadRecord(Record("someone@contoso.com", "tenant-a"))!;

        var result = AzureSignIn.Describe(record);

        Assert.Equal("someone@contoso.com", result.Username);
        Assert.Equal("tenant-a", result.TenantId);

        using var doc = System.Text.Json.JsonDocument.Parse(result.SerializedRecord);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("username", keys);
        Assert.Contains("tenantId", keys);
        Assert.All(keys, k => Assert.DoesNotContain("token", k, StringComparison.OrdinalIgnoreCase));
        Assert.All(keys, k => Assert.DoesNotContain("secret", k, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_serialized_record_uses_the_field_names_the_app_persists()
    {
        // The GUI stores these five by name (AzureSignInData) instead of an opaque blob, so
        // that settings.json stays reviewable. That makes the field names a contract: if
        // Azure.Identity renames one, a remembered sign-in would silently stop replaying and
        // the user would be asked to sign in again forever. Fail here instead.
        var record = AzureCredentialFactory.ReadRecord(Record("someone@contoso.com", "tenant-a"))!;

        using var doc = System.Text.Json.JsonDocument.Parse(AzureSignIn.Describe(record).SerializedRecord);

        foreach (var name in new[] { "username", "authority", "homeAccountId", "tenantId", "clientId" })
            Assert.True(doc.RootElement.TryGetProperty(name, out _), $"missing field: {name}");
    }

    // ── Pre-flight identity check ──────────────────────────────────────────────

    [Fact]
    public async Task Describing_the_current_identity_costs_a_token_not_a_signature()
    {
        // Deliberately does NOT construct a credential signer: that would run the
        // certificate-discovery probe, which is a real Trusted Signing sign operation against
        // the account's quota. Answering "who would sign?" only needs a token.
        var identity = await AzureIdentity.DescribeAsync(
            new FakeToken(Jwt("checker@contoso.com", "tenant-a")), default);

        Assert.Equal("checker@contoso.com (tenant tenant-a)", identity);
    }

    [Fact]
    public async Task An_unreadable_token_describes_nothing_rather_than_throwing()
    {
        Assert.Null(await AzureIdentity.DescribeAsync(new FakeToken("not-a-jwt"), default));
    }

    [Fact]
    public async Task The_public_overload_reports_the_identity_of_a_supplied_token()
    {
        // Covers the PUBLIC entry point — the only one that reaches AzureCredentialFactory and
        // so the only one that could ever prompt. An explicit token short-circuits before a
        // credential is built, so this exercises it without touching the runner's keychain.
        var identity = await AzureIdentity.DescribeAsync(
            new SigningOptions { TrustedSigningAccessToken = Jwt("supplied@contoso.com", "tenant-a") });

        Assert.Equal("supplied@contoso.com (tenant tenant-a)", identity);
    }

    private static string Jwt(string username, string tenant)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { preferred_username = username, tid = tenant });
        var b64 = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"eyJhbGciOiJub25lIn0.{b64}.not-a-signature";
    }

    // ── Token acquisition ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_explicit_token_wins_over_any_credential_source()
    {
        // Long-standing behaviour worth pinning: --trusted-signing-token is the escape hatch
        // that must keep working no matter how the credential is configured. The credential
        // here throws if touched, so this also proves we never even build one.
        var provider = new DefaultAzureTokenProvider(
            new SigningOptions
            {
                TrustedSigningAccessToken = "  handed-in-token  ",
                TrustedSigningCredentialSource = TrustedSigningCredentialSource.InteractiveBrowser,
            },
            new ThrowingCredential(new InvalidOperationException("must not be consulted")));

        Assert.Equal("handed-in-token", await provider.GetTokenAsync(default));
    }

    [Fact]
    public async Task An_expired_browser_sign_in_asks_the_user_to_sign_in_again()
    {
        // Signing never prompts, so a cold or cleared cache arrives here as
        // AuthenticationRequiredException. It must not be swallowed by the generic handler,
        // whose advice ("run az login") is wrong for a browser-selected account.
        var provider = new DefaultAzureTokenProvider(
            new SigningOptions { TrustedSigningCredentialSource = TrustedSigningCredentialSource.InteractiveBrowser },
            new ThrowingCredential(new AuthenticationRequiredException("cache miss", new TokenRequestContext([]))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(default));

        Assert.Contains("sign in", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("az login", ex.Message);
    }

    [Fact]
    public async Task A_timeout_reads_as_a_failure_not_as_a_cancellation()
    {
        // HttpClient throws TaskCanceledException — a SUBCLASS of OperationCanceledException —
        // when its own 30s timeout elapses, with the caller's token never signalled. Treating
        // that as "the user cancelled" loses this actionable message, and higher up abandons
        // the rest of a batch under a banner naming a cancellation that never happened.
        var provider = new DefaultAzureTokenProvider(
            new SigningOptions(), new ThrowingCredential(new TaskCanceledException("timed out")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(default));

        Assert.Contains("az login", ex.Message);
    }

    [Fact]
    public async Task A_real_cancellation_still_propagates_untouched()
    {
        // The other half: once the caller's token HAS fired, the cancellation must stay a
        // cancellation so the layers above report it as one rather than as a sign-in failure.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = new DefaultAzureTokenProvider(
            new SigningOptions(), new ThrowingCredential(new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetTokenAsync(cts.Token));
    }

    [Fact]
    public async Task A_default_chain_failure_still_points_at_az_login()
    {
        var provider = new DefaultAzureTokenProvider(
            new SigningOptions(),
            new ThrowingCredential(new InvalidOperationException("no credential answered")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(default));

        Assert.Contains("az login", ex.Message);
    }

    [Fact]
    public async Task A_cancelled_token_fetch_stays_a_cancellation()
    {
        // Acquiring the token is the first network step of a sign, and it runs under the
        // caller's token. The generic handler must not recast a cancellation as "run az login" —
        // advice for a problem the user doesn't have, and SignAsync would then report the
        // cancelled run as a file that failed to sign.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var provider = new DefaultAzureTokenProvider(
            new SigningOptions(), new ThrowingCredential(new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetTokenAsync(cts.Token));
    }

    private static string Record(string username, string tenantId) =>
        $$"""
        {
          "username": "{{username}}",
          "authority": "https://login.microsoftonline.com/{{tenantId}}",
          "homeAccountId": "home-account-id",
          "tenantId": "{{tenantId}}",
          "clientId": "client-id",
          "version": "1.0"
        }
        """;
}

/// <summary>A credential that fails however it is asked — proves what we don't call, and
/// lets a chosen failure mode be routed through the provider's error handling.</summary>
internal sealed class ThrowingCredential(Exception failure) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) => throw failure;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) =>
        throw failure;
}
