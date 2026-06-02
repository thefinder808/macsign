using MacSign.Signing.Formats.Pe;
using MacSign.Signing.Formats.Ps1;

namespace MacSign.Signing.Formats;

/// <summary>Maps a file path to the format that can sign it. PE and PowerShell.</summary>
internal static class FormatRegistry
{
    private static readonly ISignatureFormat[] Formats = [new PeFormat(), new Ps1Format()];

    /// <summary>The format for <paramref name="path"/>, or null if not implemented yet.</summary>
    public static ISignatureFormat? For(string path) =>
        Array.Find(Formats, f => f.CanHandle(path));
}
