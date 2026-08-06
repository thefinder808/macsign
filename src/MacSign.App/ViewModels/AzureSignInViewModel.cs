using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSign.App.Services;
using MacSign.Signing.Azure;

namespace MacSign.App.ViewModels;

/// <summary>
/// Backs the "Sign in to Azure" dialog: opens the system browser, lets the user pick an
/// account, and hands the resulting sign-in back to the Sign screen.
/// <para>
/// This is the only place in the app that opens a browser. Signing itself replays the account
/// recorded here and never prompts, so a batch can't be interrupted halfway through.
/// </para>
/// <para>
/// No secret is persisted: the result carries a username, a tenant and an opaque account
/// record — the tokens stay in the OS keychain.
/// </para>
/// </summary>
public partial class AzureSignInViewModel : ObservableObject
{
    private readonly Func<string?, CancellationToken, Task<AzureSignInResult>> _authenticate;
    private CancellationTokenSource? _cts;

    /// <param name="tenantId">Tenant to sign in to, or null for the account's home tenant.</param>
    /// <param name="authenticate">Injectable for tests — production uses the real browser flow.</param>
    public AzureSignInViewModel(
        string? tenantId = null,
        Func<string?, CancellationToken, Task<AzureSignInResult>>? authenticate = null)
    {
        TenantId = tenantId ?? "";
        _authenticate = authenticate ?? ((t, ct) => AzureSignIn.AuthenticateAsync(t, ct));
    }

    /// <summary>Raised on success — the dialog closes with <see cref="Result"/>.</summary>
    public event Action? Succeeded;

    [ObservableProperty] private string _tenantId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyPropertyChangedFor(nameof(PrimaryLabel))]
    private bool _busy;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasError))] private string _error = "";
    public bool HasError => !string.IsNullOrEmpty(Error);

    public string PrimaryLabel => Busy ? "Waiting for browser…" : "Sign in";

    public AzureSignInData? Result { get; private set; }

    /// <summary>
    /// Appended only when no tenant was given, because that is the only case where it is
    /// certainly right. A blank tenant signs in to the <i>account's own</i> directory — for a
    /// personal Microsoft account that is the consumer tenant ("Microsoft Services"), which is
    /// never where an Azure signing account lives. Entra's reply names a tenant the user has
    /// never heard of and suggests adding themselves as an external user; the real fix is one
    /// field away, and nothing in Entra's message can point at it.
    /// </summary>
    private const string BlankTenantHint =
        "\n\nIf you signed in with a personal Microsoft account, fill in the Tenant field and " +
        "try again — left blank, sign-in goes to that account's own directory rather than the " +
        "Azure directory your signing account belongs to. `az account show --query tenantId -o tsv` " +
        "prints the right one.";

    private bool CanSignIn() => !Busy;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        Busy = true;
        Error = "";
        _cts = new CancellationTokenSource();
        try
        {
            var account = await _authenticate(NullIfEmpty(TenantId), _cts.Token);
            Result = AzureSignInData.FromRecordJson(account.SerializedRecord);
            // Remember the tenant as typed here. Entra only reports the canonical GUID, so
            // without this a domain-form tenant would never match the field the user is
            // looking at, and the Sign screen would read "not signed in" forever.
            Result.RequestedTenant = NullIfEmpty(TenantId);
            Succeeded?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Error = "Sign-in cancelled.";
        }
        catch (Exception ex)
        {
            // Surfaced verbatim on purpose. Entra's own text (an AADSTS code, a consent
            // prompt, a blocked tenant) is far more actionable than anything we could
            // paraphrase, and paraphrasing it is how a fixable problem becomes a mystery.
            // The one thing worth *adding* is the fix Entra can't know about — see below.
            Error = ex.Message + (NullIfEmpty(TenantId) is null ? BlankTenantHint : "");
        }
        finally
        {
            Busy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Abandons a sign-in that is waiting on the browser — otherwise closing the
    /// browser tab would leave the dialog stuck on "Waiting for browser…" forever.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
