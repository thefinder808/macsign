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
        "Pkcs11" => $"token · {Ts}",
        _ => $"Azure · {Data.Profile ?? "profile"} · {Ts}",
    };

    public string LastUsedText =>
        string.IsNullOrEmpty(Data.LastUsedIso) ? "Not used yet"
        : DateTime.TryParse(Data.LastUsedIso, out var d) ? $"Used {d:MMM d}"
        : "";

    [RelayCommand] private void SignWith() => _parent.SignWith(this);
    [RelayCommand] private void Delete() => _parent.Delete(this);

    private string Ts => Data.Timestamp ? "timestamped" : "no timestamp";
    private static string FileName(string? path, string fallback) =>
        string.IsNullOrWhiteSpace(path) ? fallback : Path.GetFileName(path);
}
