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
    public async Task A_failure_with_no_tenant_says_to_try_setting_one()
    {
        // Hit for real on first use. Leaving the tenant blank means "the account's own home
        // directory", which for a personal Microsoft account is the consumer tenant
        // ("Microsoft Services") — never where an Azure signing account lives. Entra's reply
        // is accurate and useless: it names a tenant you've never heard of and suggests adding
        // yourself as an external user, when the actual fix is one field on the screen behind.
        var vm = new AzureSignInViewModel(null,
            (_, _) => throw new InvalidOperationException(
                "Selected user account does not exist in tenant 'Microsoft Services'"));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains("Microsoft Services", vm.Error);     // Entra's own words survive
        Assert.Contains("Tenant", vm.Error);                 // …plus the way out
    }

    [Fact]
    public async Task A_failure_with_a_tenant_set_adds_no_noise()
    {
        // The hint is only ever right when the tenant is blank; appending it to an unrelated
        // failure would be a confident wrong answer, which is worse than none.
        var vm = new AzureSignInViewModel("tenant-a",
            (_, _) => throw new InvalidOperationException("AADSTS70016: pending end-user authorization"));

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Equal("AADSTS70016: pending end-user authorization", vm.Error);
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
