using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MacSign.App.Services;

/// <summary>App identity bits for the About box. The version comes from the .app
/// bundle's Info.plist (CFBundleShortVersionString, stamped by build-macos.sh) when
/// packaged, falling back to the assembly version in dev.</summary>
public static class AppInfo
{
    public static string Version
    {
        get
        {
            try
            {
                // In a packaged .app the executable is at Contents/MacOS/, so the
                // Info.plist sits one directory up.
                var plist = Path.Combine(AppContext.BaseDirectory, "..", "Info.plist");
                if (File.Exists(plist) && ParseShortVersion(File.ReadAllText(plist)) is { } v
                    && !string.IsNullOrWhiteSpace(v))
                    return v;
            }
            catch { /* fall through to the assembly version */ }
            var asm = Assembly.GetExecutingAssembly().GetName().Version;
            return asm is null ? "dev" : $"{asm.Major}.{asm.Minor}.{asm.Build}";
        }
    }

    private static readonly Regex ShortVersionRegex =
        new(@"<key>\s*CFBundleShortVersionString\s*</key>\s*<string>([^<]+)</string>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Extract CFBundleShortVersionString from an Info.plist's XML; null when absent.</summary>
    public static string? ParseShortVersion(string plistXml)
    {
        var m = ShortVersionRegex.Match(plistXml);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
