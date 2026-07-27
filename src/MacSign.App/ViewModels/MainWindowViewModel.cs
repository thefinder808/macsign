using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>
/// The app shell's single source of truth: which view is active and the
/// contextual toolbar text. Owns the shared <see cref="AppData"/> + store, wires
/// the sub-view-models together, and coordinates a full "reset all settings".
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppData _data;
    private readonly UpdateService _updates;

    public SignViewModel Sign { get; }
    public VerifyViewModel Verify { get; }
    public AppleSignViewModel Apple { get; }
    public ProfilesViewModel Profiles { get; }
    public ActivityViewModel Activity { get; }
    public PreferencesViewModel Preferences { get; }

    // Raised (on the UI thread) when an update is available.
    // The View opens UpdateWindow; the VM stays window-free.
    public event Action<UpdateViewModel>? ShowUpdate;

    public MainWindowViewModel(SettingsStore? store = null, UpdateService? updates = null)
    {
        _store = store ?? new SettingsStore();
        _data = _store.Load();
        _updates = updates ?? new UpdateService();

        // Apply the saved appearance before the window paints.
        ThemeService.Apply(_data.Preferences.Theme);

        Sign = new SignViewModel();
        Verify = new VerifyViewModel();
        Apple = new AppleSignViewModel(_data, _store);
        Profiles = new ProfilesViewModel(_data, _store);
        Activity = new ActivityViewModel(_data, _store);
        // Pass the shared UpdateService so Preferences.CheckNow reuses it.
        Preferences = new PreferencesViewModel(_data, _store, updates: _updates);

        // Seed the Sign screen's defaults from prefs (replacing the hardcoded values).
        Sign.TimestampUrl = _data.Preferences.DefaultTimestampUrl;
        Sign.TimestampEnabled = _data.Preferences.TimestampByDefault;

        // Keep the Sign toolbar subtitle live as files/selection change.
        Sign.StateChanged += () => OnPropertyChanged(nameof(ToolbarSubtitle));
        // Show "Verify another" only once a report exists.
        Verify.ReportChanged += () => OnPropertyChanged(nameof(ShowVerifyAnother));
        // Completed runs flow into Activity (persisted).
        Sign.RunRecorded += Activity.Record;
        Apple.RunRecorded += Activity.Record;
        // "Sign with…" a profile applies it and jumps to the Sign screen.
        Profiles.SignWithRequested += p => { Sign.ApplyProfile(p); CurrentView = AppView.Sign; };
        // "Save as profile" on the Sign screen itself — reusing NewProfile means the save
        // also navigates to Profiles, which is the confirmation (the app has no toast surface).
        Sign.SaveProfileRequested += NewProfile;
        // Preferences → cross-VM actions.
        Preferences.ClearHistoryRequested += Activity.Clear;
        Preferences.CapChanged += Activity.ReTrim;
        Preferences.ResetRequested += ResetAll;
        // Preferences "Check Now" found an update → open the update dialog.
        Preferences.UpdateAvailable += info => ShowUpdate?.Invoke(MakeUpdateViewModel(info));

        // Must run AFTER the prefs seeding above: ApplyProfile keeps the current timestamp
        // URL for profiles that predate that field, and "current" has to mean the prefs
        // default that was just seeded, not the SignViewModel ctor's hardcoded fallback.
        RestoreLastCredentialIfEnabled();
    }

    /// <summary>Restores the most-recently-used profile's credential to the Sign screen at
    /// launch (opt-out via <see cref="AppPrefs.RestoreLastCredential"/>), mirroring the
    /// Apple screen's existing behaviour of remembering its credential across launches.</summary>
    private void RestoreLastCredentialIfEnabled()
    {
        if (!_data.Preferences.RestoreLastCredential) return;

        var last = _data.Profiles
            .Where(p => !string.IsNullOrEmpty(p.LastUsedIso))
            .MaxBy(LastUsed);
        if (last is not null) Sign.ApplyProfile(last);

        // Compare instants, not strings: the "o" format carries a UTC offset, and across a DST
        // change an ordinal compare puts the two sides of the boundary in the wrong order.
        static DateTimeOffset LastUsed(ProfileData p) =>
            DateTimeOffset.TryParse(p.LastUsedIso, out var d) ? d : DateTimeOffset.MinValue;
    }

    /// <summary>True if the app should run an automatic update check right now.
    /// Returns false when auto-check is disabled, or when the last check was within 24 hours.</summary>
    public static bool ShouldAutoCheck(bool autoOn, string? lastIso, DateTime nowUtc)
    {
        if (!autoOn) return false;
        if (string.IsNullOrWhiteSpace(lastIso)) return true;
        return !DateTime.TryParse(lastIso, null,
                   DateTimeStyles.AdjustToUniversal, out var last)
               || nowUtc - last > TimeSpan.FromHours(24);
    }

    /// <summary>Fire-and-forget throttled on-launch update check.
    /// Call once from the View's Opened/Loaded handler after the window is shown.</summary>
    public void StartLaunchUpdateCheck()
    {
        if (!ShouldAutoCheck(_data.Preferences.AutoCheckUpdates,
                             _data.Preferences.LastUpdateCheckUtc,
                             DateTime.UtcNow)) return;
        _ = RunLaunchCheckAsync();
    }

    private async Task RunLaunchCheckAsync()
    {
        try
        {
            var r = await _updates.CheckAsync(CancellationToken.None);
            if (r.Error is not null) return;   // failed check → don't stamp; retry next launch

            // Stamp the check time + persist only on a successful check.
            _data.Preferences.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            _store.Save(_data);

            if (r.UpdateAvailable && r.Info is not null
                && r.Info.Version != _data.Preferences.SkippedVersion)
            {
                var vm = MakeUpdateViewModel(r.Info);
                Dispatcher.UIThread.Post(() => ShowUpdate?.Invoke(vm));
            }
        }
        catch
        {
            // Never surface launch-check errors — background only.
        }
    }

    /// <summary>Invoked by the "Check for Updates…" menu item.
    /// If an update is found, opens the update dialog. If not, navigates to
    /// Preferences and invokes Check Now there (so the inline status message
    /// "You're up to date." / "Couldn't check…" is visible to the user).</summary>
    [RelayCommand]
    private async Task CheckForUpdates()
    {
        try
        {
            var r = await _updates.CheckAsync(CancellationToken.None);

            if (r.UpdateAvailable && r.Info is not null
                && r.Info.Version != _data.Preferences.SkippedVersion)
            {
                ShowUpdate?.Invoke(MakeUpdateViewModel(r.Info));
            }
            else
            {
                // Navigate to Preferences, then run CheckNow so the user sees the
                // inline status ("You're up to date." or "Couldn't check…").
                CurrentView = AppView.Preferences;
                await Preferences.CheckNowCommand.ExecuteAsync(null);
            }
        }
        catch { /* silent — the Preferences screen surfaces any inline error */ }
    }

    private UpdateViewModel MakeUpdateViewModel(UpdateInfo info)
        => new UpdateViewModel(info, _updates, _data, _store);

    /// <summary>Wipe all persisted data back to defaults and refresh the live UI.
    /// The sub-view-models all share the one <see cref="_data"/> instance, so we
    /// mutate it in place to defaults, save, then refresh each VM from it.</summary>
    private void ResetAll()
    {
        _data.Profiles.Clear();
        _data.Activity.Clear();
        _data.AppleSign = new AppleSignPrefs();
        _data.Preferences = new AppPrefs();
        _store.Save(_data);

        Profiles.Clear();
        Activity.Clear();
        Apple.ReloadFromData();
        Preferences.ReloadFromData();
        // ProfileData defaults CredMode to "Azure", so say "Pfx" to match a fresh view-model.
        Sign.ApplyProfile(new ProfileData { CredMode = "Pfx" });
        // ApplyProfile deliberately never touches the transient secret fields (PfxPassword,
        // Pin — they aren't part of ProfileData), so a reset has to clear them here itself,
        // or a typed password/PIN survives "Reset all settings".
        Sign.PfxPassword = "";
        Sign.Pin = "";
        Sign.TimestampUrl = _data.Preferences.DefaultTimestampUrl;
        Sign.TimestampEnabled = _data.Preferences.TimestampByDefault;
    }

    [RelayCommand]
    private void NewProfile()
    {
        Profiles.Save(Sign.CreateProfileSnapshot());
        CurrentView = AppView.Profiles;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    [NotifyPropertyChangedFor(nameof(IsSign))]
    [NotifyPropertyChangedFor(nameof(IsVerify))]
    [NotifyPropertyChangedFor(nameof(IsApple))]
    [NotifyPropertyChangedFor(nameof(IsProfiles))]
    [NotifyPropertyChangedFor(nameof(IsActivity))]
    [NotifyPropertyChangedFor(nameof(IsPreferences))]
    [NotifyPropertyChangedFor(nameof(ToolbarTitle))]
    [NotifyPropertyChangedFor(nameof(ToolbarSubtitle))]
    [NotifyPropertyChangedFor(nameof(ShowVerifyAnother))]
    private AppView _currentView = AppView.Sign;

    /// <summary>The "Verify another" toolbar action shows only on Verify, once a report exists.</summary>
    public bool ShowVerifyAnother => IsVerify && Verify.HasReport;

    public object CurrentPage => CurrentView switch
    {
        AppView.Verify => Verify,
        AppView.Apple => Apple,
        AppView.Profiles => Profiles,
        AppView.Activity => Activity,
        AppView.Preferences => Preferences,
        _ => Sign,
    };

    public bool IsSign => CurrentView == AppView.Sign;
    public bool IsVerify => CurrentView == AppView.Verify;
    public bool IsApple => CurrentView == AppView.Apple;
    public bool IsProfiles => CurrentView == AppView.Profiles;
    public bool IsActivity => CurrentView == AppView.Activity;
    public bool IsPreferences => CurrentView == AppView.Preferences;

    public string ToolbarTitle => CurrentView switch
    {
        AppView.Verify => "Verify",
        AppView.Apple => "Sign",
        AppView.Profiles => "Profiles",
        AppView.Activity => "Activity",
        AppView.Preferences => "Preferences",
        _ => "Sign",
    };

    public string ToolbarSubtitle => CurrentView switch
    {
        AppView.Verify => "Check an existing Authenticode signature",
        AppView.Apple => "Sign, notarize & staple a .app bundle",
        AppView.Profiles => "Reusable credential + option presets",
        AppView.Activity => "Recent signing runs · secrets are never logged",
        AppView.Preferences => "Appearance, signing defaults & data",
        _ => Sign.SubtitleText,
    };

    [RelayCommand]
    private void Show(AppView view) => CurrentView = view;
}
