using MacSign.Signing.Formats.Pe;

namespace MacSign.Signing.Formats;

/// <summary>Maps a file path to the format that can sign it. Phase 1: PE only.</summary>
internal static class FormatRegistry
{
    private static readonly PeFormat Pe = new();

    /// <summary>The format for <paramref name="path"/>, or null if not implemented yet.</summary>
    public static ISignatureFormat? For(string path) =>
        Pe.CanHandle(path) ? Pe : null;
}
