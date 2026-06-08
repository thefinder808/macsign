using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class CliPathTests
{
    // Every dir "exists" unless overridden — keeps the common cases terse.
    private static bool All(string _) => true;

    [Fact]
    public void Augment_appends_missing_existing_tool_dirs()
    {
        // The minimal launchd PATH a Finder-launched app gets — no Homebrew.
        var minimal = "/usr/bin:/bin:/usr/sbin:/sbin";
        var result = CliPath.Augment(minimal, ["/opt/homebrew/bin", "/usr/local/bin"], All);

        Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin", result);
    }

    [Fact]
    public void Augment_is_a_noop_when_dir_already_present()
    {
        // A terminal launch already has Homebrew on PATH — nothing should change.
        var withBrew = "/opt/homebrew/bin:/usr/bin:/bin";
        var result = CliPath.Augment(withBrew, ["/opt/homebrew/bin"], All);

        Assert.Equal(withBrew, result);
    }

    [Fact]
    public void Augment_is_idempotent()
    {
        var dirs = new[] { "/opt/homebrew/bin", "/usr/local/bin" };
        var once = CliPath.Augment("/usr/bin", dirs, All);
        var twice = CliPath.Augment(once, dirs, All);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Augment_skips_dirs_that_do_not_exist_on_disk()
    {
        // /usr/local/bin missing (Apple Silicon machine) — only the real one is added.
        bool exists(string d) => d == "/opt/homebrew/bin";
        var result = CliPath.Augment("/usr/bin", ["/opt/homebrew/bin", "/usr/local/bin"], exists);

        Assert.Equal("/usr/bin:/opt/homebrew/bin", result);
    }

    [Fact]
    public void Augment_handles_null_and_empty_current_path()
    {
        Assert.Equal("/opt/homebrew/bin", CliPath.Augment(null, ["/opt/homebrew/bin"], All));
        Assert.Equal("/opt/homebrew/bin", CliPath.Augment("", ["/opt/homebrew/bin"], All));
    }

    [Fact]
    public void Augment_does_not_duplicate_when_only_some_dirs_present()
    {
        var path = "/opt/homebrew/bin:/usr/bin";
        var result = CliPath.Augment(path, ["/opt/homebrew/bin", "/usr/local/bin"], All);

        // homebrew kept in place (not re-added), only the truly-missing one appended.
        Assert.Equal("/opt/homebrew/bin:/usr/bin:/usr/local/bin", result);
    }
}
