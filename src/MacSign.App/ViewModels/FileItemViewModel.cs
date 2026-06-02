using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacSign.App.ViewModels;

/// <summary>One row in the Sign files list.</summary>
public partial class FileItemViewModel : ObservableObject
{
    public string Path { get; }
    public string Name { get; }
    public string Ext { get; }       // lowercase, no dot (exe/dll/sys/msi/ps1)
    public string SizeText { get; }

    public FileItemViewModel(string path, bool isSigned, long sizeBytes)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        SizeText = FormatSize(sizeBytes);
        _isSigned = isSigned;
        // Pre-signed files start deselected; unsigned start selected (the common case).
        _isSelected = !isSigned;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectable), nameof(RowOpacity),
        nameof(ShowSignedPill), nameof(ShowUnsignedText), nameof(ShowCheckbox))]
    private bool _isSigned;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectable), nameof(ShowCheckbox),
        nameof(ShowSpinner), nameof(ShowDoneCheck),
        nameof(ShowDonePill), nameof(ShowUnsignedText), nameof(ShowSigningText))]
    private FileRunState _runState;

    /// <summary>Only unsigned, not-yet-run files can be checked.</summary>
    public bool IsSelectable => !IsSigned && RunState == FileRunState.None;
    public double RowOpacity => IsSigned ? 0.6 : 1.0;

    // ── checkbox column content ──
    public bool ShowCheckbox => IsSelectable;
    public bool ShowSpinner => RunState == FileRunState.Signing;
    public bool ShowDoneCheck => RunState == FileRunState.Done || IsSigned;

    // ── status column content ──
    public bool ShowSignedPill => IsSigned && RunState == FileRunState.None;  // pre-existing
    public bool ShowDonePill => RunState == FileRunState.Done;                // "Signed now"
    public bool ShowSigningText => RunState == FileRunState.Signing;          // "Signing…"
    public bool ShowUnsignedText => !IsSigned && RunState == FileRunState.None;

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
