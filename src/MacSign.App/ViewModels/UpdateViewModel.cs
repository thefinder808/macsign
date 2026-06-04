using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateInfo _info;
    private readonly UpdateService _service;
    private readonly AppData _data;
    private readonly SettingsStore _store;

    public string Title => $"MacSign {_info.Version} is available";
    public string ReleaseNotes => _info.ReleaseNotes;
    public string ReleaseUrl => _info.ReleaseUrl;

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _busy;

    /// <summary>Raised when the install starts (so the window can ask the app to quit;
    /// the detached helper then relaunches the new version).</summary>
    public event Action? InstallStarted;

    public UpdateViewModel(UpdateInfo info, UpdateService service, AppData data, SettingsStore store)
    { _info = info; _service = service; _data = data; _store = store; }

    [RelayCommand]
    private async Task Install()
    {
        Busy = true;
        using var cts = new CancellationTokenSource();
        try
        {
            Status = "Downloading…";
            var dmg = await _service.DownloadAsync(_info, new Progress<double>(p => Progress = p), cts.Token);
            if (dmg is null) { Status = "Download failed. Open the release page to update manually."; Busy = false; return; }

            Status = "Verifying…";
            if (!await _service.VerifyAsync(dmg, cts.Token))
            { Status = "Couldn’t verify the download. Open the release page to update manually."; Busy = false; return; }

            Status = "Installing…";
            var r = await _service.InstallAndRelaunchAsync(dmg, cts.Token);
            if (!r.Success) { Status = r.Detail; Busy = false; return; }
            InstallStarted?.Invoke();   // window handler quits the app; the helper relaunches
        }
        catch (Exception ex) { Status = ex.Message; Busy = false; }
    }

    [RelayCommand]
    private void Skip()
    {
        _data.Preferences.SkippedVersion = _info.Version;
        _store.Save(_data);
    }
}
