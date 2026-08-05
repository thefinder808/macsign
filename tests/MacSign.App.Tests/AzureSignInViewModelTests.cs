using System;
using System.Threading;
using System.Threading.Tasks;
using MacSign.App.ViewModels;
using MacSign.Signing.Azure;
using Xunit;

namespace MacSign.App.Tests;

/// <summary>
/// The sign-in dialog. The browser leg can't run headlessly, so the flow is driven through the
/// injectable authenticate delegate; what's covered here is everything around it — the result
/// mapping, cancellation, and how failures are surfaced.
/// </summary>
public class AzureSignInViewModelTests
{
    private const string RecordJson = """
        {"username":"chosen@contoso.com","authority":"https://login.microsoftonline.com/tenant-a",
         "homeAccountId":"h","tenantId":"tenant-a","clientId":"c","version":"1.0"}
        """;

    [Fact]
    public async Task A_completed_sign_in_reports_the_chosen_account()
    {
        var vm = new AzureSignInViewModel("tenant-a",
            (_, _) => Task.FromResult(new AzureSignInResult("chosen@contoso.com", "tenant-a", RecordJson)));
        var succeeded = 0;
        vm.Succeeded += () => succeeded++;

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal(1, succeeded);
        Assert.Equal("chosen@contoso.com", vm.Result?.Username);
        Assert.False(vm.HasError);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task The_tenant_is_passed_through_and_blank_means_unset()
    {
        string? seen = "sentinel";
        var vm = new AzureSignInViewModel("   ", (t, _) =>
        {
            seen = t;
            return Task.FromResult(new AzureSignInResult("u", "t", RecordJson));
        });

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Null(seen);   // not "" — null means "the account's home tenant"
    }

    [Fact]
    public async Task A_failure_is_surfaced_verbatim()
    {
        // Entra's own wording — an AADSTS code, a consent requirement, a blocked tenant — is
        // far more actionable than any paraphrase, and paraphrasing is how a fixable problem
        // turns into a mystery. AADSTS65001 (no consent for the signing scope) is the specific
        // one worth being able to read.
        var vm = new AzureSignInViewModel(null,
            (_, _) => throw new InvalidOperationException("AADSTS65001: The user or administrator has not consented"));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains("AADSTS65001", vm.Error);
        Assert.True(vm.HasError);
        Assert.Null(vm.Result);
        Assert.False(vm.Busy);       // the button comes back, so it can be retried
    }

    [Fact]
    public async Task Cancelling_reads_as_cancelled_rather_than_as_an_error()
    {
        var vm = new AzureSignInViewModel(null,
            (_, ct) => Task.FromException<AzureSignInResult>(new OperationCanceledException(ct)));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Sign-in cancelled.", vm.Error);
        Assert.Null(vm.Result);
    }

    [Fact]
    public async Task Cancel_releases_a_sign_in_still_waiting_on_the_browser()
    {
        // Closing the browser tab returns nothing, so without this the dialog would sit on
        // "Waiting for browser…" forever with no way out.
        var started = new TaskCompletionSource();
        var vm = new AzureSignInViewModel(null, async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable — the delay above always cancels");
        });

        var run = vm.SignInCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.Busy);

        vm.CancelCommand.Execute(null);
        await run;

        Assert.False(vm.Busy);
        Assert.Equal("Sign-in cancelled.", vm.Error);
    }
}
