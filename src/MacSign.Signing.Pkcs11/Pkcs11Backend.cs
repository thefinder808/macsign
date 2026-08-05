using MacSign.Signing.Credentials;

namespace MacSign.Signing.Pkcs11;

/// <summary>
/// Entry point for the PKCS#11 backend. A consumer (CLI / app) calls
/// <see cref="Register"/> once at startup to enable <c>CertMode.Pkcs11</c> signing
/// in the core engine.
/// </summary>
public static class Pkcs11Backend
{
    public static void Register() => CredentialBackends.Pkcs11Factory = Create;

    // The token is unused: a PKCS#11 credential opens a local module and authenticates a
    // session, with no interruptible wait to speak of.
    private static ICredentialSigner Create(SigningOptions options, CancellationToken ct) =>
        new Pkcs11CredentialSigner(options.Pkcs11ModulePath!, options.Secret, options.Pkcs11CertThumbprint);
}
