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
using MacSign.Signing.Verification;

namespace MacSign.App.ViewModels;

/// <summary>
/// The Sign screen: a files list + a credential/options inspector, wired to the
/// real engine. Signs one file per <c>SignAsync</c> call so each row reports its
/// own success/failure. Secrets (password/PIN) are transient — never persisted.
/// </summary>
public partial class SignViewModel : ObservableObject
{
    /// <summary>The Trusted Signing endpoint field's default — also what a profile
    /// applies when it predates the <c>Endpoint</c> field (or was saved from a
    /// non-Azure mode, which never carries one).</summary>
    public const string DefaultEndpoint = "eus.codesigning.azure.net";

    private readonly EngineService _engine;
    private CancellationTokenSource? _cts;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    /// <summary>Raised when counts/credential change, so the shell can refresh
    /// the toolbar subtitle and the sidebar badge/credential card.</summary>
    public event Action? StateChanged;

    /// <param name="engine">Injectable for tests (a fake can script sign/verify
    /// outcomes); production passes none and gets the real engine façade.</param>
    public SignViewModel(EngineService? engine = null)
    {
        _engine = engine ?? new EngineService();
        Files.CollectionChanged += OnFilesChanged;
    }

    // ── credential mode ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPfx), nameof(IsPkcs11), nameof(IsAzure),
        nameof(CredBlurb), nameof(ActiveCredentialName), nameof(ActiveCredentialSub), nameof(CredentialReady))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private CredMode _credMode = CredMode.Pfx;

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveCredentialSub), nameof(CredentialReady))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private string _pfxPath = "";
    [ObservableProperty] private string _pfxPassword = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialReady))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private string _modulePath = "";
    [ObservableProperty] private string _thumbprint = "";
    [ObservableProperty] private string _pin = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveCredentialSub), nameof(CredentialReady))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private string _account = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialReady))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private string _profile = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialReady))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private string _endpoint = DefaultEndpoint;

    // ── which Azure identity signs ──
    // Left unset, Azure.Identity resolves to whatever answers first — on a Mac usually
    // whichever account `az login` last selected, which need not be the one holding the role.

    /// <summary>Entra tenant to authenticate against — a GUID or a domain. Blank = unpinned.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialReady), nameof(IsAzureSignedIn), nameof(AzureAccountName), nameof(AzureSignInLabel))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private string _tenantId = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialReady), nameof(IsAzureBrowserSource), nameof(IsAzureSignedIn), nameof(AzureSignInLabel))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private TrustedSigningCredentialSource _azureSource = TrustedSigningCredentialSource.Default;

    /// <summary>The remembered browser sign-in, or null when signed out. Holds no token.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialReady), nameof(IsAzureSignedIn), nameof(AzureAccountName), nameof(AzureSignInLabel))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(SaveProfileCommand))]
    private AzureSignInData? _azureSignIn;

    public bool IsAzureBrowserSource => AzureSource == TrustedSigningCredentialSource.InteractiveBrowser;

    /// <summary>
    /// Signed in <i>and</i> for the tenant this profile asks for. A sign-in belonging to a
    /// different tenant reads as signed out rather than being used anyway — silently signing
    /// as an identity the user didn't ask for is the bug this screen exists to prevent.
    /// </summary>
    public bool IsAzureSignedIn => AzureSignIn is { IsSignedIn: true } s && s.MatchesTenant(TenantId);

    /// <summary>Who a sign now goes out as, for the Sign screen's readback.</summary>
    public string AzureAccountName => IsAzureSignedIn ? AzureSignIn!.Username! : "";

    public string AzureSignInLabel => IsAzureSignedIn ? "Switch account" : "Sign in…";

    [RelayCommand]
    private void SetAzureSource(TrustedSigningCredentialSource source)
    {
        AzureSource = source;
        StateChanged?.Invoke();
    }

    /// <summary>Raised when the signed-in Azure account changes, so the shell can persist it.
    /// This VM holds no store of its own — the shell mediates, the same way it does for
    /// "Save as profile".</summary>
    public event Action? AzureSignInChanged;

    /// <summary>Records a completed sign-in, or null to sign out ("Switch account").</summary>
    public void ApplyAzureSignIn(AzureSignInData? signIn)
    {
        AzureSignIn = signIn;
        AzureSignInChanged?.Invoke();
        StateChanged?.Invoke();
    }

    // ── options ──
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _moreInfoUrl = "";
    [ObservableProperty] private bool _timestampEnabled = true;
    [ObservableProperty] private string _timestampUrl = "http://timestamp.digicert.com";

    // ── run state ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsSigning), nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand), nameof(ClearCommand),
        nameof(ClearSignedCommand), nameof(ToggleSelectAllCommand))]
    private SignState _signState = SignState.Idle;

    public bool IsIdle => SignState == SignState.Idle;
    public bool IsSigning => SignState == SignState.Signing;
    public bool IsDone => SignState == SignState.Done;

    [ObservableProperty] private string _bannerTitle = "";
    [ObservableProperty] private string _bannerDetail = "";
    [ObservableProperty] private bool _bannerIsError;
    [ObservableProperty] private string _signProgress = "";

    // ── derived counts ──
    public int FilesCount => Files.Count;
    public int SelectedCount => Files.Count(f => f.IsSelected && f.IsSelectable);
    public int SignedCount => Files.Count(f => f.IsSigned);
    public int ToSignCount => SelectedCount;
    public bool HasToSign => ToSignCount > 0;
    public bool HasFiles => Files.Count > 0;
    public bool HasNoFiles => Files.Count == 0;
    public string SubtitleText => $"{FilesCount} files · {SelectedCount} selected · {SignedCount} already signed";
    public string SignButtonText => ToSignCount == 1 ? "Sign 1 file" : $"Sign {ToSignCount} files";

    /// <summary>True when every selectable row is checked (drives the header toggle).</summary>
    public bool AllSelected => Files.Any(f => f.IsSelectable) && Files.All(f => !f.IsSelectable || f.IsSelected);
    /// <summary>There are already-signed rows that "Clear signed" can tidy away.</summary>
    public bool HasSignedToClear => !IsSigning && Files.Any(f => f.IsSigned);

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

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var candidates = paths
            .Where(_engine.IsSignable)
            .Where(p => !Files.Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0) return;

        // The "already signed" probe is a full verify (read + digest); scan off the UI thread
        // so a large file or a multi-file drop doesn't freeze the window.
        var scanned = await Task.Run(() => candidates
            .Select(p => new FileItemViewModel(p, _engine.IsAlreadySigned(p), SafeSize(p)))
            .ToList());

        foreach (var item in scanned)
        {
            if (Files.Any(f => string.Equals(f.Path, item.Path, StringComparison.OrdinalIgnoreCase))) continue;
            Files.Add(item);
        }
        if (SignState == SignState.Done) SignState = SignState.Idle;
        RaiseCounts();
    }

    private static long SafeSize(string p)
    {
        try { return new FileInfo(p).Length; } catch { return 0; }
    }

    [RelayCommand]
    private async Task AddFilesAsync() => await AddPathsAsync(await FileDialogs.PickSignablesAsync());

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await FileDialogs.PickFolderAsync();
        if (folder is null) return;
        var files = await Task.Run(() =>
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(_engine.IsSignable).ToList());
        await AddPathsAsync(files);
    }

    /// <summary>Remove one row from the list (✕ on hover / Delete key). No-op mid-run.</summary>
    [RelayCommand]
    private void RemoveFile(FileItemViewModel? item)
    {
        if (IsSigning || item is null) return;
        Files.Remove(item);                       // → OnFilesChanged → RaiseCounts()
        if (Files.Count == 0) ResetRunState();
    }

    /// <summary>Empty the whole list (header "Clear"). Acts as a reset when a run is Done.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Files.Clear();
        ResetRunState();
    }

    private bool CanClear() => !IsSigning && Files.Count > 0;

    /// <summary>Tidy away the dimmed already-signed rows, leaving only remaining work.</summary>
    [RelayCommand(CanExecute = nameof(HasSignedToClear))]
    private void ClearSigned()
    {
        foreach (var f in Files.Where(f => f.IsSigned).ToList())
            Files.Remove(f);
        if (Files.Count == 0) ResetRunState();
    }

    /// <summary>Check or uncheck every selectable row at once (header toggle).</summary>
    [RelayCommand(CanExecute = nameof(CanToggleAll))]
    private void ToggleSelectAll()
    {
        bool target = !AllSelected;
        foreach (var f in Files.Where(f => f.IsSelectable))
            f.IsSelected = target;                // each raises IsSelected → RaiseCounts()
    }

    private bool CanToggleAll() => !IsSigning && Files.Any(f => f.IsSelectable);

    /// <summary>Clear the result banner and drop back to Idle (when not mid-run).</summary>
    private void ResetRunState()
    {
        BannerTitle = "";
        BannerDetail = "";
        BannerIsError = false;
        if (SignState != SignState.Signing) SignState = SignState.Idle;
    }

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
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(SignButtonText));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(HasSignedToClear));
        SignCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        ClearSignedCommand.NotifyCanExecuteChanged();
        ToggleSelectAllCommand.NotifyCanExecuteChanged();
        StateChanged?.Invoke();
    }

    // ════════════════ signing ════════════════

    /// <summary>Raised when a run finishes, so the shell can record it in Activity.</summary>
    public event Action<RunData>? RunRecorded;

    /// <summary>The minimum fields for the active credential are filled in.</summary>
    public bool CredentialReady => CredMode switch
    {
        CredMode.Pfx => !string.IsNullOrWhiteSpace(PfxPath),
        CredMode.Pkcs11 => !string.IsNullOrWhiteSpace(ModulePath),
        // Requiring the sign-in here disables the button, rather than letting a batch start
        // and die on its first file with the same error repeated per row.
        _ => !string.IsNullOrWhiteSpace(Account) && !string.IsNullOrWhiteSpace(Profile) && !string.IsNullOrWhiteSpace(Endpoint)
             && (!IsAzureBrowserSource || IsAzureSignedIn),
    };

    private bool CanSign() => IsIdle && HasToSign && CredentialReady;

    [RelayCommand(CanExecute = nameof(CanSign))]
    private async Task SignAsync()
    {
        var targets = Files.Where(f => f.IsSelected && f.IsSelectable).ToList();
        if (targets.Count == 0) return;

        var options = BuildOptions();
        var signer = _engine.TryCreateSigner(options, out var error);
        if (signer is null)
        {
            BannerIsError = true;
            BannerTitle = "Couldn’t start signing";
            BannerDetail = error ?? "Unknown error.";
            SignState = SignState.Done;
            Record(targets.Count, "fail", error ?? "could not start");
            return;
        }

        SignState = SignState.Signing;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var log = new Progress<string>(_ => { /* per-file state drives the UI */ });

        int ok = 0;
        string? firstError = null;
        bool canceled = false;

        for (int i = 0; i < targets.Count; i++)
        {
            if (_cts.IsCancellationRequested) { canceled = true; break; }

            var file = targets[i];
            file.RunState = FileRunState.Signing;
            SignProgress = targets.Count == 1 ? "Signing…" : $"Signing {i + 1} of {targets.Count}…";

            SignResult result;
            try
            {
                // Offload the crypto/IO so the UI stays responsive.
                result = await Task.Run(() => _engine.SignOneAsync(signer, file.Path, options, log, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                file.RunState = FileRunState.None;
                canceled = true;
                break;
            }
            catch (Exception ex)
            {
                result = SignResult.Fail(ex.Message);
            }

            if (result.Success)
            {
                // The success banner claims "verified VALID" — so actually re-verify the file
                // we just wrote rather than trusting the sign call. A signing bug or write
                // corruption must NOT be reported as verified. Integrity only (SignatureValid),
                // independent of chain trust: legitimate self-signed certs are untrusted yet
                // produce a VALID signature, which is exactly what the banner asserts.
                VerifyReport vr;
                try { vr = await Task.Run(() => _engine.Verify(file.Path)); }
                catch (Exception ex) { vr = VerifyReport.Failed(ex.Message); }

                if (vr.Error is null && vr.IsSigned && vr.SignatureValid)
                {
                    file.RunState = FileRunState.Done;
                    ok++;
                }
                else
                {
                    file.RunState = FileRunState.None;
                    firstError ??= vr.Error ?? "signature did not verify after signing";
                }
            }
            else { file.RunState = FileRunState.None; firstError ??= result.Error; }
        }

        SignProgress = "";
        SignState = SignState.Done;

        if (canceled)
        {
            BannerIsError = true;
            BannerTitle = "Signing canceled";
            BannerDetail = ok > 0 ? $"{ok} file{(ok == 1 ? "" : "s")} signed before canceling." : "No files were signed.";
            Record(ok, "warn", $"canceled after {ok}/{targets.Count}");
            return;
        }

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

        var status = ok == 0 ? "fail" : ok == targets.Count ? "ok" : "warn";
        var detail = ok == targets.Count
            ? (TimestampEnabled ? "signed + timestamped" : "signed")
            : ok == 0 ? (firstError ?? "failed") : $"{ok}/{targets.Count} — {firstError}";
        Record(ok > 0 ? ok : targets.Count, status, detail);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void Record(int count, string status, string detail) =>
        RunRecorded?.Invoke(new RunData
        {
            FileCount = count,
            Credential = CredentialLabel,
            Detail = detail,
            Status = status,
            WhenIso = DateTime.Now.ToString("o"),
        });

    private string CredentialLabel => CredMode switch
    {
        CredMode.Pfx => $"PFX · {(string.IsNullOrWhiteSpace(PfxPath) ? "pfx" : Path.GetFileName(PfxPath))}",
        CredMode.Pkcs11 => "PKCS#11 token",
        _ => $"Azure · {Profile}",
    };

    // ── Profiles interop ──

    /// <summary>Raised by "Save as profile" on the Sign screen. The shell owns the Profiles
    /// collection, so it performs the actual add — this VM only reports the intent.</summary>
    public event Action? SaveProfileRequested;

    [RelayCommand(CanExecute = nameof(CredentialReady))]
    private void SaveProfile() => SaveProfileRequested?.Invoke();

    /// <summary>A profile name that distinguishes it from other profiles in the same mode
    /// (unlike <see cref="ActiveCredentialName"/>, which is the same for every Azure
    /// profile) — the file/module basename, or the Azure account.</summary>
    private string DefaultProfileName => CredMode switch
    {
        CredMode.Pfx => string.IsNullOrWhiteSpace(PfxPath)
            ? "PFX file"
            : Path.GetFileNameWithoutExtension(PfxPath),
        CredMode.Pkcs11 => string.IsNullOrWhiteSpace(ModulePath)
            ? "PKCS#11 token"
            : Path.GetFileNameWithoutExtension(ModulePath),
        _ => string.IsNullOrWhiteSpace(Account) ? "Azure Trusted Signing" : Account,
    };

    /// <summary>Snapshots only the fields that belong to the active credential mode —
    /// mirrors <see cref="BuildOptions"/> — so switching modes never leaks a stale
    /// PFX path into an Azure profile or vice versa.</summary>
    public ProfileData CreateProfileSnapshot() => new()
    {
        Name = DefaultProfileName,
        CredMode = CredMode.ToString(),
        PfxPath    = IsPfx    ? NullIfEmpty(PfxPath)    : null,
        ModulePath = IsPkcs11 ? NullIfEmpty(ModulePath) : null,
        Thumbprint = IsPkcs11 ? NullIfEmpty(Thumbprint) : null,
        Account    = IsAzure  ? NullIfEmpty(Account)    : null,
        Profile    = IsAzure  ? NullIfEmpty(Profile)    : null,
        Endpoint   = IsAzure  ? NullIfEmpty(Endpoint)   : null,
        TenantId   = IsAzure  ? NullIfEmpty(TenantId)   : null,
        CredentialSource = (IsAzure ? AzureSource : TrustedSigningCredentialSource.Default).ToString(),
        Timestamp = TimestampEnabled,
        TimestampUrl = NullIfEmpty(TimestampUrl),
        Description = NullIfEmpty(Description),
        Url = NullIfEmpty(MoreInfoUrl),
        LastUsedIso = DateTime.Now.ToString("o"),
    };

    public void ApplyProfile(ProfileData p)
    {
        CredMode = p.CredMode switch { "Pfx" => CredMode.Pfx, "Pkcs11" => CredMode.Pkcs11, _ => CredMode.Azure };
        PfxPath = p.PfxPath ?? "";
        ModulePath = p.ModulePath ?? "";
        Thumbprint = p.Thumbprint ?? "";
        Account  = p.Account  ?? "";
        Profile  = p.Profile  ?? "";
        Endpoint = p.Endpoint ?? DefaultEndpoint;
        TenantId = p.TenantId ?? "";
        AzureSource = p.CredentialSource == nameof(TrustedSigningCredentialSource.InteractiveBrowser)
            ? TrustedSigningCredentialSource.InteractiveBrowser
            : TrustedSigningCredentialSource.Default;
        TimestampEnabled = p.Timestamp;
        // Deliberate exception: profiles predating TimestampUrl carry null. Blanking the
        // URL would drop the TSA while the toggle still reads "on".
        if (p.TimestampUrl is not null) TimestampUrl = p.TimestampUrl;
        Description = p.Description ?? "";
        MoreInfoUrl = p.Url ?? "";
        StateChanged?.Invoke();
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
        TrustedSigningTenantId = IsAzure ? NullIfEmpty(TenantId) : null,
        TrustedSigningCredentialSource = IsAzure ? AzureSource : TrustedSigningCredentialSource.Default,
        // Scoped to the browser source on purpose: switching back to the default sign-in must
        // actually use it, not keep quietly signing as the browser account.
        TrustedSigningAuthRecord = IsAzure && IsAzureBrowserSource ? AzureSignIn?.ToRecordJson() : null,
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
