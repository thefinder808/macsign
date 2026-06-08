namespace MacSign.App.Services;

/// <summary>
/// Ensures CLI tools that live in Homebrew prefixes are reachable on the process
/// <c>PATH</c>. A macOS app launched from Finder/Dock inherits only the minimal
/// launchd PATH (<c>/usr/bin:/bin:/usr/sbin:/sbin</c>), so a Homebrew-installed
/// <c>az</c> is invisible — which makes Azure.Identity's <c>AzureCliCredential</c>
/// report "Azure CLI not installed" even after a successful <c>az login</c>, and
/// the Azure Trusted Signing credential chain fails. Restoring the standard tool
/// dirs lets the existing <c>DefaultAzureCredential</c> shell out to <c>az</c>.
/// (A terminal launch already inherits these, so this is a no-op there.)
/// </summary>
public static class CliPath
{
    /// <summary>Homebrew bin dirs: Apple Silicon (/opt/homebrew) and Intel (/usr/local).</summary>
    private static readonly string[] MacToolDirs = ["/opt/homebrew/bin", "/usr/local/bin"];

    /// <summary>
    /// Adds the standard tool dirs to the current macOS process PATH so child
    /// processes (e.g. the <c>az</c> invocation inside DefaultAzureCredential)
    /// inherit them. macOS-only, idempotent — safe to call once at startup.
    /// </summary>
    public static void EnsureToolPath()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var current = Environment.GetEnvironmentVariable("PATH");
        var augmented = Augment(current, MacToolDirs, Directory.Exists);
        if (!string.Equals(augmented, current, StringComparison.Ordinal))
            Environment.SetEnvironmentVariable("PATH", augmented);
    }

    /// <summary>
    /// Returns <paramref name="currentPath"/> with each tool dir that exists on disk and
    /// is not already listed appended to the end. Pure (no environment access) for tests:
    /// existence is supplied via <paramref name="dirExists"/>. Appending (not prepending)
    /// keeps system binaries winning over Homebrew shadows; order is preserved and the
    /// result is duplicate-free, so repeated application is a no-op.
    /// </summary>
    public static string Augment(string? currentPath, IEnumerable<string> toolDirs, Func<string, bool> dirExists)
    {
        var existing = (currentPath ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var present = new HashSet<string>(existing, StringComparer.Ordinal);

        var toAdd = toolDirs.Where(d => !present.Contains(d) && dirExists(d)).ToList();
        if (toAdd.Count == 0)
            return currentPath ?? string.Empty;

        return string.Join(Path.PathSeparator, existing.Concat(toAdd));
    }
}
