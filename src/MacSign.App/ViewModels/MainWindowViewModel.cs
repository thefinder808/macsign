using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MacSign.App.ViewModels;

/// <summary>
/// The app shell's single source of truth: which view is active, the contextual
/// toolbar text, and the sidebar's "Active credential" summary + Sign badge.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public SignViewModel Sign { get; } = new();
    public VerifyViewModel Verify { get; } = new();
    public ProfilesViewModel Profiles { get; } = new();
    public ActivityViewModel Activity { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    [NotifyPropertyChangedFor(nameof(IsSign))]
    [NotifyPropertyChangedFor(nameof(IsVerify))]
    [NotifyPropertyChangedFor(nameof(IsProfiles))]
    [NotifyPropertyChangedFor(nameof(IsActivity))]
    [NotifyPropertyChangedFor(nameof(ToolbarTitle))]
    [NotifyPropertyChangedFor(nameof(ToolbarSubtitle))]
    private AppView _currentView = AppView.Sign;

    /// <summary>The view-model the content host renders (DataTemplated to a view).</summary>
    public object CurrentPage => CurrentView switch
    {
        AppView.Verify => Verify,
        AppView.Profiles => Profiles,
        AppView.Activity => Activity,
        _ => Sign,
    };

    public bool IsSign => CurrentView == AppView.Sign;
    public bool IsVerify => CurrentView == AppView.Verify;
    public bool IsProfiles => CurrentView == AppView.Profiles;
    public bool IsActivity => CurrentView == AppView.Activity;

    public string ToolbarTitle => CurrentView switch
    {
        AppView.Verify => "Verify",
        AppView.Profiles => "Profiles",
        AppView.Activity => "Activity",
        _ => "Sign",
    };

    public string ToolbarSubtitle => CurrentView switch
    {
        AppView.Verify => "Check an existing Authenticode signature",
        AppView.Profiles => "Reusable credential + option presets",
        AppView.Activity => "Recent signing runs · secrets are never logged",
        _ => "Choose files + a credential, then sign",
    };

    // ── Sidebar "Active credential" card + Sign badge (stubbed; wired to the
    //    Sign view-model's real credential state in a later phase). ──
    [ObservableProperty] private string _activeCredentialName = "Azure Trusted Signing";
    [ObservableProperty] private string _activeCredentialSub = "my-signing-account";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSignCount))]
    private int _signCount = 3;

    public bool HasSignCount => SignCount > 0;

    [RelayCommand]
    private void Show(AppView view) => CurrentView = view;
}
