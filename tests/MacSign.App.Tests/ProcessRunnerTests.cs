using System.Threading;
using System.Threading.Tasks;
using MacSign.App.Services;

namespace MacSign.App.Tests;

/// <summary>
/// Real-process tests for <see cref="ProcessRunner"/> — the injection firewall.
/// Uses /bin tools present on every macOS runner.
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public async Task Captures_stdout_and_exit_code()
    {
        var r = await new ProcessRunner().RunAsync("/bin/echo", new[] { "hello world" }, null, default);

        Assert.True(r.Success);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("hello world", r.StdOut);
    }

    [Fact]
    public async Task Arguments_are_passed_literally_no_shell_interpretation()
    {
        // If this were routed through a shell, the `;`, `$()` and backticks would
        // execute. With ArgumentList it must be echoed back verbatim.
        const string payload = "safe ; rm -rf /tmp/nope $(whoami) `id`";
        var r = await new ProcessRunner().RunAsync("/bin/echo", new[] { payload }, null, default);

        Assert.True(r.Success);
        Assert.Equal(payload, r.StdOut.Trim());
    }

    [Fact]
    public async Task Nonzero_exit_is_reported_not_thrown()
    {
        // `false` exits 1.
        var r = await new ProcessRunner().RunAsync("/usr/bin/false", System.Array.Empty<string>(), null, default);

        Assert.False(r.Success);
        Assert.NotEqual(0, r.ExitCode);
        Assert.False(r.Canceled);
    }

    [Fact]
    public async Task Missing_tool_is_reported_not_thrown()
    {
        var r = await new ProcessRunner().RunAsync("/nope/not-a-real-tool", System.Array.Empty<string>(), null, default);

        Assert.False(r.Success);
    }

    [Fact]
    public async Task Cancellation_kills_the_process()
    {
        using var cts = new CancellationTokenSource();
        var task = new ProcessRunner().RunAsync("/bin/sleep", new[] { "30" }, null, cts.Token);
        cts.CancelAfter(200);

        var r = await task;

        Assert.True(r.Canceled);
    }
}
