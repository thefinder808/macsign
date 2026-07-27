using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

/// <summary>Covers <see cref="ProfileItemViewModel.Summary"/> (defect 8 — distinguishable
/// PKCS#11 cards) and <see cref="ProfileItemViewModel.RefreshFrom"/> (defect 7 — re-save
/// updates settings on the matched card but keeps the user's rename).</summary>
public class ProfileItemViewModelTests
{
    private static ProfilesViewModel Vm() =>
        new(new AppData(), new SettingsStore(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "macsign-pivm-" + System.Guid.NewGuid().ToString("N"))));

    [Fact]
    public void Pkcs11_summary_includes_the_module_filename_and_thumbprint_prefix()
    {
        var data = new ProfileData
        {
            CredMode = "Pkcs11",
            ModulePath = "/opt/homebrew/lib/libykcs11.dylib",
            Thumbprint = "AB CD 12 34 56 78 90 EF FF FF",
            Timestamp = true,
        };
        var item = new ProfileItemViewModel(data, Vm());

        Assert.Equal("libykcs11.dylib · ABCD1234 · timestamped", item.Summary);
    }

    [Fact]
    public void Pkcs11_summary_distinguishes_two_certs_on_the_same_module()
    {
        var moduleA = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/token.dylib", Thumbprint = "AAAA1111" };
        var moduleB = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/token.dylib", Thumbprint = "BBBB2222" };

        var itemA = new ProfileItemViewModel(moduleA, Vm());
        var itemB = new ProfileItemViewModel(moduleB, Vm());

        Assert.NotEqual(itemA.Summary, itemB.Summary);
    }

    [Fact]
    public void Pkcs11_summary_omits_the_thumbprint_segment_when_absent()
    {
        var data = new ProfileData { CredMode = "Pkcs11", ModulePath = "/opt/token.dylib", Thumbprint = null };
        var item = new ProfileItemViewModel(data, Vm());

        Assert.Equal("token.dylib · no timestamp", item.Summary);
    }

    [Fact]
    public void Pkcs11_summary_falls_back_to_token_when_module_path_is_blank()
    {
        var data = new ProfileData { CredMode = "Pkcs11", ModulePath = "", Thumbprint = "" };
        var item = new ProfileItemViewModel(data, Vm());

        Assert.Equal("token · no timestamp", item.Summary);
    }

    [Fact]
    public void RefreshFrom_updates_settings_but_keeps_the_existing_name()
    {
        var original = new ProfileData
        {
            Name = "renamed-by-user", CredMode = "Pfx", PfxPath = "/certs/dev.pfx",
            Timestamp = false, TimestampUrl = null, Description = "old", Url = "http://old.example",
            LastUsedIso = "2026-01-01T00:00:00-00:00",
        };
        var item = new ProfileItemViewModel(original, Vm());

        var incoming = new ProfileData
        {
            Name = "caller-supplied-default-name", CredMode = "Pfx", PfxPath = "/certs/dev.pfx",
            Timestamp = true, TimestampUrl = "http://tsa.example", Description = "new", Url = "http://new.example",
            LastUsedIso = "2026-07-01T00:00:00-00:00",
        };

        item.RefreshFrom(incoming);

        Assert.Equal("renamed-by-user", item.Name);               // kept — the rename was deliberate
        Assert.True(item.Data.Timestamp);
        Assert.Equal("http://tsa.example", item.Data.TimestampUrl);
        Assert.Equal("new", item.Data.Description);
        Assert.Equal("http://new.example", item.Data.Url);
        Assert.Equal("2026-07-01T00:00:00-00:00", item.Data.LastUsedIso);
    }

    [Fact]
    public void RefreshFrom_raises_Summary_and_LastUsedText_so_the_card_repaints()
    {
        var original = new ProfileData { Name = "n", CredMode = "Pfx", PfxPath = "/certs/dev.pfx", Timestamp = false };
        var item = new ProfileItemViewModel(original, Vm());
        var raised = new System.Collections.Generic.List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.RefreshFrom(new ProfileData { CredMode = "Pfx", PfxPath = "/certs/dev.pfx", Timestamp = true });

        Assert.Contains(nameof(ProfileItemViewModel.Summary), raised);
        Assert.Contains(nameof(ProfileItemViewModel.LastUsedText), raised);
    }
}
