using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MacSign.App.ViewModels;

/// <summary>
/// The app shell's single source of truth: which view is active and the
/// contextual toolbar text. The sidebar's "Active credential" card + Sign badge
/// bind directly to the Sign view-model.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public SignViewModel Sign { get; } = new();
    public VerifyViewModel Verify { get; } = new();
    public ProfilesViewModel Profiles { get; } = new();
    public ActivityViewModel Activity { get; } = new();

    public MainWindowViewModel()
    {
        // Keep the Sign toolbar subtitle live as files/selection change.
        Sign.StateChanged += () => OnPropertyChanged(nameof(ToolbarSubtitle));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    [NotifyPropertyChangedFor(nameof(IsSign))]
    [NotifyPropertyChangedFor(nameof(IsVerify))]
    [NotifyPropertyChangedFor(nameof(IsProfiles))]
    [NotifyPropertyChangedFor(nameof(IsActivity))]
    [NotifyPropertyChangedFor(nameof(ToolbarTitle))]
    [NotifyPropertyChangedFor(nameof(ToolbarSubtitle))]
    private AppView _currentView = AppView.Sign;

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
        _ => Sign.SubtitleText,
    };

    [RelayCommand]
    private void Show(AppView view) => CurrentView = view;
}
