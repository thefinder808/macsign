using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Save a profile — matching an existing one on key material (see
    /// <see cref="ProfileData.SameCredentialAs"/>) updates its settings in place instead of
    /// stacking a duplicate; no match adds a new card.</summary>
    public void Save(ProfileData p)
    {
        var existing = Profiles.FirstOrDefault(item => item.Data.SameCredentialAs(p));
        if (existing is not null)
        {
            existing.RefreshFrom(p);
            // The card's own name, not the caller's generated one: RefreshFrom keeps a rename,
            // so echoing the incoming name would announce a profile that isn't on screen.
            Announce($"Updated “{existing.Name}”");
        }
        else
        {
            _data.Profiles.Add(p);
            Profiles.Add(new ProfileItemViewModel(p, this));
            Announce($"Saved “{p.Name}”");
        }
        Persist();
    }

    // ── Save confirmation ──────────────────────────────────────────────────────
    // Navigating here was the only signal that a save happened, which reads as nothing at all
    // once you have a few cards: no way to tell which is new, or whether the click landed.
    // Task.Delay rather than Dispatcher — the App tests never initialise Avalonia.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedNotice))]
    private string _savedNotice = "";

    public bool HasSavedNotice => !string.IsNullOrEmpty(SavedNotice);

    private CancellationTokenSource? _noticeCts;

    private void Announce(string message)
    {
        SavedNotice = message;
        _noticeCts?.Cancel();
        _noticeCts = new CancellationTokenSource();
        _ = ClearNoticeAfterDelay(_noticeCts.Token);
    }

    private async Task ClearNoticeAfterDelay(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(6), ct); }
        catch (OperationCanceledException) { return; }
        SavedNotice = "";
    }

    public void Delete(ProfileItemViewModel item)
    {
        _data.Profiles.Remove(item.Data);
        Profiles.Remove(item);
        Persist();
    }

    /// <summary>Remove every profile (used by Preferences → Reset all settings).</summary>
    public void Clear()
    {
        _data.Profiles.Clear();
        Profiles.Clear();
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
