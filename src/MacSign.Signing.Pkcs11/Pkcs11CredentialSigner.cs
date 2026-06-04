using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MacSign.Signing.Credentials;
using Net.Pkcs11Interop.X509Store;

namespace MacSign.Signing.Pkcs11;

/// <summary>
/// A credential whose private key lives on a PKCS#11 token / HSM. The key never
/// enters this process: <see cref="SigningKey"/> is a delegating RSA/ECDsa that
/// performs the actual signature operation on the token (via Pkcs11Interop).
/// </summary>
internal sealed class Pkcs11CredentialSigner : ICredentialSigner
{
    private readonly Pkcs11X509Store _store;
    private readonly X509Certificate2 _certificate;
    private readonly AsymmetricAlgorithm _signingKey;

    public Pkcs11CredentialSigner(string modulePath, string? pin, string? thumbprint)
    {
        _store = new Pkcs11X509Store(modulePath, new StaticPinProvider(pin));

        // The store pairs certificate + private-key objects by CKA_ID, so the certs
        // it lists are signing-capable; GetPrivateKey() below confirms a key is present.
        var candidates = new List<Pkcs11X509Certificate>();
        foreach (var slot in _store.Slots)
        {
            if (slot.Token is not { } token)
                continue;
            candidates.AddRange(token.Certificates);
        }

        var chosen = thumbprint is not null
            ? candidates.FirstOrDefault(c =>
                string.Equals(c.Info.ParsedCertificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
            : candidates.Count == 1 ? candidates[0] : null;

        if (chosen is null)
        {
            _store.Dispose();
            throw new InvalidOperationException(candidates.Count switch
            {
                0 => "No certificate with a private key was found on the token.",
                _ => $"{candidates.Count} signing certificates found on the token — pass a thumbprint to select one.",
            });
        }

        _certificate = chosen.Info.ParsedCertificate;
        _signingKey = chosen.GetPrivateKey()
            ?? throw new InvalidOperationException("The token's private key is not accessible.");
    }

    public X509Certificate2 Certificate => _certificate;

    public AsymmetricAlgorithm SigningKey => _signingKey;

    public IReadOnlyList<X509Certificate2> Chain => [];

    public void Dispose()
    {
        _signingKey.Dispose();
        _certificate.Dispose(); // parity with PfxCredentialSigner / AzureTrustedSigner
        _store.Dispose();
    }
}

/// <summary>Supplies a fixed PIN (or cancels, for a PIN-pad token) to the store.</summary>
internal sealed class StaticPinProvider(string? pin) : IPinProvider
{
    private readonly byte[]? _pin = pin is null ? null : Encoding.UTF8.GetBytes(pin);

    public GetPinResult GetTokenPin(Pkcs11X509StoreInfo storeInfo, Pkcs11SlotInfo slotInfo, Pkcs11TokenInfo tokenInfo)
        => new(cancel: _pin is null, pin: _pin!);

    public GetPinResult GetKeyPin(Pkcs11X509StoreInfo storeInfo, Pkcs11SlotInfo slotInfo, Pkcs11TokenInfo tokenInfo, Pkcs11X509CertificateInfo certificateInfo)
        => new(cancel: _pin is null, pin: _pin!);
}
