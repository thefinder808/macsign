using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>Profiles screen: reusable credential + option presets (no secrets),
/// persisted via <see cref="SettingsStore"/>.</summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly AppData _data;
    private readonly SettingsStore _store;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();
    public bool HasProfiles => Profiles.Count > 0;
    public bool IsEmpty => Profiles.Count == 0;

    /// <summary>Raised when a profile's "Sign with…" is clicked.</summary>
    public event Action<ProfileData>? SignWithRequested;

    public ProfilesViewModel(AppData data, SettingsStore store)
    {
        _data = data;
        _store = store;
        foreach (var p in data.Profiles)
            Profiles.Add(new ProfileItemViewModel(p, this));
        Profiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasProfiles));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public void Add(ProfileData p)
    {
        _data.Profiles.Add(p);
        Profiles.Add(new ProfileItemViewModel(p, this));
        Persist();
    }

    public void Delete(ProfileItemViewModel item)
    {
        _data.Profiles.Remove(item.Data);
        Profiles.Remove(item);
        Persist();
    }

    public void SignWith(ProfileItemViewModel item)
    {
        item.Data.LastUsedIso = DateTime.Now.ToString("o");
        Persist();
        SignWithRequested?.Invoke(item.Data);
    }

    public void Persist() => _store.Save(_data);
}
