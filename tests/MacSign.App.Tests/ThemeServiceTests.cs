using Avalonia.Styling;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class ThemeServiceTests
{
    [Fact] public void Light_maps_to_Light()   => Assert.Equal(ThemeVariant.Light,   ThemeService.ToVariant("Light"));
    [Fact] public void Dark_maps_to_Dark()      => Assert.Equal(ThemeVariant.Dark,    ThemeService.ToVariant("Dark"));
    [Fact] public void System_maps_to_Default() => Assert.Equal(ThemeVariant.Default, ThemeService.ToVariant("System"));

    [Fact]
    public void Null_or_unknown_maps_to_Default()
    {
        Assert.Equal(ThemeVariant.Default, ThemeService.ToVariant(null));
        Assert.Equal(ThemeVariant.Default, ThemeService.ToVariant("nonsense"));
    }
}
