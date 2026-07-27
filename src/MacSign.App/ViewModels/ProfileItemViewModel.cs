using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>One saved profile card.</summary>
public partial class ProfileItemViewModel : ObservableObject
{
    private readonly ProfilesViewModel _parent;
    public ProfileData Data { get; }

    public ProfileItemViewModel(ProfileData data, ProfilesViewModel parent)
    {
        Data = data;
        _parent = parent;
        _name = data.Name;
    }

    [ObservableProperty] private string _name;
    partial void OnNameChanged(string value)
    {
        Data.Name = value;
        _parent.Persist();
    }

    public bool IsPfx => Data.CredMode == "Pfx";
    public bool IsPkcs11 => Data.CredMode == "Pkcs11";
    public bool IsAzure => Data.CredMode == "Azure";

    public string Summary => Data.CredMode switch
    {
        "Pfx" => $"{FileName(Data.PfxPath, "pfx")} · {Ts}",
        "Pkcs11" => $"{FileName(Data.ModulePath, "token")}{Thumb} · {Ts}",
        _ => $"Azure · {Data.Profile ?? "profile"} · {Ts}",
    };

    public string LastUsedText =>
        string.IsNullOrEmpty(Data.LastUsedIso) ? "Not used yet"
        : DateTime.TryParse(Data.LastUsedIso, out var d) ? $"Used {d:MMM d}"
        : "";

    [RelayCommand] private void SignWith() => _parent.SignWith(this);
    [RelayCommand] private void Delete() => _parent.Delete(this);

    /// <summary>Copies the settings that live <i>on</i> a credential — never the identity
    /// fields, which by construction already matched (that's why this got called) — from a
    /// re-saved profile. Deliberately keeps <see cref="Name"/>: a rename was intentional.</summary>
    public void RefreshFrom(ProfileData src)
    {
        Data.Timestamp = src.Timestamp;
        Data.TimestampUrl = src.TimestampUrl;
        Data.Description = src.Description;
        Data.Url = src.Url;
        Data.LastUsedIso = src.LastUsedIso;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(LastUsedText));
    }

    private string Ts => Data.Timestamp ? "timestamped" : "no timestamp";

    /// <summary>" · " + the first 8 chars of the space-stripped thumbprint, or "" — keeps
    /// two certificates on the same PKCS#11 module distinguishable in the Profiles list.</summary>
    private string Thumb
    {
        get
        {
            var t = (Data.Thumbprint ?? "").Replace(" ", "");
            return t.Length == 0 ? "" : " · " + t[..Math.Min(8, t.Length)];
        }
    }

    private static string FileName(string? path, string fallback) =>
        string.IsNullOrWhiteSpace(path) ? fallback : Path.GetFileName(path);
}
