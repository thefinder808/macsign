using MacSign.App.Services;
using MacSign.App.ViewModels;
using MacSign.Signing;
using Xunit;

namespace MacSign.App.Tests;

/// <summary>
/// Choosing which Entra identity signs, from the Sign screen. The reported bug was that the
/// GUI offered no control at all: it always fell through to Azure.Identity's default chain,
/// which on macOS resolves to whichever account <c>az login</c> last selected.
/// </summary>
public class SignAzureIdentityTests
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

    private static SignViewModel Azure() => new()
    {
        CredMode = CredMode.Azure,
        Account = "acct",
        Profile = "prof",
        Endpoint = SignViewModel.DefaultEndpoint,
    };

    /// <summary>A Sign screen wired to an engine that records the options and then refuses to
    /// build a signer, so a run stops right after the options are assembled — which is the
    /// only thing these tests care about.</summary>
    private static (SignViewModel Vm, CapturingEngine Engine) Capturing()
    {
        var engine = new CapturingEngine();
        var vm = new SignViewModel(engine)
        {
            CredMode = CredMode.Azure,
            Account = "acct",
            Profile = "prof",
            Endpoint = SignViewModel.DefaultEndpoint,
        };
        vm.Files.Add(new FileItemViewModel("/tmp/azure-identity-probe.dll", isSigned: false, sizeBytes: 1024));
        return (vm, engine);
    }

    // ── Readiness ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_default_source_needs_no_sign_in()
    {
        Assert.True(Azure().CredentialReady);
    }

    [Fact]
    public void The_browser_source_is_not_ready_until_an_account_is_signed_in()
    {
        // Disable the button rather than starting a batch that dies on file 1.
        var vm = Azure();
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;

        Assert.False(vm.CredentialReady);

        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));

        Assert.True(vm.CredentialReady);
        Assert.Equal("chosen@contoso.com", vm.AzureAccountName);
    }

    [Fact]
    public void A_sign_in_for_a_different_tenant_does_not_count()
    {
        // Signing as the account we happen to hold, rather than the one the profile asks for,
        // is precisely the failure this whole feature exists to prevent.
        var vm = Azure();
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;
        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));   // tenant-a
        vm.TenantId = "tenant-b";

        Assert.False(vm.IsAzureSignedIn);
        Assert.False(vm.CredentialReady);
    }

    [Fact]
    public void Changing_the_sign_in_tells_the_shell_to_persist_it()
    {
        // SignViewModel has no store by design — the shell mediates persistence, the same way
        // it already does for "Save as profile".
        var vm = Azure();
        var raised = 0;
        vm.AzureSignInChanged += () => raised++;

        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));
        vm.ApplyAzureSignIn(null);      // "Switch account"

        Assert.Equal(2, raised);
        Assert.False(vm.IsAzureSignedIn);
    }

    // ── What reaches the engine ────────────────────────────────────────────────

    [Fact]
    public async Task The_tenant_and_source_reach_the_signing_options()
    {
        var (vm, engine) = Capturing();
        vm.TenantId = "tenant-a";
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;
        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal("tenant-a", engine.Options!.TrustedSigningTenantId);
        Assert.Equal(TrustedSigningCredentialSource.InteractiveBrowser, engine.Options.TrustedSigningCredentialSource);
        Assert.Contains("chosen@contoso.com", engine.Options.TrustedSigningAuthRecord);
    }

    [Fact]
    public async Task The_default_source_sends_no_sign_in_record_even_if_one_is_held()
    {
        // Otherwise switching back to "Default" would keep silently using the browser account
        // — the same class of surprise as the bug being fixed.
        var (vm, engine) = Capturing();
        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(TrustedSigningCredentialSource.Default, engine.Options!.TrustedSigningCredentialSource);
        Assert.Null(engine.Options.TrustedSigningAuthRecord);
    }

    // ── Profile round-trip (the mode-scoping invariant) ────────────────────────

    [Fact]
    public void A_pfx_profile_never_captures_the_azure_tenant_or_source()
    {
        // Every Azure field is scoped to the active mode in three places that must move in
        // lockstep — snapshot, BuildOptions, ApplyProfile. This pins the snapshot half.
        var vm = Azure();
        vm.TenantId = "tenant-a";
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;
        vm.CredMode = CredMode.Pfx;
        vm.PfxPath = "/certs/dev.pfx";

        var snapshot = vm.CreateProfileSnapshot();

        Assert.Null(snapshot.TenantId);
        Assert.Equal("Default", snapshot.CredentialSource);
    }

    [Fact]
    public void Tenant_and_source_round_trip_through_a_profile()
    {
        var source = Azure();
        source.TenantId = "tenant-a";
        source.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;

        var restored = new SignViewModel();
        restored.ApplyProfile(source.CreateProfileSnapshot());

        Assert.Equal("tenant-a", restored.TenantId);
        Assert.Equal(TrustedSigningCredentialSource.InteractiveBrowser, restored.AzureSource);
    }

    [Fact]
    public void Applying_a_pfx_profile_clears_a_stale_tenant_and_source()
    {
        // ApplyProfile must be symmetric with the snapshot, or leftovers from the previous
        // credential survive into the next one.
        var vm = Azure();
        vm.TenantId = "tenant-a";
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;

        vm.ApplyProfile(new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });

        Assert.Equal("", vm.TenantId);
        Assert.Equal(TrustedSigningCredentialSource.Default, vm.AzureSource);
    }
}

/// <summary>Records the options a run was built with, then declines to produce a signer so
/// nothing is actually signed.</summary>
internal sealed class CapturingEngine : Services.EngineService
{
    public SigningOptions? Options;

    public override MacSign.Signing.AuthenticodeSigner? TryCreateSigner(SigningOptions options, out string? error)
    {
        Options = options;
        error = "captured — this fake never signs";
        return null;
    }
}
