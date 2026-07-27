using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

/// <summary>Covers <see cref="ProfileData.SameCredentialAs"/> (defect 7) — identity is key
/// material only. Description/URL/timestamping are settings *on* a credential, not part of
/// what makes two profiles "the same" credential, so they must never factor into the match.</summary>
public class ProfileDataSameCredentialTests
{
    [Fact]
    public void Pfx_profiles_with_the_same_path_are_the_same_credential()
    {
        var a = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" };
        var b = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" };

        Assert.True(a.SameCredentialAs(b));
    }

    [Fact]
    public void Pfx_profiles_with_different_paths_are_different_credentials()
    {
        var a = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" };
        var b = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/other.pfx" };

        Assert.False(a.SameCredentialAs(b));
    }

    [Fact]
    public void Pfx_path_comparison_is_case_insensitive()
    {
        var a = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/DEV.pfx" };
        var b = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" };

        Assert.True(a.SameCredentialAs(b));
    }

    [Fact]
    public void Different_cred_modes_are_never_the_same_credential()
    {
        var pfx = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" };
        var pkcs11 = new ProfileData { CredMode = "Pkcs11", ModulePath = "/certs/dev.pfx" };

        Assert.False(pfx.SameCredentialAs(pkcs11));
    }

    [Fact]
    public void Pkcs11_profiles_match_on_module_and_thumbprint_together()
    {
        var a = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/token.dylib", Thumbprint = "ABCD1234" };
        var b = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/token.dylib", Thumbprint = "ABCD1234" };
        var diffThumb = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/token.dylib", Thumbprint = "FFFF0000" };
        var diffModule = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/other.dylib", Thumbprint = "ABCD1234" };

        Assert.True(a.SameCredentialAs(b));
        Assert.False(a.SameCredentialAs(diffThumb));
        Assert.False(a.SameCredentialAs(diffModule));
    }

    [Fact]
    public void Azure_profiles_match_on_account_profile_and_endpoint_together()
    {
        var a = new ProfileData { CredMode = "Azure", Account = "acct", Profile = "prof", Endpoint = "eus.codesigning.azure.net" };
        var b = new ProfileData { CredMode = "Azure", Account = "acct", Profile = "prof", Endpoint = "eus.codesigning.azure.net" };
        var diffEndpoint = new ProfileData { CredMode = "Azure", Account = "acct", Profile = "prof", Endpoint = "wus.codesigning.azure.net" };

        Assert.True(a.SameCredentialAs(b));
        Assert.False(a.SameCredentialAs(diffEndpoint));
    }

    [Fact]
    public void Null_and_empty_identity_fields_are_equivalent()
    {
        var a = new ProfileData { CredMode = "Pfx", PfxPath = null };
        var b = new ProfileData { CredMode = "Pfx", PfxPath = "" };

        Assert.True(a.SameCredentialAs(b));
    }

    [Fact]
    public void Description_url_and_timestamp_settings_never_affect_the_match()
    {
        var a = new ProfileData
        {
            CredMode = "Pfx", PfxPath = "/certs/dev.pfx",
            Description = "old", Url = "http://old.example", Timestamp = false, TimestampUrl = null,
        };
        var b = new ProfileData
        {
            CredMode = "Pfx", PfxPath = "/certs/dev.pfx",
            Description = "new", Url = "http://new.example", Timestamp = true, TimestampUrl = "http://tsa.example",
        };

        Assert.True(a.SameCredentialAs(b));
    }
}
