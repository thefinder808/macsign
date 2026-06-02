using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;
using MacSign.Signing;

namespace MacSign.App.ViewModels;

/// <summary>
/// The Sign screen: a files list + a credential/options inspector, wired to the
/// real engine. Signs one file per <c>SignAsync</c> call so each row reports its
/// own success/failure. Secrets (password/PIN) are transient — never persisted.
/// </summary>
public partial class SignViewModel : ObservableObject
{
    private readonly EngineService _engine = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    /// <summary>Raised when counts/credential change, so the shell can refresh
    /// the toolbar subtitle and the sidebar badge/credential card.</summary>
    public event Action? StateChanged;

    public SignViewModel()
    {
        Files.CollectionChanged += OnFilesChanged;
    }

    // ── credential mode ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPfx), nameof(IsPkcs11), nameof(IsAzure),
        nameof(CredBlurb), nameof(ActiveCredentialName), nameof(ActiveCredentialSub))]
    private CredMode _credMode = CredMode.Azure;

    public bool IsPfx => CredMode == CredMode.Pfx;
    public bool IsPkcs11 => CredMode == CredMode.Pkcs11;
    public bool IsAzure => CredMode == CredMode.Azure;

    public string CredBlurb => CredMode switch
    {
        CredMode.Pfx => "Local certificate — kept on this Mac.",
        CredMode.Pkcs11 => "Hardware token — the key never leaves the device.",
        _ => "Cloud HSM — the key never leaves Azure.",
    };

    [RelayCommand]
    private void SetCredMode(CredMode mode)
    {
        CredMode = mode;
        StateChanged?.Invoke();
    }

    // ── credential fields (transient; never persisted) ──
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ActiveCredentialSub))] private string _pfxPath = "";
    [ObservableProperty] private string _pfxPassword = "";
    [ObservableProperty] private string _modulePath = "";
    [ObservableProperty] private string _thumbprint = "";
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ActiveCredentialSub))] private string _account = "my-signing-account";
    [ObservableProperty] private string _profile = "my-cert-profile";
    [ObservableProperty] private string _endpoint = "eus.codesigning.azure.net";

    // ── options ──
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _moreInfoUrl = "";
    [ObservableProperty] private bool _timestampEnabled = true;
    [ObservableProperty] private string _timestampUrl = "http://timestamp.digicert.com";

    // ── run state ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsSigning), nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand))]
    private SignState _signState = SignState.Idle;

    public bool IsIdle => SignState == SignState.Idle;
    public bool IsSigning => SignState == SignState.Signing;
    public bool IsDone => SignState == SignState.Done;

    [ObservableProperty] private string _bannerTitle = "";
    [ObservableProperty] private string _bannerDetail = "";
    [ObservableProperty] private bool _bannerIsError;

    // ── derived counts ──
    public int FilesCount => Files.Count;
    public int SelectedCount => Files.Count(f => f.IsSelected && f.IsSelectable);
    public int SignedCount => Files.Count(f => f.IsSigned);
    public int ToSignCount => SelectedCount;
    public bool HasToSign => ToSignCount > 0;
    public bool HasFiles => Files.Count > 0;
    public string SubtitleText => $"{FilesCount} files · {SelectedCount} selected · {SignedCount} already signed";
    public string SignButtonText => ToSignCount == 1 ? "Sign 1 file" : $"Sign {ToSignCount} files";

    // ── sidebar "Active credential" ──
    public string ActiveCredentialName => CredMode switch
    {
        CredMode.Pfx => "PFX file",
        CredMode.Pkcs11 => "PKCS#11 token",
        _ => "Azure Trusted Signing",
    };
    public string ActiveCredentialSub => CredMode switch
    {
        CredMode.Pfx => string.IsNullOrWhiteSpace(PfxPath) ? "no file chosen" : Path.GetFileName(PfxPath),
        CredMode.Pkcs11 => "hardware token",
        _ => Account,
    };

    // ════════════════ files ════════════════

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!_engine.IsSignable(p)) continue;
            if (Files.Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            long size = 0;
            try { size = new FileInfo(p).Length; } catch { /* size best-effort */ }
            Files.Add(new FileItemViewModel(p, _engine.IsAlreadySigned(p), size));
        }
        if (SignState == SignState.Done) SignState = SignState.Idle;
        RaiseCounts();
    }

    [RelayCommand]
    private async Task AddFilesAsync() => AddPaths(await FileDialogs.PickSignablesAsync());

    [RelayCommand]
    private async Task ChoosePfxAsync()
    {
        var p = await FileDialogs.PickOneAsync("Choose PFX / P12", new[] { "*.pfx", "*.p12" });
        if (p is not null) PfxPath = p;
    }

    [RelayCommand]
    private async Task ChooseModuleAsync()
    {
        var p = await FileDialogs.PickOneAsync("Choose PKCS#11 module", new[] { "*.so", "*.dylib" });
        if (p is not null) ModulePath = p;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (FileItemViewModel f in e.OldItems) f.PropertyChanged -= OnItemChanged;
        if (e.NewItems is not null)
            foreach (FileItemViewModel f in e.NewItems) f.PropertyChanged += OnItemChanged;
        RaiseCounts();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItemViewModel.IsSelected)
            or nameof(FileItemViewModel.IsSigned)
            or nameof(FileItemViewModel.RunState))
            RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(FilesCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SignedCount));
        OnPropertyChanged(nameof(ToSignCount));
        OnPropertyChanged(nameof(HasToSign));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(SignButtonText));
        SignCommand.NotifyCanExecuteChanged();
        StateChanged?.Invoke();
    }

    // ════════════════ signing ════════════════

    private bool CanSign() => IsIdle && HasToSign;

    [RelayCommand(CanExecute = nameof(CanSign))]
    private async Task SignAsync()
    {
        var options = BuildOptions();
        var signer = _engine.TryCreateSigner(options, out var error);
        if (signer is null)
        {
            BannerIsError = true;
            BannerTitle = "Couldn’t start signing";
            BannerDetail = error ?? "Unknown error.";
            SignState = SignState.Done;
            return;
        }

        SignState = SignState.Signing;
        _cts = new CancellationTokenSource();
        var log = new Progress<string>(_ => { /* per-file state drives the UI; log reserved for Activity */ });

        var targets = Files.Where(f => f.IsSelected && f.IsSelectable).ToList();
        int ok = 0;
        string? firstError = null;

        foreach (var file in targets)
        {
            file.RunState = FileRunState.Signing;
            SignResult result;
            try
            {
                // Offload the crypto/IO so the UI stays responsive.
                result = await Task.Run(() => _engine.SignOneAsync(signer, file.Path, options, log, _cts.Token));
            }
            catch (Exception ex)
            {
                result = SignResult.Fail(ex.Message);
            }

            if (result.Success) { file.RunState = FileRunState.Done; ok++; }
            else { file.RunState = FileRunState.None; firstError ??= result.Error; }
        }

        SignState = SignState.Done;
        BannerIsError = ok == 0;
        if (ok == targets.Count)
        {
            BannerTitle = $"{ok} file{(ok == 1 ? "" : "s")} signed{(TimestampEnabled ? " & timestamped" : "")}";
            BannerDetail = "Signed and verified VALID after signing.";
        }
        else
        {
            BannerTitle = $"{ok} of {targets.Count} signed";
            BannerDetail = firstError ?? "Some files failed.";
        }
    }

    [RelayCommand]
    private void SignMore()
    {
        // The just-signed files become permanently signed (dimmed, deselected).
        foreach (var f in Files.Where(f => f.RunState == FileRunState.Done).ToList())
        {
            f.IsSigned = true;
            f.IsSelected = false;
            f.RunState = FileRunState.None;
        }
        BannerTitle = "";
        BannerDetail = "";
        BannerIsError = false;
        SignState = SignState.Idle;
        RaiseCounts();
    }

    private SigningOptions BuildOptions() => new()
    {
        CertMode = CredMode switch
        {
            CredMode.Pkcs11 => CertMode.Pkcs11,
            CredMode.Azure => CertMode.TrustedSigning,
            _ => CertMode.Pfx,
        },
        PfxPath = IsPfx ? NullIfEmpty(PfxPath) : null,
        Pkcs11ModulePath = IsPkcs11 ? NullIfEmpty(ModulePath) : null,
        Pkcs11CertThumbprint = IsPkcs11 ? NullIfEmpty(Thumbprint) : null,
        TrustedSigningEndpoint = IsAzure ? NullIfEmpty(Endpoint) : null,
        TrustedSigningAccount = IsAzure ? NullIfEmpty(Account) : null,
        TrustedSigningProfile = IsAzure ? NullIfEmpty(Profile) : null,
        Secret = CredMode switch
        {
            CredMode.Pfx => NullIfEmpty(PfxPassword),
            CredMode.Pkcs11 => NullIfEmpty(Pin),
            _ => null,
        },
        Description = NullIfEmpty(Description),
        Url = NullIfEmpty(MoreInfoUrl),
        TimestampUrl = TimestampEnabled ? NullIfEmpty(TimestampUrl) : null,
        SignAllSignableFiles = false,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
