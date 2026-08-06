using System.Collections.Concurrent;
using System.Text;
using Azure.Core;
using Azure.Identity;

namespace MacSign.Signing.Azure;

/// <summary>
/// Turns the caller's <see cref="SigningOptions"/> into the Azure.Identity credential that will
/// mint Trusted Signing tokens.
/// <para>
/// The build methods are separated from <see cref="Create"/> so the *decision* can be asserted
/// in tests without constructing a live credential — a real interactive credential reaches for
/// the machine's keychain, which is not something CI should be doing.
/// </para>
/// <para>
/// <b>Neither path may open a browser.</b> <see cref="AuthenticodeSigner"/> builds a credential
/// once per <i>call</i>, and the GUI calls it once per file — so a credential allowed to prompt
/// would open one browser window per file whenever the token cache is cold. Signing is
/// silent-or-fail; the single place an <i>authentication</i> prompt may open is
/// <see cref="AzureSignIn"/>, reached only by an explicit user gesture.
/// </para>
/// </summary>
internal static class AzureCredentialFactory
{
    /// <summary>
    /// Names our slice of the MSAL token cache. Without a name the cache uses a shared default,
    /// which would mean reading and writing some other Azure tool's cached accounts.
    /// </summary>
    internal const string TokenCacheName = "macsign";

    /// <summary>
    /// Everything that decides <i>which</i> identity a credential produces. A manually supplied
    /// access token is deliberately absent: the provider short-circuits before a credential is
    /// ever built when one is given.
    /// </summary>
    internal readonly record struct CredentialKey(
        TrustedSigningCredentialSource Source, string? TenantId, string? AuthRecord);

    /// <summary>Normalises the same way the option builders do — otherwise a blank tenant and
    /// an absent one become two cache entries for one credential.</summary>
    internal static CredentialKey KeyFor(SigningOptions options) => new(
        options.TrustedSigningCredentialSource,
        Tenant(options),
        string.IsNullOrWhiteSpace(options.TrustedSigningAuthRecord) ? null : options.TrustedSigningAuthRecord);

    /// <summary>
    /// Credentials are reused for the lifetime of the process, keyed by the identity they
    /// resolve to. <see cref="AuthenticodeSigner"/> constructs a credential per <i>call</i> and
    /// disposes it, and the GUI calls it once per file — so without this a 50-file GUI run built
    /// 50, each one shelling out to <c>az</c> twice on the default path. The CLI already built
    /// one, since it signs a whole batch in a single call.
    /// <para>
    /// Holding them is safe and deliberate: Azure.Identity credentials are thread-safe, are not
    /// <see cref="IDisposable"/>, and keep no secret of their own (tokens live in the OS
    /// keychain). A <c>GetOrAdd</c> race can build two — both valid, one discarded.
    /// </para>
    /// </summary>
    private static readonly ConcurrentDictionary<CredentialKey, TokenCredential> Cache = new();

    internal static TokenCredential Create(SigningOptions options) =>
        Cache.GetOrAdd(KeyFor(options), _ => Build(options));

    private static TokenCredential Build(SigningOptions options) =>
        options.TrustedSigningCredentialSource switch
        {
            TrustedSigningCredentialSource.InteractiveBrowser =>
                new InteractiveBrowserCredential(BuildInteractiveOptions(options)),
            _ => new DefaultAzureCredential(BuildDefaultOptions(options)),
        };

    internal static DefaultAzureCredentialOptions BuildDefaultOptions(SigningOptions options) => new()
    {
        // Without this the chain resolves to whatever answers first — on a Mac usually
        // AzureCliCredential, i.e. whichever account `az login` last selected.
        TenantId = Tenant(options),

        // Already Azure.Identity's default, set explicitly so a future default change can't
        // quietly introduce a browser popup into the middle of a signing batch.
        ExcludeInteractiveBrowserCredential = true,
    };

    internal static InteractiveBrowserCredentialOptions BuildInteractiveOptions(SigningOptions options) => new()
    {
        TenantId = Tenant(options),
        AuthenticationRecord = ReadRecord(options.TrustedSigningAuthRecord),

        // Persisted to the OS keychain, so a sign-in survives relaunches. Note there is no
        // UnsafeAllowUnencryptedStorage fallback on purpose: that writes refresh tokens to a
        // plaintext file. If the keychain is unavailable, degrade to no persistence — the user
        // signs in again — never to plaintext.
        TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = TokenCacheName },

        // See the class remarks: signing is silent-or-fail. A cache miss surfaces as
        // AuthenticationRequiredException, which the token provider turns into "sign in again".
        DisableAutomaticAuthentication = true,
    };

    /// <summary>
    /// A tenant may be a GUID <i>or</i> a domain (<c>contoso.onmicrosoft.com</c>), so this only
    /// trims. Blank becomes null, because an empty string is not the same as "unset" — null
    /// lets the credential fall back to its own default tenant.
    /// </summary>
    internal static string? Tenant(SigningOptions options) =>
        string.IsNullOrWhiteSpace(options.TrustedSigningTenantId)
            ? null
            : options.TrustedSigningTenantId.Trim();

    /// <summary>
    /// Rehydrates a stored sign-in. Returns null for anything unreadable rather than throwing:
    /// this is fed from user-editable <c>settings.json</c> that outlives app upgrades, and the
    /// right answer to a mangled record is "you're signed out", not a crash on startup.
    /// </summary>
    internal static AuthenticationRecord? ReadRecord(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return AuthenticationRecord.Deserialize(stream);
        }
        catch
        {
            return null;
        }
    }
}
