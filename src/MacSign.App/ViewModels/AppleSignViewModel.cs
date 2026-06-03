using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>
/// The "Mac apps" screen: choose a .app bundle or a .dmg disk image, pick a
/// Developer ID identity, then sign → verify → (optionally) notarize → staple by
/// driving Apple's own tools through <see cref="AppleSigningService"/>. Mirrors
/// the Sign screen's patterns (RelayCommand, off-thread work, Idle/Working/Done,
/// banner, RunRecorded). Secrets are never persisted; tool output streams live.
/// </summary>
public partial class AppleSignViewModel : ObservableObject
{
    private readonly AppleSigningService _apple = new();
    private readonly AppData _data;
    private readonly SettingsStore _store;
    private readonly string? _pendingIdentitySha1;
    private CancellationTokenSource? _cts;

    /// <summary>Raised when a run finishes, so the shell records it in Activity.</summary>
    public event Action<RunData>? RunRecorded;

    public ObservableCollection<SigningIdentity> Identities { get; } = new();

    public AppleSignViewModel(AppData data, SettingsStore store)
    {
        _data = data;
        _store = store;
        var p = data.AppleSign;
        _notaryProfile = string.IsNullOrWhiteSpace(p.NotaryProfile) ? "my-notary-profile" : p.NotaryProfile;
        _hardenedRuntime = p.HardenedRuntime;
        _deep = p.Deep;
        _notarize = p.Notarize;
        _staple = p.Staple;
        _useApiKey = p.UseApiKey;
        _pendingIdentitySha1 = p.IdentitySha1;
    }

