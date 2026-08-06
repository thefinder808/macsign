using System;
using System.Linq;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

/// <summary>
/// The remembered Azure browser sign-in. It is persisted as five named account fields rather
/// than an opaque blob so that "settings.json holds no secrets" stays something a reviewer can
/// confirm by looking — the tokens themselves live in the OS keychain.
/// </summary>
public class AzureSignInDataTests
{
    private const string RecordJson = """
        {
          "username": "chosen@contoso.com",
          "authority": "https://login.microsoftonline.com/tenant-a",
          "homeAccountId": "home-account-id",
          "tenantId": "tenant-a",
          "clientId": "client-id",
          "version": "1.0"
        }
        """;

    [Fact]
    public void A_sign_in_round_trips_through_the_persisted_fields()
    {
        var data = AzureSignInData.FromRecordJson(RecordJson);

        Assert.Equal("chosen@contoso.com", data.Username);
        Assert.Equal("tenant-a", data.TenantId);
        Assert.Equal("home-account-id", data.HomeAccountId);
        Assert.Equal("client-id", data.ClientId);
        Assert.Equal("https://login.microsoftonline.com/tenant-a", data.Authority);

        // Rebuilding has to produce something the credential can consume again, or a sign-in
        // survives a relaunch on paper and fails in practice.
        var again = AzureSignInData.FromRecordJson(data.ToRecordJson()!);

        Assert.Equal(data.Username, again.Username);
        Assert.Equal(data.TenantId, again.TenantId);
        Assert.Equal(data.HomeAccountId, again.HomeAccountId);
        Assert.Equal(data.ClientId, again.ClientId);
        Assert.Equal(data.Authority, again.Authority);
    }

    [Fact]
    public void Only_the_account_fields_are_written_to_disk()
    {
        // The reason this is stored as named fields rather than an opaque blob is so that
        // "no secrets persisted" is verifiable by reading the file. Derived state landing
        // there undermines that, so pin the exact key set.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macsign-signin-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(dir);
        store.Save(new AppData { AzureSignIn = AzureSignInData.FromRecordJson(RecordJson) });

        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(store.FilePath));
        var keys = doc.RootElement.GetProperty("AzureSignIn").EnumerateObject().Select(p => p.Name).ToList();

        // Five account fields from Azure.Identity's record, plus RequestedTenant — our own
        // annotation recording the tenant as the user typed it. All six are non-secret.
        Assert.Equal(
            new[] { "Authority", "ClientId", "HomeAccountId", "RequestedTenant", "TenantId", "Username" },
            keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_rebuilt_record_carries_only_the_fields_azure_identity_defined()
    {
        // RequestedTenant is ours, not Azure.Identity's. It must stay out of the record we
        // hand back to the credential, or we would be feeding a foreign field into
        // AuthenticationRecord.Deserialize.
        var data = AzureSignInData.FromRecordJson(RecordJson);
        data.RequestedTenant = "contoso.onmicrosoft.com";

        using var doc = System.Text.Json.JsonDocument.Parse(data.ToRecordJson()!);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.DoesNotContain("RequestedTenant", keys);
        Assert.Contains("tenantId", keys);
    }

    [Fact]
    public void A_fresh_install_is_not_signed_in()
    {
        Assert.False(new AzureSignInData().IsSignedIn);
        Assert.Null(new AzureSignInData().ToRecordJson());
    }

    [Fact]
    public void A_record_that_cannot_be_read_leaves_the_user_signed_out()
    {
        // settings.json is hand-editable and outlives app upgrades. "Signed out" is a state
        // the user can fix; a crash on startup is not.
        Assert.False(AzureSignInData.FromRecordJson("{not json").IsSignedIn);
    }

    [Theory]
    [InlineData(null)]      // no tenant pinned on the profile
    [InlineData("")]
    [InlineData("tenant-a")]
    [InlineData("TENANT-A")]
    public void A_sign_in_satisfies_a_matching_or_absent_tenant(string? wanted)
    {
        Assert.True(AzureSignInData.FromRecordJson(RecordJson).MatchesTenant(wanted));
    }

    [Fact]
    public void A_sign_in_for_another_tenant_does_not_count_as_signed_in()
    {
        // The reported bug in miniature: silently authenticating as the account you happen to
        // have, rather than the one the configuration asked for, is exactly what went wrong.
        Assert.False(AzureSignInData.FromRecordJson(RecordJson).MatchesTenant("tenant-b"));
    }

    [Fact]
    public void A_domain_tenant_matches_the_sign_in_it_was_used_for()
    {
        // Entra always reports the canonical GUID in the record, but the Tenant field
        // deliberately accepts a domain — `contoso.onmicrosoft.com` is its own watermark.
        // Comparing only against the record left anyone who typed a domain reading as
        // permanently "not signed in", with no way out short of wiping settings.
        var data = AzureSignInData.FromRecordJson(RecordJson);      // tenantId = "tenant-a"
        data.RequestedTenant = "contoso.onmicrosoft.com";

        Assert.True(data.MatchesTenant("contoso.onmicrosoft.com"));
        Assert.True(data.MatchesTenant("CONTOSO.ONMICROSOFT.COM"));
        Assert.True(data.MatchesTenant("  contoso.onmicrosoft.com  "));
        // …and the canonical form keeps working, so switching the field to the GUID is fine.
        Assert.True(data.MatchesTenant("tenant-a"));
    }

    [Fact]
    public void A_genuinely_different_tenant_still_reads_as_signed_out()
    {
        // The permissiveness above must not swallow the case the check exists for.
        var data = AzureSignInData.FromRecordJson(RecordJson);
        data.RequestedTenant = "contoso.onmicrosoft.com";

        Assert.False(data.MatchesTenant("fabrikam.onmicrosoft.com"));
        Assert.False(data.MatchesTenant("tenant-b"));
    }
}
