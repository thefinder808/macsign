using System;
using System.IO;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class UpdatePruneTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "macsign-prune-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PruneStaleDownloads_deletes_only_download_artifacts()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var stale = Path.Combine(dir, "macsign-update-abc-MacSign-1.0.0-osx-arm64.dmg");
        var unrelated = Path.Combine(dir, "something-else.dmg");
        File.WriteAllText(stale, "x");
        File.WriteAllText(unrelated, "y");

        UpdateService.PruneStaleDownloads(dir);

        Assert.False(File.Exists(stale));   // a prior abandoned download is cleaned up
        Assert.True(File.Exists(unrelated)); // unrelated temp files are left alone
    }

    [Fact]
    public void PruneStaleDownloads_is_safe_on_missing_dir()
    {
        // Best-effort: a non-existent dir must not throw.
        UpdateService.PruneStaleDownloads(
            Path.Combine(Path.GetTempPath(), "macsign-nope-" + Guid.NewGuid().ToString("N")));
    }
}