    // ── target (.app bundle or .dmg) ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetName), nameof(HasTarget), nameof(HasNoTarget),
        nameof(IsApp), nameof(IsDmg))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _targetPath = "";

    public string TargetName => string.IsNullOrEmpty(TargetPath) ? "" : Path.GetFileName(TargetPath);
    public bool HasTarget => AppleSigningService.Classify(TargetPath) != AppleTargetKind.Unsupported;
    public bool HasNoTarget => !HasTarget;
    public bool IsApp => AppleSigningService.Classify(TargetPath) == AppleTargetKind.App;
    public bool IsDmg => AppleSigningService.Classify(TargetPath) == AppleTargetKind.Dmg;

    // ── identity ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private SigningIdentity? _selectedIdentity;

    // ── options (.app only) ──
    [ObservableProperty] private string _entitlementsPath = "";
    [ObservableProperty] private bool _hardenedRuntime;
    [ObservableProperty] private bool _deep;

    // ── notarization ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _notarize;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _useApiKey;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _notaryProfile;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _apiKeyPath = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _apiKeyId = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _apiIssuer = "";
    [ObservableProperty] private bool _staple;

    // ── run state ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsWorking), nameof(IsDone),
        nameof(ShowOkBanner), nameof(ShowErrBanner))]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private AppleSignState _state = AppleSignState.Idle;

    public bool IsIdle => State == AppleSignState.Idle;
    public bool IsWorking => State == AppleSignState.Working;
    public bool IsDone => State == AppleSignState.Done;

    [ObservableProperty] private string _bannerTitle = "";
    [ObservableProperty] private string _bannerDetail = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOkBanner), nameof(ShowErrBanner))]
    private bool _bannerIsError;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _logText = "";

    public bool ShowOkBanner => IsDone && !BannerIsError;
    public bool ShowErrBanner => IsDone && BannerIsError;

    public string RunButtonText =>
        !Notarize ? "Sign"
        : Staple ? "Sign, notarize & staple"
        : "Sign & notarize";

    partial void OnNotarizeChanged(bool value) => OnPropertyChanged(nameof(RunButtonText));
    partial void OnStapleChanged(bool value) => OnPropertyChanged(nameof(RunButtonText));

    // ════════════════ commands ════════════════

    [RelayCommand]
    private async Task ChooseAppAsync()
    {
        var p = await FileDialogs.PickAppBundleAsync();
        if (p is not null) SetTarget(p);
    }

    [RelayCommand]
    private async Task ChooseDmgAsync()
    {
        var p = await FileDialogs.PickDmgAsync();
        if (p is not null) SetTarget(p);
    }

    [RelayCommand]
    private void ClearTarget() => SetTarget("");

    [RelayCommand]
    private async Task ChooseEntitlementsAsync()
    {
        var p = await FileDialogs.PickEntitlementsAsync();
        if (p is not null) EntitlementsPath = p;
    }

    [RelayCommand]
    private async Task RefreshIdentitiesAsync()
    {
        var ids = await Task.Run(() => _apple.ListIdentitiesAsync(CancellationToken.None));
        Identities.Clear();
        foreach (var i in ids) Identities.Add(i);
        SelectedIdentity =
            Identities.FirstOrDefault(i => i.Sha1 == _pendingIdentitySha1)
            ?? Identities.FirstOrDefault(i => i.Name.StartsWith("Developer ID Application:", StringComparison.OrdinalIgnoreCase))
            ?? Identities.FirstOrDefault();
    }

    public void SetTarget(string path)
    {
        TargetPath = path;
        if (IsDone) State = AppleSignState.Idle;
    }

    private bool CanRun() => IsIdle && HasTarget && SelectedIdentity is not null
        && (!Notarize || BuildCreds().IsComplete);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        SavePrefs();
        State = AppleSignState.Working;
        BannerTitle = ""; BannerDetail = ""; BannerIsError = false;
        LogText = "";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var log = new Progress<string>(AppendLog);
        var id = SelectedIdentity!;
        string target = TargetPath;
        string stage = "signed";
        bool gatekeeperOk = false;

        try
        {
            ProgressText = "Signing…";
            AppendLog("==> Signing");
            var r = await Task.Run(() => _apple.SignAsync(target, id,
                NullIfEmpty(EntitlementsPath), HardenedRuntime, Deep, log, ct));
            if (!r.Success) { Finish(r, ct); return; }

            ProgressText = "Verifying…";
            AppendLog("==> Verifying integrity");
            r = await Task.Run(() => _apple.VerifyAsync(target, log, ct));
            if (!r.Success) { Finish(r, ct); return; }
            stage = "signed & verified";

            if (Notarize)
            {
                ProgressText = "Notarizing…";
                AppendLog("==> Notarizing");
                r = await Task.Run(() => _apple.NotarizeAsync(target, BuildCreds(), log, ct));
                if (!r.Success) { Finish(r, ct); return; }
                stage = "signed, verified & notarized";

                if (Staple)
                {
                    ProgressText = "Stapling…";
                    AppendLog("==> Stapling");
                    r = await Task.Run(() => _apple.StapleAsync(target, log, ct));
                    if (!r.Success) { Finish(r, ct); return; }
                    stage = "signed, verified, notarized & stapled";

                    // Final, best-effort Gatekeeper check — informational, never a hard gate.
                    ProgressText = "Assessing…";
                    AppendLog("==> Gatekeeper assessment");
                    var assess = await Task.Run(() => _apple.AssessAsync(target, log, ct));
                    gatekeeperOk = assess.Success;
                }
            }
        }
        catch (Exception ex)
        {
            ProgressText = "";
            State = AppleSignState.Done;
            BannerIsError = true;
            BannerTitle = "Failed";
            BannerDetail = ex.Message;
            Record("fail", ex.Message);
            return;
        }

        ProgressText = "";
        State = AppleSignState.Done;
        BannerIsError = false;
        BannerTitle = $"{TargetName} {stage}";
        BannerDetail = !Notarize ? "Notarization was skipped."
            : gatekeeperOk ? "Gatekeeper accepts it — ready to distribute."
            : "Ready to distribute.";
        Record("ok", stage);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void UseProfileMode() => UseApiKey = false;

    [RelayCommand]
    private void UseApiKeyMode() => UseApiKey = true;

    [RelayCommand]
    private void RunAnother()
    {
        BannerTitle = ""; BannerDetail = ""; BannerIsError = false;
        LogText = "";
        State = AppleSignState.Idle;
    }

    // ════════════════ helpers ════════════════

    private void Finish(AppleOpResult result, CancellationToken ct)
    {
        ProgressText = "";
        State = AppleSignState.Done;
        bool canceled = ct.IsCancellationRequested;
        BannerIsError = true;
        BannerTitle = canceled ? "Canceled" : result.Title;
        BannerDetail = canceled ? "The operation was canceled." : result.Detail;
        Record(canceled ? "warn" : "fail", canceled ? "canceled" : result.Title.ToLowerInvariant());
    }

    private void AppendLog(string line) => LogText += line + "\n";

    private NotarizeCreds BuildCreds() => UseApiKey
        ? new NotarizeCreds
        {
            ApiKeyPath = NullIfEmpty(ApiKeyPath),
            ApiKeyId = NullIfEmpty(ApiKeyId),
            ApiIssuer = NullIfEmpty(ApiIssuer),
        }
        : new NotarizeCreds { KeychainProfile = NullIfEmpty(NotaryProfile) };

    private void Record(string status, string detail) => RunRecorded?.Invoke(new RunData
    {
        FileCount = 1,
        Credential = SelectedIdentity is null ? "Apple" : $"Apple · {SelectedIdentity.Name}",
        Detail = detail,
        Status = status,
        WhenIso = DateTime.Now.ToString("o"),
    });

    private void SavePrefs()
    {
        _data.AppleSign = new AppleSignPrefs
        {
            IdentitySha1 = SelectedIdentity?.Sha1,
            NotaryProfile = NullIfEmpty(NotaryProfile),
            HardenedRuntime = HardenedRuntime,
            Deep = Deep,
            Notarize = Notarize,
            Staple = Staple,
            UseApiKey = UseApiKey,
        };
        _store.Save(_data);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
