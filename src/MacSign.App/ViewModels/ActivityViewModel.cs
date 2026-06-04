using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>Activity screen: recent signing runs (metadata only — secrets are
/// never logged), persisted via <see cref="SettingsStore"/>. The history cap is
/// driven by <see cref="AppPrefs.ActivityKeepLast"/> (0 = unlimited).</summary>
public partial class ActivityViewModel : ObservableObject
{
    private readonly AppData _data;
    private readonly SettingsStore _store;

    public ObservableCollection<RunItemViewModel> Runs { get; } = new();
    public bool HasRuns => Runs.Count > 0;
    public bool IsEmpty => Runs.Count == 0;

    public ActivityViewModel(AppData data, SettingsStore store)
    {
        _data = data;
        _store = store;
        foreach (var r in data.Activity)
            Runs.Add(new RunItemViewModel(r));
    }

    public void Record(RunData r)
    {
        _data.Activity.Insert(0, r);
        Runs.Insert(0, new RunItemViewModel(r));
        Trim();
        Persist();
    }

    /// <summary>Re-apply the current cap (e.g. after the user lowers "keep last N").</summary>
    public void ReTrim()
    {
        Trim();
        Persist();
    }

    /// <summary>Empty the Activity history.</summary>
    public void Clear()
    {
        _data.Activity.Clear();
        Runs.Clear();
        Persist();
    }

    private void Trim()
    {
        int cap = _data.Preferences.ActivityKeepLast;
        if (cap <= 0) return;   // 0 = unlimited
        while (_data.Activity.Count > cap) _data.Activity.RemoveAt(_data.Activity.Count - 1);
        while (Runs.Count > cap) Runs.RemoveAt(Runs.Count - 1);
    }

    private void Persist()
    {
        _store.Save(_data);
        OnPropertyChanged(nameof(HasRuns));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
