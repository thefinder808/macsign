using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class AppInfoTests
{
    [Fact]
    public void ParseShortVersion_extracts_marketing_version()
    {
        var plist = "<plist><dict>" +
            "<key>CFBundleVersion</key><string>0.6.0</string>" +
            "<key>CFBundleShortVersionString</key><string>0.6.0</string>" +
            "</dict></plist>";
        Assert.Equal("0.6.0", AppInfo.ParseShortVersion(plist));
    }

    [Fact]
    public void ParseShortVersion_returns_null_when_absent()
    {
        Assert.Null(AppInfo.ParseShortVersion("<plist><dict></dict></plist>"));
    }

    [Fact]
    public void ParseShortVersion_ignores_CFBundleVersion_key()
    {
        // CFBundleVersion shares a prefix with the key we want — must NOT match it.
        var plist = "<plist><dict><key>CFBundleVersion</key><string>9.9.9</string></dict></plist>";
        Assert.Null(AppInfo.ParseShortVersion(plist));
    }

    [Fact]
    public void ParseShortVersion_handles_formatting_whitespace()
    {
        var plist = "<dict>\n  <key>CFBundleShortVersionString</key>\n  <string>1.2.3</string>\n</dict>";
        Assert.Equal("1.2.3", AppInfo.ParseShortVersion(plist));
    }
}
