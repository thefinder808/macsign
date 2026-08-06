using System.Text;
using System.Text.Json;
using MacSign.Signing.Azure;

namespace MacSign.Signing.Tests;

/// <summary>
/// Reading the account out of an access token so an auth failure can say *who* it was issued
/// to. Display only — the token is never validated here and the answer must never gate a
/// decision. The reported bug was precisely that a user could not tell which identity signed.
/// </summary>
public class JwtIdentityTests
{
    [Fact]
    public void Describes_a_user_token_by_username_and_tenant()
    {
        var hint = JwtIdentity.Describe(Jwt(new
        {
            preferred_username = "daily.driver@contoso.com",
            tid = "11111111-2222-3333-4444-555555555555",
        }));

        Assert.Equal("daily.driver@contoso.com (tenant 11111111-2222-3333-4444-555555555555)", hint);
    }

    [Theory]
    [InlineData("upn")]              // older user tokens
    [InlineData("unique_name")]      // older still
    [InlineData("azp")]              // service principal: authorized party
    [InlineData("appid")]            // service principal: v1 form
    public void Falls_back_through_the_other_identity_claims(string claim)
    {
        var payload = new Dictionary<string, object> { [claim] = "someone-or-something", ["tid"] = "t" };

        Assert.Equal("someone-or-something (tenant t)", JwtIdentity.Describe(Jwt(payload)));
    }

    [Fact]
    public void Prefers_preferred_username_when_several_claims_are_present()
    {
        var hint = JwtIdentity.Describe(Jwt(new Dictionary<string, object>
        {
            ["appid"] = "an-app-id",
            ["upn"] = "old@contoso.com",
            ["preferred_username"] = "new@contoso.com",
            ["tid"] = "t",
        }));

        Assert.Equal("new@contoso.com (tenant t)", hint);
    }

    [Fact]
    public void Reports_the_tenant_alone_when_no_identity_claim_is_present()
    {
        // A wrong-tenant token is the failure this feature exists to explain, so the tenant is
        // worth surfacing even when the token names no principal.
        Assert.Equal("tenant only-a-tenant", JwtIdentity.Describe(Jwt(new { tid = "only-a-tenant" })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]                    // no segments
    [InlineData("only.two")]                     // too few segments
    [InlineData("aGVhZGVy.bm90LWpzb24.sig")]     // valid base64url, payload isn't JSON
    [InlineData("aGVhZGVy.!!!not-base64!!!.sig")]// payload isn't base64url
    [InlineData("aGVhZGVy.e30.sig")]             // valid but empty JSON object
    public void Returns_null_rather_than_throwing_for_anything_unreadable(string? token)
    {
        // This runs while formatting an *error*. Throwing here would replace a good diagnostic
        // with a confusing crash — the worst possible moment to be strict.
        Assert.Null(JwtIdentity.Describe(token));
    }

    /// <summary>Builds an unsigned JWT. Nothing verifies it — only the payload is ever read.</summary>
    private static string Jwt(object payload) =>
        "eyJhbGciOiJub25lIn0." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload)) + ".not-a-signature";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
