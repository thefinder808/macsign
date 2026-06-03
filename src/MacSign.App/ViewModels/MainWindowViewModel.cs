using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>
/// The app shell's single source of truth: which view is active and the
/// contextual toolbar text. The sidebar's "Active credential" card + Sign badge
/// bind directly to the Sign view-model.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsStore _store = new();

    public SignViewModel Sign { get; } = new();
    public VerifyViewModel Verify { get; } = new();
    public AppleSignViewModel Apple { get; }
    public ProfilesViewModel Profiles { get; }
    public ActivityViewModel Activity { get; }

    public MainWindowViewModel()
    {
        var data = _store.Load();
        Apple = new AppleSignViewModel(data, _store);
        Profiles = new ProfilesViewModel(data, _store);
        Activity = new ActivityViewModel(data, _store);

        // Keep the Sign toolbar subtitle live as files/selection change.
        Sign.StateChanged += () => OnPropertyChanged(nameof(ToolbarSubtitle));
        // Show "Verify another" only once a report exists.
        Verify.ReportChanged += () => OnPropertyChanged(nameof(ShowVerifyAnother));
        // Completed runs flow into Activity (persisted).
        Sign.RunRecorded += Activity.Record;
        Apple.RunRecorded += Activity.Record;
        // "Sign with…" a profile applies it and jumps to the Sign screen.
        Profiles.SignWithRequested += p => { Sign.ApplyProfile(p); CurrentView = AppView.Sign; };
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
        _ => Sign,
    };

    public bool IsSign => CurrentView == AppView.Sign;
    public bool IsVerify => CurrentView == AppView.Verify;
    public bool IsApple => CurrentView == AppView.Apple;
    public bool IsProfiles => CurrentView == AppView.Profiles;
    public bool IsActivity => CurrentView == AppView.Activity;

    public string ToolbarTitle => CurrentView switch
    {
        AppView.Verify => "Verify",
        AppView.Apple => "Mac apps",
        AppView.Profiles => "Profiles",
        AppView.Activity => "Activity",
        _ => "Sign",
    };

    public string ToolbarSubtitle => CurrentView switch
    {
        AppView.Verify => "Check an existing Authenticode signature",
        AppView.Apple => "Sign, notarize & staple a .app bundle",
        AppView.Profiles => "Reusable credential + option presets",
        AppView.Activity => "Recent signing runs · secrets are never logged",
        _ => Sign.SubtitleText,
    };

    [RelayCommand]
    private void Show(AppView view) => CurrentView = view;
}
