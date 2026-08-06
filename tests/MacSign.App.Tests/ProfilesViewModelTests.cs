using System;
using System.IO;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class ProfilesViewModelTests
{
    [Fact]
    public void Clear_empties_profiles_and_persists()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-prof-" + Guid.NewGuid().ToString("N")));
        var data = new AppData();
        var vm = new ProfilesViewModel(data, store);
        vm.Save(new ProfileData { Name = "p1" });
        Assert.True(vm.HasProfiles);

        vm.Clear();

        Assert.Empty(vm.Profiles);
        Assert.True(vm.IsEmpty);
        Assert.Empty(store.Load().Profiles);
    }

    // ── re-save updates instead of duplicating (defect 7) ──────────────────

    private static SettingsStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "macsign-prof-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void Save_of_a_new_credential_adds_a_profile()
    {
        var data = new AppData();
        var vm = new ProfilesViewModel(data, TempStore());

        vm.Save(new ProfileData { Name = "p1", CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });

        Assert.Single(vm.Profiles);
        Assert.Single(data.Profiles);
    }

    [Fact]
    public void Saving_a_new_profile_says_what_it_was_saved_as()
    {
        // Reported from a live run: "Save as profile" jumps to the Profiles screen, but that
        // screen looks identical whether or not anything was saved — with several cards
        // already there, nothing tells you which one is new, or that the click even landed.
        var vm = new ProfilesViewModel(new AppData(), TempStore());

        vm.Save(new ProfileData { Name = "my-signing-account", CredMode = "Azure", Account = "acct" });

        Assert.Contains("my-signing-account", vm.SavedNotice);
        Assert.True(vm.HasSavedNotice);
    }

    [Fact]
    public void Re_saving_reports_an_update_under_the_name_the_user_chose()
    {
        // RefreshFrom deliberately keeps a rename, so the notice has to use the card's own
        // name — echoing the caller's auto-generated one would name a profile that isn't there.
        var data = new AppData();
        data.Profiles.Add(new ProfileData { Name = "renamed-by-me", CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });
        var vm = new ProfilesViewModel(data, TempStore());

        vm.Save(new ProfileData { Name = "dev", CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });

        Assert.Contains("renamed-by-me", vm.SavedNotice);
        Assert.DoesNotContain("dev", vm.SavedNotice);
        Assert.Single(vm.Profiles);
    }

    [Fact]
    public void Save_of_a_matching_credential_updates_the_existing_card_instead_of_duplicating()
    {
        var store = TempStore();
        var data = new AppData();
        var vm = new ProfilesViewModel(data, store);
        vm.Save(new ProfileData
        {
            Name = "original-name", CredMode = "Pfx", PfxPath = "/certs/dev.pfx", Timestamp = false,
        });

        vm.Save(new ProfileData
        {
            Name = "default-name-from-second-save", CredMode = "Pfx", PfxPath = "/certs/dev.pfx",
            Timestamp = true, TimestampUrl = "http://tsa.example",
        });

        Assert.Single(vm.Profiles);
        Assert.Single(data.Profiles);
        Assert.Single(store.Load().Profiles);
    }

    [Fact]
    public void Save_of_a_matching_credential_keeps_the_original_name()
    {
        var vm = new ProfilesViewModel(new AppData(), TempStore());
        vm.Save(new ProfileData { Name = "renamed-by-user", CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });

        vm.Save(new ProfileData { Name = "auto-generated-name", CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });

        Assert.Equal("renamed-by-user", vm.Profiles[0].Name);
    }

    [Fact]
    public void Save_of_a_different_credential_adds_a_second_profile()
    {
        var vm = new ProfilesViewModel(new AppData(), TempStore());
        vm.Save(new ProfileData { Name = "p1", CredMode = "Pfx", PfxPath = "/certs/dev.pfx" });

        vm.Save(new ProfileData { Name = "p2", CredMode = "Pfx", PfxPath = "/certs/other.pfx" });

        Assert.Equal(2, vm.Profiles.Count);
    }
}

/// <summary>Covers the Sign screen's profile interop (<c>CreateProfileSnapshot</c>/
/// <c>ApplyProfile</c>/"Save as profile") — the correctness fixes are in
/// <c>SignViewModel</c>, not <c>ProfilesViewModel</c>, but they exist to serve the
/// Profiles feature, so the tests live alongside <see cref="ProfilesViewModelTests"/>.
///
/// macOS trap: every path used here is POSIX (e.g. "/certs/dev.pfx"). On Unix,
/// <c>Path.GetFileNameWithoutExtension</c> does NOT strip a Windows-style
/// "C:\certs\dev.pfx" — a Windows-path test would silently pass for the wrong reason
/// (or fail) on the macOS CI runner these tests actually run on.</summary>
public class SignProfileInteropTests
{
    [Fact]
    public void CreateProfileSnapshot_then_ApplyProfile_round_trips_the_timestamp_url()
    {
        var source = new SignViewModel
        {
            CredMode = CredMode.Pfx,
            PfxPath = "/certs/dev.pfx",
            TimestampUrl = "http://tsa.example/from-source",
        };
        var snapshot = source.CreateProfileSnapshot();

        var target = new SignViewModel();
        target.ApplyProfile(snapshot);

        Assert.Equal("http://tsa.example/from-source", target.TimestampUrl);
    }

