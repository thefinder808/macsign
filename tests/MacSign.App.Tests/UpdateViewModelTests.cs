using System.Net.Http;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class UpdateViewModelTests
{
    private static UpdateInfo Info => new("9.9.9", "Notes here", "https://example.test/rel", "a.dmg", "https://example.test/a.dmg");

    private static SettingsStore TempStore() =>
        new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macsign-uvm-" + System.Guid.NewGuid().ToString("N")));

    [Fact]
    public void Skip_recordsVersion_inPrefs()
    {
        var data = new AppData();
        var vm = new UpdateViewModel(Info, new UpdateService(new HttpClient(new FakeHttp())), data, TempStore());
        vm.SkipCommand.Execute(null);
        Assert.Equal("9.9.9", data.Preferences.SkippedVersion);
    }

    [Fact]
    public void Presents_versionAndNotes()
    {
        var vm = new UpdateViewModel(Info, new UpdateService(new HttpClient(new FakeHttp())), new AppData(), TempStore());
        Assert.Contains("9.9.9", vm.Title);
        Assert.Equal("Notes here", vm.ReleaseNotes);
    }
}
