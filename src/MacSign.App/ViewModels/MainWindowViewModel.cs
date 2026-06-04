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

    public SignViewModel Sign { get; }
    public VerifyViewModel Verify { get; }
    public AppleSignViewModel Apple { get; }
    public ProfilesViewModel Profiles { get; }
    public ActivityViewModel Activity { get; }
    public PreferencesViewModel Preferences { get; }

    public MainWindowViewModel(SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore();
        _data = _store.Load();

        // Apply the saved appearance before the window paints.
        ThemeService.Apply(_data.Preferences.Theme);

        Sign = new SignViewModel();
        Verify = new VerifyViewModel();
        Apple = new AppleSignViewModel(_data, _store);
        Profiles = new ProfilesViewModel(_data, _store);
        Activity = new ActivityViewModel(_data, _store);
        Preferences = new PreferencesViewModel(_data, _store);

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
        // Preferences → cross-VM actions.
        Preferences.ClearHistoryRequested += Activity.Clear;
        Preferences.CapChanged += Activity.ReTrim;
        Preferences.ResetRequested += ResetAll;
    }

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
        Sign.TimestampUrl = _data.Preferences.DefaultTimestampUrl;
        Sign.TimestampEnabled = _data.Preferences.TimestampByDefault;
    }

    [RelayCommand]
    private void NewProfile()
    {
        Profiles.Add(Sign.CreateProfileSnapshot());
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
