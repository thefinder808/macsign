using Azure.Core;
using Azure.Identity;

namespace MacSign.Signing.Azure;

/// <summary>Supplies the bearer token for the Trusted Signing data plane.</summary>
internal interface IAzureTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
}

/// <summary>
/// Resolves a Trusted Signing access token. A manually supplied token (option/env)
/// wins; otherwise Azure.Identity's <see cref="DefaultAzureCredential"/> is used — its
/// chain already covers <c>az login</c>, an environment service principal
/// (<c>AZURE_CLIENT_ID</c>/<c>TENANT_ID</c>/<c>CLIENT_SECRET</c>), and managed identity.
/// Tokens are short-lived, so one is fetched per signing run rather than cached.
/// </summary>
internal sealed class DefaultAzureTokenProvider : IAzureTokenProvider
{
    /// <summary>The Trusted Signing data-plane resource the token must be scoped to.</summary>
    public const string Scope = "https://codesigning.azure.net/.default";

    private readonly string? _manualToken;
    private readonly TokenCredential _credential;

    public DefaultAzureTokenProvider(string? manualToken, TokenCredential? credential = null)
    {
        _manualToken = string.IsNullOrWhiteSpace(manualToken) ? null : manualToken.Trim();
        _credential = credential ?? new DefaultAzureCredential();
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_manualToken is not null)
            return _manualToken;

        try
        {
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext([Scope]), ct)
                .ConfigureAwait(false);
            return token.Token;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not acquire an Azure access token for Trusted Signing (scope {Scope}). " +
                "Run `az login`, set AZURE_CLIENT_ID/AZURE_TENANT_ID/AZURE_CLIENT_SECRET, " +
                "or pass a token via --trusted-signing-token. Detail: " + ex.Message, ex);
        }
    }
}
