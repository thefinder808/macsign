using MacSign.Signing.Credentials;

namespace MacSign.Signing.Azure;

/// <summary>
/// Entry point for the Azure Trusted Signing backend. A consumer (CLI / app) calls
/// <see cref="Register"/> once at startup to enable <c>CertMode.TrustedSigning</c>
/// signing in the core engine. The core never compile-time references this package.
/// </summary>
public static class AzureBackend
{
    public static void Register() => CredentialBackends.TrustedSigningFactory = Create;

    private static ICredentialSigner Create(SigningOptions options) =>
        new AzureTrustedSigner(
            options.TrustedSigningEndpoint!,
            options.TrustedSigningAccount!,
            options.TrustedSigningProfile!,
            new DefaultAzureTokenProvider(options.TrustedSigningAccessToken));
}
