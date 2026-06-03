using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>
/// Verify screen. Runs <c>SignatureVerifier.Verify</c> and surfaces the report,
/// keeping the engine's core distinction: integrity is asserted authoritatively,
/// while chain trust is "not validated on this OS" (Microsoft roots absent).
/// </summary>
public partial class VerifyViewModel : ObservableObject
{
    private readonly EngineService _engine = new();
    private readonly AppleSigningService _apple;

    public VerifyViewModel(AppleSigningService? apple = null) => _apple = apple ?? new();

    /// <summary>Raised when a report appears/clears, so the shell can toggle
    /// the "Verify another" toolbar action.</summary>
    public event Action? ReportChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasReport;

    public bool IsEmpty => !HasReport;

    // ── report fields ──
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _ext = "";
    [ObservableProperty] private string _fileMeta = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IntegrityBad))]
    private bool _integrityValid;
    public bool IntegrityBad => !IntegrityValid;

    [ObservableProperty] private string _integrityHeadline = "";
    [ObservableProperty] private string _integrityDetail = "";
    [ObservableProperty] private string _signer = "";
    [ObservableProperty] private string _issuer = "";
    [ObservableProperty] private string _serial = "";
    [ObservableProperty] private bool _hasTimestamp;
    [ObservableProperty] private string _timestampText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChainBad))]
    private bool _chainTrusted;
    public bool ChainBad => !ChainTrusted;

    [ObservableProperty] private string _chainText = "";
    [ObservableProperty] private string _chainNote = "";
    [ObservableProperty] private bool _hasChainNote;

    // ── Mac (codesign) report fields; shown when IsMacReport ──
    [ObservableProperty] private bool _isMacReport;
    [ObservableProperty] private string _macTeamId = "";
    [ObservableProperty] private bool _macHardened;
    [ObservableProperty] private bool _macStapled;
    [ObservableProperty] private bool _macGatekeeper;

    public async Task VerifyPathAsync(string path)
    {
        // A .app / .dmg is an Apple artifact — verify it via codesign/spctl/stapler
        // instead of the Authenticode engine, and show the Mac report block.
        if (AppleSigningService.Classify(path) is AppleTargetKind.App or AppleTargetKind.Dmg)
        {
            var mac = await Task.Run(() => _apple.InspectAsync(path, default));
            IsMacReport = true;
            FileName = Path.GetFileName(path);
            Ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            FileMeta = mac.Kind == AppleTargetKind.Dmg ? "Disk image" : "macOS app bundle";
            IntegrityValid = mac.Valid;
            IntegrityHeadline = mac.Valid ? "Signature VALID" : "Not validly signed";
            IntegrityDetail = mac.Valid
                ? "codesign --verify --deep --strict passes"
                : "codesign could not validate the signature";
            Signer = Dash(mac.Signer);
            MacTeamId = Dash(mac.TeamId);
            MacHardened = mac.HardenedRuntime;
            MacStapled = mac.Stapled;
            MacGatekeeper = mac.GatekeeperAccepted;
            HasReport = true;
            ReportChanged?.Invoke();
            return;
        }
        IsMacReport = false;

        // Offload the read + digest + chain build so a large file (or a multi-file drop)
        // doesn't freeze the UI thread. The engine never throws, so a bad/unreadable file
        // comes back as a Failed report rather than crashing here.
        var r = await Task.Run(() => _engine.Verify(path));
        long size = 0;
        try { size = new FileInfo(path).Length; } catch { /* best-effort */ }

        FileName = Path.GetFileName(path);
        Ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        FileMeta = $"{DescribeType(Ext)} · {FormatSize(size)}";

        bool isError = r.Error is not null;
        IntegrityValid = r.IsSigned && r.SignatureValid;
        IntegrityHeadline = isError ? "Couldn’t verify"
            : !r.IsSigned ? "Not signed"
            : r.SignatureValid ? "Integrity VALID" : "Integrity INVALID";
        IntegrityDetail = isError ? r.Error!
            : !r.IsSigned ? "No Authenticode signature present"
            : r.SignatureValid ? "Unmodified — signer signature verifies"
            : "Modified — signer signature does not verify";

        Signer = Dash(r.SignerSubject);
        Issuer = Dash(r.SignerIssuer);
        Serial = Dash(r.SignerSerialNumber);

        HasTimestamp = r.Timestamp is not null;
        TimestampText = r.Timestamp is { } ts
            ? $"{ts.ToUniversalTime():yyyy-MM-dd HH:mm} UTC · RFC 3161"
            : "None";

        ChainTrusted = r.ChainTrusted;
        ChainText = r.ChainTrusted ? "Trusted on this OS" : "Not validated on this OS";
        ChainNote = r.ChainNote ?? "";
        HasChainNote = !string.IsNullOrWhiteSpace(ChainNote);

        HasReport = true;
        ReportChanged?.Invoke();
    }

    [RelayCommand]
    private async Task PickAndVerifyAsync()
    {
        var p = await FileDialogs.PickOneAsync(
            "Verify a file", new[] { "*.exe", "*.dll", "*.sys", "*.msi", "*.ps1", "*.dmg" });
        if (p is not null) await VerifyPathAsync(p);
        // (.app bundles are folders — drop them on the window to verify.)
    }

    [RelayCommand]
    private void VerifyAnother()
    {
        HasReport = false;
        ReportChanged?.Invoke();
    }

    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private static string DescribeType(string ext) => ext switch
    {
        "exe" => "PE executable",
        "dll" => "PE library",
        "sys" => "Kernel driver",
        "msi" => "Windows Installer",
        "ps1" => "PowerShell script",
        _ => ext.ToUpperInvariant(),
    };

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
