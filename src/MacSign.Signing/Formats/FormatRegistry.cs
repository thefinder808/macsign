using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Formats.Ps1;

namespace MacSign.Signing.Formats;

/// <summary>Maps a file path to the format that can sign it. PE + PowerShell built in; MSI via a backend.</summary>
internal static class FormatRegistry
{
    private static readonly ISignatureFormat[] BuiltIn = [new PeFormat(), new Ps1Format()];

    /// <summary>The format for <paramref name="path"/>, or null if not implemented/loaded.</summary>
    public static ISignatureFormat? For(string path)
    {
        var builtIn = Array.Find(BuiltIn, f => f.CanHandle(path));
        if (builtIn is not null)
            return builtIn;

        if (FormatBackends.Msi is { } msi && msi.CanHandle(path))
            return msi;

        return null;
    }
}
