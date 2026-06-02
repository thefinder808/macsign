using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MacSign.App.Services;

namespace MacSign.App.ViewModels;

/// <summary>Activity screen: recent signing runs (metadata only — secrets are
/// never logged), persisted via <see cref="SettingsStore"/>.</summary>
public partial class ActivityViewModel : ObservableObject
{
    private const int Cap = 50;

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
        while (_data.Activity.Count > Cap) _data.Activity.RemoveAt(_data.Activity.Count - 1);
        while (Runs.Count > Cap) Runs.RemoveAt(Runs.Count - 1);
        _store.Save(_data);
        OnPropertyChanged(nameof(HasRuns));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
