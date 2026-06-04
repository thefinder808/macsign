using System;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class UpdateLaunchCheckTests
{
    [Theory]
    [InlineData(true,  null,                   true)]   // never checked → check
    [InlineData(true,  "2000-01-01T00:00:00Z", true)]   // long ago → check
    [InlineData(false, null,                   false)]  // auto off → never
    public void ShouldAutoCheck_cases(bool auto, string? lastIso, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.ShouldAutoCheck(auto, lastIso, DateTime.UtcNow));

    [Fact]
    public void ShouldAutoCheck_recent_isFalse()
        => Assert.False(MainWindowViewModel.ShouldAutoCheck(true, DateTime.UtcNow.AddHours(-1).ToString("o"), DateTime.UtcNow));
}
