using System.Text;
using Azure.Core;
using Azure.Identity;

namespace MacSign.Signing.Azure;

/// <summary>
/// A completed browser sign-in. Deliberately carries no token: only who signed in, plus the
/// opaque record needed to replay that sign-in later. Safe to persist.
/// </summary>
/// <param name="Username">The account's user principal name, for display.</param>
/// <param name="TenantId">The directory the account authenticated against.</param>
/// <param name="SerializedRecord">
/// The account fields to hand back via <see cref="SigningOptions.TrustedSigningAuthRecord"/>.
/// The tokens themselves live in the OS keychain, never here.
/// </param>
public sealed record AzureSignInResult(string Username, string TenantId, string SerializedRecord);

/// <summary>
/// Signs a user in to Microsoft Entra through the system browser, so they can pick <i>which</i>
/// account signs rather than inheriting whichever one the machine happens to default to.
/// <para>
/// <b>This is the only code in MacSign permitted to open a browser.</b> Signing itself is
/// silent-or-fail by design (see <see cref="AzureCredentialFactory"/>), because a credential
/// that may prompt would open one window per file. Reach this only from an explicit user
/// gesture — never from a signing run, and never from CI.
/// </para>
/// </summary>
public static class AzureSignIn
{
    /// <summary>
    /// Opens the browser, lets the user choose an account, and returns who they picked.
    /// <paramref name="tenantId"/> may be a GUID or a domain; null authenticates against the
    /// account's home tenant. Cancelling <paramref name="ct"/> abandons the wait.
    /// </summary>
    public static async Task<AzureSignInResult> AuthenticateAsync(
        string? tenantId, CancellationToken ct = default)
    {
        var credential = new InteractiveBrowserCredential(BuildOptions(tenantId));

        // Authenticate for the Trusted Signing resource specifically, rather than a generic
        // scope, so any consent the tenant requires is granted here — at a moment the user is
        // looking at a browser — instead of surfacing much later as a failed sign.
        var record = await credential
            .AuthenticateAsync(new TokenRequestContext([DefaultAzureTokenProvider.Scope]), ct)
            .ConfigureAwait(false);

        return Describe(record);
    }

    /// <summary>
    /// No account hint is passed on purpose. Pre-filling a username suppresses the account
    /// picker, and picking a different account is the entire point of this screen — on a
    /// machine with Platform SSO the pre-filled account is exactly the one the user is trying
    /// to get away from.
    /// </summary>
    internal static InteractiveBrowserCredentialOptions BuildOptions(string? tenantId) => new()
    {
        TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),

        // The same named cache the signing path reads. If these ever diverge, sign-in appears
        // to work and every later sign reports "not signed in", with nothing to explain why.
        TokenCachePersistenceOptions = new TokenCachePersistenceOptions
        {
            Name = AzureCredentialFactory.TokenCacheName,
        },

        // DisableAutomaticAuthentication is left off here — the exact inverse of the signing
        // path. This method exists to prompt.
    };

    internal static AzureSignInResult Describe(AuthenticationRecord record)
    {
        using var stream = new MemoryStream();
        record.Serialize(stream);

        return new AzureSignInResult(
            record.Username,
            record.TenantId,
            Encoding.UTF8.GetString(stream.ToArray()));
    }
}
