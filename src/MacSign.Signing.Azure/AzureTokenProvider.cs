using Azure.Core;
using Azure.Identity;

namespace MacSign.Signing.Azure;

/// <summary>Supplies the bearer token for the Trusted Signing data plane.</summary>
internal interface IAzureTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
}

/// <summary>
/// Resolves a Trusted Signing access token. A manually supplied token (option/env) wins;
/// otherwise the credential chosen by <see cref="AzureCredentialFactory"/> supplies one —
/// either Azure.Identity's default chain (<c>az login</c>, an environment service principal,
/// managed identity) or an account picked earlier through the browser.
/// Tokens are short-lived, so one is fetched per signing run rather than cached.
/// </summary>
internal sealed class DefaultAzureTokenProvider : IAzureTokenProvider
{
    /// <summary>The Trusted Signing data-plane resource the token must be scoped to.</summary>
    public const string Scope = "https://codesigning.azure.net/.default";

    private readonly string? _manualToken;
    private readonly Lazy<TokenCredential> _credential;

    public DefaultAzureTokenProvider(SigningOptions options, TokenCredential? credential = null)
    {
        _manualToken = string.IsNullOrWhiteSpace(options.TrustedSigningAccessToken)
            ? null
            : options.TrustedSigningAccessToken.Trim();

        // Lazy so an explicit token short-circuits before a credential is ever built — with the
        // browser source that construction reaches for the keychain, which shouldn't happen for
        // a caller who supplied their own token.
        _credential = credential is not null
            ? new Lazy<TokenCredential>(credential)
            : new Lazy<TokenCredential>(() => AzureCredentialFactory.Create(options));
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_manualToken is not null)
            return _manualToken;

        try
        {
            var token = await _credential.Value
                .GetTokenAsync(new TokenRequestContext([Scope]), ct)
                .ConfigureAwait(false);
            return token.Token;
        }
        catch (AuthenticationRequiredException ex)
        {
            // Signing deliberately never opens a browser (see AzureCredentialFactory), so a
            // cold or cleared token cache lands here rather than popping a window per file.
            // Must be caught ahead of the generic handler below, whose "run az login" advice
            // is simply wrong for a browser-selected account.
            throw new InvalidOperationException(
                "Your Azure sign-in has expired or is no longer available. Sign in again " +
                "(Sign screen → \"Sign in…\") to keep signing with the account you chose. " +
                "Detail: " + ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not acquire an Azure access token for Trusted Signing (scope {Scope}). " +
                "Run `az login`, set AZURE_CLIENT_ID/AZURE_TENANT_ID/AZURE_CLIENT_SECRET, " +
                "or pass a token via --trusted-signing-token. If a token is issued but the " +
                "service rejects it, the sign-in is probably for the wrong tenant — set the " +
                "tenant explicitly. Detail: " + ex.Message, ex);
        }
    }
}
