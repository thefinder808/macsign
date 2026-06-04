using System;
using System.IO;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class ProfilesViewModelTests
{
    [Fact]
    public void Clear_empties_profiles_and_persists()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-prof-" + Guid.NewGuid().ToString("N")));
        var data = new AppData();
        var vm = new ProfilesViewModel(data, store);
        vm.Add(new ProfileData { Name = "p1" });
        Assert.True(vm.HasProfiles);

        vm.Clear();

        Assert.Empty(vm.Profiles);
        Assert.True(vm.IsEmpty);
        Assert.Empty(store.Load().Profiles);
    }
}
