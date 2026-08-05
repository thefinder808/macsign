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
}
