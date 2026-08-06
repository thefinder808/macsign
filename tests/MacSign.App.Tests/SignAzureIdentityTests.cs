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

    [Fact]
    public void Switching_the_sign_in_source_and_back_keeps_the_tenant()
    {
        // Reported from a live run: the tenant field emptied itself after toggling
        // Browser → This Mac → Browser.
        var vm = Azure();
        vm.TenantId = "tenant-a";

        vm.SetAzureSourceCommand.Execute(TrustedSigningCredentialSource.InteractiveBrowser);
        vm.SetAzureSourceCommand.Execute(TrustedSigningCredentialSource.Default);
        vm.SetAzureSourceCommand.Execute(TrustedSigningCredentialSource.InteractiveBrowser);

        Assert.Equal("tenant-a", vm.TenantId);
    }

    [Fact]
    public void Restoring_a_profile_saved_before_the_tenant_existed_does_not_blank_it()
    {
        // The likelier culprit. Every profile saved before this feature carries
        // TenantId = null, and ApplyProfile maps null → "". At launch
        // RestoreLastCredentialIfEnabled applies the most recent profile, so a tenant typed
        // in the previous session is wiped by a profile that predates the field — exactly the
        // asymmetry TimestampUrl already carries a deliberate exception for.
        var vm = Azure();
        vm.TenantId = "tenant-a";

        vm.ApplyProfile(new ProfileData
        {
            CredMode = "Azure", Account = "acct", Profile = "prof",
            Endpoint = SignViewModel.DefaultEndpoint,
            TenantId = null,   // legacy profile
        });

        Assert.Equal("tenant-a", vm.TenantId);
    }

    [Fact]
    public void Restoring_a_legacy_profile_does_not_silently_switch_the_sign_in_source()
    {
        // Same hazard, worse consequence: flipping a signed-in user back to the machine
        // default would sign as a different identity than the one on screen a moment earlier.
        var vm = Azure();
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;

        vm.ApplyProfile(new ProfileData
        {
            CredMode = "Azure", Account = "acct", Profile = "prof",
            Endpoint = SignViewModel.DefaultEndpoint,
            CredentialSource = null,   // legacy profile
        });

        Assert.Equal(TrustedSigningCredentialSource.InteractiveBrowser, vm.AzureSource);
    }

    [Fact]
    public void Signing_in_adopts_the_tenant_it_was_performed_against()
    {
        // The dialog has its own Tenant box, and the blank-tenant hint explicitly tells the
        // user to fill it in there. Dropping that value left the Sign screen unpinned while
        // the account was pinned — so later signs silently resolved against the account's
        // home tenant, which is the exact failure this feature exists to prevent.
        var vm = Azure();
        var signIn = AzureSignInData.FromRecordJson(RecordJson);
        signIn.RequestedTenant = "contoso.onmicrosoft.com";

        vm.ApplyAzureSignIn(signIn);

        Assert.Equal("contoso.onmicrosoft.com", vm.TenantId);
        Assert.True(vm.IsAzureSignedIn);
    }

    [Fact]
    public void Signing_in_without_a_tenant_leaves_the_field_alone()
    {
        var vm = Azure();
        vm.TenantId = "tenant-a";

        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));   // no RequestedTenant

        Assert.Equal("tenant-a", vm.TenantId);
    }

    // ── Pinned vs unpinned vs legacy ───────────────────────────────────────────

    [Fact]
    public void An_azure_profile_saved_with_no_tenant_records_it_as_unpinned()
    {
        // null means "this profile predates the field". An explicitly empty tenant has to be
        // distinguishable from that, or "follow whatever az login says" cannot be saved at all.
        var vm = Azure();
        vm.TenantId = "";

        Assert.Equal("", vm.CreateProfileSnapshot().TenantId);
    }

    [Fact]
    public void Applying_an_unpinned_azure_profile_clears_a_previous_tenant()
    {
        var vm = Azure();
        vm.TenantId = "tenant-a";

        vm.ApplyProfile(new ProfileData
        {
            CredMode = "Azure", Account = "acct", Profile = "prof",
            Endpoint = SignViewModel.DefaultEndpoint,
            TenantId = "",   // explicitly unpinned, not legacy
        });

        Assert.Equal("", vm.TenantId);
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
    public async Task A_tenant_mismatched_sign_in_never_reaches_the_engine()
    {
        // CredentialReady disables the Sign button, but the invariant belongs on the data path
        // too: AzureSignInData states it absolutely — "a mismatch must read as signed out,
        // never as a silent fallback". Any future caller that bypasses CanExecute would
        // otherwise hand the engine a record for a tenant the user did not ask for.
        var (vm, engine) = Capturing();
        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;
        vm.ApplyAzureSignIn(AzureSignInData.FromRecordJson(RecordJson));   // tenant-a
        vm.TenantId = "tenant-b";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Null(engine.Options!.TrustedSigningAuthRecord);
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

    // ── Reporting who actually signed ──────────────────────────────────────────

    [Fact]
    public async Task A_successful_run_says_which_account_authorized_it()
    {
        // The gap this closes: on the default source the identity was only ever named when a
        // request FAILED. An account that wrongly holds the role signed silently, which is the
        // reported bug with a successful outcome.
        var engine = new FakeSignEngine(TestSigners.Throwaway())
        {
            SignResultFor = _ => SignResult.Ok() with { AuthenticatedAs = "signer@contoso.com (tenant t)" },
        };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };
        vm.Files.Add(new FileItemViewModel("/tmp/who-signed.dll", isSigned: false, sizeBytes: 1024));
        RunData? recorded = null;
        vm.RunRecorded += r => recorded = r;

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Contains("signer@contoso.com", vm.BannerDetail);
        Assert.Contains("signer@contoso.com", recorded!.Credential);
    }

    [Fact]
    public async Task A_local_credential_reports_no_account_and_reads_as_before()
    {
        // A PFX has no Entra account — the certificate is the identity — so nothing should be
        // appended, and the existing banner text must be untouched.
        var engine = new FakeSignEngine(TestSigners.Throwaway());   // AuthenticatedAs stays null
        var vm = new SignViewModel(engine) { CredMode = CredMode.Pfx, PfxPath = "/tmp/cred.pfx" };
        vm.Files.Add(new FileItemViewModel("/tmp/local.dll", isSigned: false, sizeBytes: 1024));

        await vm.SignCommand.ExecuteAsync(null);

        Assert.DoesNotContain(" as ", vm.BannerDetail);
    }

    [Fact]
    public async Task A_run_that_fails_to_start_is_not_attributed_to_the_previous_account()
    {
        // The reset lived after the "couldn't build a signer" early return, so a local-key
        // failure inherited the last Azure run's account — and Record() persists it. A PKCS#11
        // module-not-found row reading "as alice@contoso.com" is the wrong-account confusion
        // this feature removes, written to settings.json.
        var engine = new FakeSignEngine(TestSigners.Throwaway())
        {
            SignResultFor = _ => SignResult.Ok() with { AuthenticatedAs = "alice@contoso.com (tenant t)" },
        };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };
        vm.Files.Add(new FileItemViewModel("/tmp/first.dll", isSigned: false, sizeBytes: 1024));
        await vm.SignCommand.ExecuteAsync(null);

        // Now a run that never gets a credential at all.
        var failing = new CapturingEngine();               // TryCreateSigner returns null
        var vm2 = new SignViewModel(failing) { CredMode = CredMode.Pfx, PfxPath = "/tmp/gone.pfx" };
        vm2.Files.Add(new FileItemViewModel("/tmp/second.dll", isSigned: false, sizeBytes: 1024));
        RunData? recorded = null;
        vm2.RunRecorded += r => recorded = r;

        await vm2.SignCommand.ExecuteAsync(null);

        Assert.DoesNotContain("alice@contoso.com", recorded!.Credential);
    }

    // ── Pre-flight "who would sign?" ───────────────────────────────────────────

    [Fact]
    public async Task Changing_an_identity_input_discards_a_stale_check()
    {
        // The answer must not outlive the question. Check under tenant A, correct the tenant to
        // B, and a pre-fix answer would sit there reading as the post-fix one — the confident
        // wrong answer this whole feature exists to remove.
        var engine = new FakeSignEngine(TestSigners.Throwaway()) { IdentityFor = _ => "alice@contoso.com (tenant a)" };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };
        await vm.CheckIdentityCommand.ExecuteAsync(null);
        Assert.True(vm.HasCheckedIdentity);

        vm.TenantId = "tenant-b";

        Assert.False(vm.HasCheckedIdentity);
    }

    [Fact]
    public async Task Switching_the_source_or_applying_a_profile_discards_a_stale_check()
    {
        var engine = new FakeSignEngine(TestSigners.Throwaway()) { IdentityFor = _ => "alice@contoso.com (tenant a)" };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };

        await vm.CheckIdentityCommand.ExecuteAsync(null);
        vm.SetAzureSourceCommand.Execute(TrustedSigningCredentialSource.InteractiveBrowser);
        Assert.False(vm.HasCheckedIdentity);

        vm.SetAzureSourceCommand.Execute(TrustedSigningCredentialSource.Default);
        await vm.CheckIdentityCommand.ExecuteAsync(null);
        // Note the account alone does NOT change which Entra identity signs, so this answer
        // would still be technically right — but a readback carried across a wholesale
        // credential switch reads as an answer about the new one.
        vm.ApplyProfile(new ProfileData { CredMode = "Azure", Account = "other", Profile = "p" });

        Assert.False(vm.HasCheckedIdentity);
    }

    [Fact]
    public void Checking_is_offered_only_for_the_default_azure_source()
    {
        // Today only the XAML hides the button; the invariant belongs in the command too.
        var vm = Azure();
        Assert.True(vm.CheckIdentityCommand.CanExecute(null));

        vm.AzureSource = TrustedSigningCredentialSource.InteractiveBrowser;
        Assert.False(vm.CheckIdentityCommand.CanExecute(null));

        vm.AzureSource = TrustedSigningCredentialSource.Default;
        vm.CredMode = CredMode.Pfx;
        Assert.False(vm.CheckIdentityCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_long_failure_is_trimmed_to_something_the_panel_can_show()
    {
        // The default chain's failure is a wrapper plus an aggregate naming every credential it
        // tried — routinely 1-2 KB, rendered into a 230px column. Unreadable exactly when it
        // matters most.
        var engine = new FakeSignEngine(TestSigners.Throwaway())
        {
            IdentityFor = _ => throw new InvalidOperationException(new string('x', 4000)),
        };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };

        await vm.CheckIdentityCommand.ExecuteAsync(null);

        Assert.True(vm.CheckedIdentity.Length < 500, $"was {vm.CheckedIdentity.Length} chars");
    }

    [Fact]
    public async Task Checking_the_identity_reports_who_would_sign()
    {
        var engine = new FakeSignEngine(TestSigners.Throwaway()) { IdentityFor = _ => "checker@contoso.com (tenant t)" };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };

        await vm.CheckIdentityCommand.ExecuteAsync(null);

        Assert.Equal("checker@contoso.com (tenant t)", vm.CheckedIdentity);
        Assert.False(vm.IsCheckingIdentity);
    }

    [Fact]
    public async Task A_failed_identity_check_shows_why_rather_than_going_quiet()
    {
        var engine = new FakeSignEngine(TestSigners.Throwaway())
        {
            IdentityFor = _ => throw new InvalidOperationException("Run `az login`."),
        };
        var vm = new SignViewModel(engine) { CredMode = CredMode.Azure, Account = "acct", Profile = "prof" };

        await vm.CheckIdentityCommand.ExecuteAsync(null);

        Assert.Contains("az login", vm.CheckedIdentity);
        Assert.False(vm.IsCheckingIdentity);
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
        // Null, not "Default" — every other mode-scoped field nulls out for the wrong mode,
        // and null is what lets ApplyProfile tell "legacy profile" from "explicitly default".
        Assert.Null(snapshot.CredentialSource);
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
