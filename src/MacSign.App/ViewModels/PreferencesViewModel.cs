using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>
/// Preferences screen: Appearance (theme), Signing defaults, Activity/data
/// housekeeping, and Updates. Owns only prefs state + persistence; cross-VM
/// actions (clear history, re-trim, reset all, surface an available update)
/// are raised as events the shell coordinates.
/// No secrets — <see cref="AppPrefs"/> is theme/URL/flags only.
/// </summary>
public partial class PreferencesViewModel : ObservableObject
{
    private readonly AppData _data;
    private readonly SettingsStore _store;
    private readonly IProcessRunner _runner;
    private readonly UpdateService _updates;
    private bool _suppress;                    // guards side effects during ReloadFromData
    private CancellationTokenSource? _confirmCts;

    /// <summary>Raised when the user empties Activity history.</summary>
    public event Action? ClearHistoryRequested;
    /// <summary>Raised when the "keep last N" cap changes, so Activity re-trims.</summary>
    public event Action? CapChanged;
    /// <summary>Raised (after the two-step confirm) to reset all settings.</summary>
    public event Action? ResetRequested;
    /// <summary>Raised by Check Now (or the on-launch check) when a newer release is found.
    /// The shell (Task 9) opens the update dialog; the VM only fires the event.</summary>
    public event Action<UpdateInfo>? UpdateAvailable;

    public PreferencesViewModel(AppData data, SettingsStore store, IProcessRunner? runner = null,
        UpdateService? updates = null)
    {
        _data = data;
        _store = store;
        _runner = runner ?? new ProcessRunner();
        _updates = updates ?? new UpdateService();
        var p = data.Preferences;
        _theme = p.Theme;
        _defaultTimestampUrl = p.DefaultTimestampUrl;
        _timestampByDefault = p.TimestampByDefault;
        _restoreLastCredential = p.RestoreLastCredential;
        _activityKeepLast = p.ActivityKeepLast;
        _autoCheckUpdates = p.AutoCheckUpdates;
    }

    // ── Appearance ──
    [ObservableProperty] private string _theme;            // System | Light | Dark
    public bool IsSystemTheme => Theme == "System";
    public bool IsLightTheme  => Theme == "Light";
    public bool IsDarkTheme   => Theme == "Dark";

    partial void OnThemeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        if (_suppress) return;
        ThemeService.Apply(value);
        Persist();
    }

    [RelayCommand] private void SetTheme(string t) => Theme = t;

    // ── Signing defaults ──
    [ObservableProperty] private string _defaultTimestampUrl;
    [ObservableProperty] private bool _timestampByDefault;
    [ObservableProperty] private bool _restoreLastCredential;
    partial void OnDefaultTimestampUrlChanged(string value)   { if (_suppress) return; Persist(); }
    partial void OnTimestampByDefaultChanged(bool value)      { if (_suppress) return; Persist(); }
    partial void OnRestoreLastCredentialChanged(bool value)   { if (_suppress) return; Persist(); }

    // ── Activity & data ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCap50), nameof(IsCap100), nameof(IsCap500), nameof(IsCapUnlimited))]
    private int _activityKeepLast;
    public bool IsCap50        => ActivityKeepLast == 50;
    public bool IsCap100       => ActivityKeepLast == 100;
    public bool IsCap500       => ActivityKeepLast == 500;
    public bool IsCapUnlimited => ActivityKeepLast == 0;

    partial void OnActivityKeepLastChanged(int value)
    {
        if (_suppress) return;
        Persist();
        CapChanged?.Invoke();
    }

    [RelayCommand]
    private void SetCap(string n) =>
        ActivityKeepLast = string.Equals(n, "Unlimited", StringComparison.OrdinalIgnoreCase)
            ? 0 : int.Parse(n, CultureInfo.InvariantCulture);

    [RelayCommand] private void ClearHistory() => ClearHistoryRequested?.Invoke();

    [RelayCommand]
    private async Task RevealSettingsAsync() =>
        await _runner.RunAsync("/usr/bin/open", new[] { "-R", _store.FilePath }, null, CancellationToken.None);

    // ── Updates ──
    [ObservableProperty] private bool _autoCheckUpdates;
    partial void OnAutoCheckUpdatesChanged(bool value) { if (_suppress) return; Persist(); }

    public string CurrentVersion => AppInfo.Version;

    [ObservableProperty] private string _updateStatus = "";

    private bool _checking;   // re-entrancy guard: an on-launch auto-check must not race a user click

    [RelayCommand]
    private async Task CheckNow()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            UpdateStatus = "Checking…";
            var r = await _updates.CheckAsync(default);
            if (r.UpdateAvailable && r.Info is not null)
            {
                UpdateStatus = $"Version {r.Info.Version} is available.";
                UpdateAvailable?.Invoke(r.Info);
            }
            else if (r.Error is not null)
            {
                UpdateStatus = "Couldn't check for updates.";
            }
            else
            {
                UpdateStatus = "You're up to date.";
            }
        }
        finally { _checking = false; }
    }

    // ── Reset all (two-step confirm; Task.Delay not Dispatcher — headless tests) ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetButtonText))]
    private bool _confirmReset;
    public string ResetButtonText => ConfirmReset ? "Click again to reset everything" : "Reset all settings…";

    [RelayCommand]
    private void ResetAll()
    {
        if (!ConfirmReset)
        {
            ConfirmReset = true;
            ArmResetTimeout();
            return;
        }
        CancelConfirm();
        ConfirmReset = false;
        ResetRequested?.Invoke();
    }

    /// <summary>Re-read prefs from the (now-default) shared data after a reset,
    /// without re-persisting or re-raising events. Applies the theme once at the end.</summary>
    public void ReloadFromData()
    {
        _suppress = true;
        var p = _data.Preferences;
        Theme = p.Theme;
        DefaultTimestampUrl = p.DefaultTimestampUrl;
        TimestampByDefault = p.TimestampByDefault;
        RestoreLastCredential = p.RestoreLastCredential;
        ActivityKeepLast = p.ActivityKeepLast;
        AutoCheckUpdates = p.AutoCheckUpdates;
        _suppress = false;
        ThemeService.Apply(Theme);
    }

    private void Persist()
    {
        var p = _data.Preferences;
        p.Theme = Theme;
        p.DefaultTimestampUrl = DefaultTimestampUrl;
        p.TimestampByDefault = TimestampByDefault;
        p.RestoreLastCredential = RestoreLastCredential;
        p.ActivityKeepLast = ActivityKeepLast;
        p.AutoCheckUpdates = AutoCheckUpdates;
        _store.Save(_data);
    }

    private void ArmResetTimeout()
    {
        CancelConfirm();
        _confirmCts = new CancellationTokenSource();
        _ = RevertAfterDelay(_confirmCts.Token);
    }

    private async Task RevertAfterDelay(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
        catch (OperationCanceledException) { return; }
        ConfirmReset = false;
    }

    private void CancelConfirm()
    {
        _confirmCts?.Cancel();
        _confirmCts?.Dispose();
        _confirmCts = null;
    }
}
