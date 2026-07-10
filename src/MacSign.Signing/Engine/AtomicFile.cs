namespace MacSign.Signing.Engine;

/// <summary>
/// Writes a file in place without risking a torn result: write a sibling temp, fsync it,
/// carry over the original's permission bits, then atomically rename over the original.
/// </summary>
internal static class AtomicFile
{
    public static async Task WriteAsync(string file, byte[] content, CancellationToken ct = default)
    {
        var temp = TempName(file);
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(content, ct);
                fs.Flush(flushToDisk: true); // durability: bytes reach disk before the rename commits
            }
            PreserveMode(file, temp);
            File.Move(temp, file, overwrite: true); // same volume → atomic rename
        }
        finally
        {
            if (File.Exists(temp))
                try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    public static void Write(string file, byte[] content)
    {
        var temp = TempName(file);
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(content);
                fs.Flush(flushToDisk: true);
            }
            PreserveMode(file, temp);
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    // A randomized sibling name (not a predictable "file.signtmp") opened with CreateNew
    // (O_CREAT|O_EXCL) so a pre-planted file/symlink at the temp path can't be followed: the
    // open fails closed rather than writing through an attacker's symlink, and the random name
    // also denies a pre-plant/DoS on a known path. Staying in the target's directory keeps the
    // final rename an atomic same-volume operation.
    private static string TempName(string file) =>
        file + "." + Guid.NewGuid().ToString("N") + ".signtmp";

    // The rename swaps in a fresh inode, so carry the original's permission bits over.
    private static void PreserveMode(string original, string temp)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(original))
            try { File.SetUnixFileMode(temp, File.GetUnixFileMode(original)); } catch { /* best effort */ }
    }
}
