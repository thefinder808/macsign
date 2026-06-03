using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>Backs the "Set up notary profile" dialog: collect an App Store Connect
/// API key + ids, run notarytool store-credentials, and hand the created profile
/// name back to the caller. No secret is persisted; the .p8 stays a file path.</summary>
public partial class NotaryProfileViewModel : ObservableObject
{
    private readonly AppleSigningService _apple;
    public NotaryProfileViewModel(AppleSigningService? apple = null) => _apple = apple ?? new();

    /// <summary>Raised when the profile was created — the dialog closes with CreatedProfileName.</summary>
    public event Action? Succeeded;

    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))] private string _profileName = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))] private string _apiKeyPath = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))] private string _keyId = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))] private string _issuer = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))] private bool _busy;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasError))] private string _error = "";
    public bool HasError => !string.IsNullOrEmpty(Error);

    public string? CreatedProfileName { get; private set; }

    private bool CanCreate() => !Busy
        && !string.IsNullOrWhiteSpace(ProfileName)
        && !string.IsNullOrWhiteSpace(KeyId)
        && !string.IsNullOrWhiteSpace(Issuer)
        && File.Exists(ApiKeyPath);

    [RelayCommand]
    private async Task ChooseKeyAsync()
    {
        var p = await FileDialogs.PickOneAsync("Choose the App Store Connect API key", new[] { "*.p8" });
        if (p is not null) ApiKeyPath = p;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateProfileAsync()
    {
        Busy = true;
        Error = "";
        try
        {
            var r = await Task.Run(() => _apple.StoreNotaryCredentialsAsync(
                ProfileName.Trim(), ApiKeyPath, KeyId.Trim(), Issuer.Trim(), null, CancellationToken.None));
            if (r.Success) { CreatedProfileName = ProfileName.Trim(); Succeeded?.Invoke(); }
            else Error = r.Detail;
        }
        finally { Busy = false; }
    }
}