    [Fact]
    public void ApplyProfile_with_legacy_null_timestamp_url_keeps_the_current_url()
    {
        // A profile saved before TimestampUrl existed carries null. Blanking the
        // field would silently drop the TSA while the toggle still reads "on".
        var vm = new SignViewModel { TimestampUrl = "http://tsa.example/keep-me" };
        var legacyProfile = new ProfileData
        {
            CredMode = "Pfx",
            PfxPath = "/certs/dev.pfx",
            TimestampUrl = null,
        };

        vm.ApplyProfile(legacyProfile);

        Assert.Equal("http://tsa.example/keep-me", vm.TimestampUrl);
    }

    [Fact]
    public void ApplyProfile_of_a_pfx_profile_clears_a_previously_set_azure_identity()
    {
        var vm = new SignViewModel
        {
            CredMode = CredMode.Azure,
            Account = "stale-account",
            Profile = "stale-profile",
            Endpoint = "stale.endpoint",
        };
        var pfxProfile = new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx" };

        vm.ApplyProfile(pfxProfile);

        Assert.Equal("", vm.Account);
        Assert.Equal("", vm.Profile);
        Assert.Equal(SignViewModel.DefaultEndpoint, vm.Endpoint);
    }

    [Fact]
    public void CreateProfileSnapshot_scopes_fields_to_the_active_credential_mode()
    {
        var pfxVm = new SignViewModel
        {
            CredMode = CredMode.Pfx,
            PfxPath = "/certs/dev.pfx",
            // Leftover values from a mode the user switched away from — must not leak in.
            Account = "leftover-account",
            Profile = "leftover-profile",
            Endpoint = "leftover.endpoint",
        };
        var pfxSnapshot = pfxVm.CreateProfileSnapshot();
        Assert.Equal("/certs/dev.pfx", pfxSnapshot.PfxPath);
        Assert.Null(pfxSnapshot.Account);
        Assert.Null(pfxSnapshot.Profile);
        Assert.Null(pfxSnapshot.Endpoint);

        var azureVm = new SignViewModel
        {
            CredMode = CredMode.Azure,
            Account = "my-account",
            Profile = "my-profile",
            Endpoint = "eus.codesigning.azure.net",
            // Leftover PFX value from before the user switched to Azure.
            PfxPath = "/certs/leftover.pfx",
        };
        var azureSnapshot = azureVm.CreateProfileSnapshot();
        Assert.Equal("my-account", azureSnapshot.Account);
        Assert.Equal("my-profile", azureSnapshot.Profile);
        Assert.Equal("eus.codesigning.azure.net", azureSnapshot.Endpoint);
        Assert.Null(azureSnapshot.PfxPath);
        Assert.Null(azureSnapshot.ModulePath);
        Assert.Null(azureSnapshot.Thumbprint);
    }

    [Fact]
    public void CreateProfileSnapshot_names_an_azure_profile_after_the_account()
    {
        var vm = new SignViewModel
        {
            CredMode = CredMode.Azure,
            Account = "my-signing-account",
            Profile = "prof",
            Endpoint = "eus.codesigning.azure.net",
        };

        var snapshot = vm.CreateProfileSnapshot();

        Assert.Equal("my-signing-account", snapshot.Name);
    }

    [Fact]
    public void CreateProfileSnapshot_names_a_pfx_profile_after_the_file()
    {
        // The macOS path trap: this must be a POSIX path, not a Windows one.
        var vm = new SignViewModel { CredMode = CredMode.Pfx, PfxPath = "/certs/dev.pfx" };

        var snapshot = vm.CreateProfileSnapshot();

        Assert.Equal("dev", snapshot.Name);
    }

    [Fact]
    public void SaveProfileCommand_CanExecute_is_false_until_the_credential_is_complete()
    {
        var vm = new SignViewModel { CredMode = CredMode.Pfx };
        Assert.False(vm.SaveProfileCommand.CanExecute(null));

        vm.PfxPath = "/certs/dev.pfx";

        Assert.True(vm.SaveProfileCommand.CanExecute(null));
    }

    [Fact]
    public void SaveProfileCommand_raises_SaveProfileRequested_so_the_shell_can_add_it()
    {
        var vm = new SignViewModel { CredMode = CredMode.Pfx, PfxPath = "/certs/dev.pfx" };
        var raised = 0;
        vm.SaveProfileRequested += () => raised++;

        vm.SaveProfileCommand.Execute(null);

        Assert.Equal(1, raised);
    }
}
