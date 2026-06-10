# MacSign.Signing — cross-platform Authenticode signing engine

Sign Windows artifacts — PE (`.exe`/`.dll`/`.sys`), PowerShell (`.ps1`), and MSI —
from **any OS**, in process, with no `signtool`/`osslsigncode`/`jsign` shell-outs.
Hand-rolled format handling on top of the .NET BCL's CMS APIs, with RFC3161
timestamping and in-process signature verification. Extracted from
[MacSign](https://github.com/thefinder808/macsign), where every release is
cross-verified by `signtool verify` on Windows CI.

## Packages

| Package | Adds |
|---|---|
| `MacSign.Signing` | The engine: PE + PowerShell formats, PFX signing, RFC3161 timestamping, verification |
| `MacSign.Signing.Msi` | `.msi` (OLE2 compound file) format — `MsiBackend.Register()` |
| `MacSign.Signing.Pkcs11` | PKCS#11 token/HSM keys (key never leaves the token) — `Pkcs11Backend.Register()` |
| `MacSign.Signing.Azure` | Azure Trusted Signing cloud keys (key never leaves Azure) — `AzureBackend.Register()` |

## Usage

```csharp
using MacSign.Signing;

// Optional backends (only if you referenced those packages):
MacSign.Signing.Msi.MsiBackend.Register();
MacSign.Signing.Azure.AzureBackend.Register();

var options = new SigningOptions
{
    CertMode = CertMode.Pfx,
    PfxPath = "signing-cert.pfx",
    Secret = pfxPassword,                              // transient, never persisted
    TimestampUrl = "http://timestamp.digicert.com",    // RFC3161; comma-separated fallbacks OK
    Description = "My App Installer",
};

var signer = AuthenticodeSigner.TryCreate(options, out var error);
if (signer is null) throw new InvalidOperationException(error);

SignResult result = await signer.SignAsync(sourceFolder, setupFile, options, log: null);
```

Already-signed files are skipped, files are replaced atomically, and
`SignatureVerifier.Verify(path)` reports signer / timestamp / chain state for any
supported file.

Licensed under Apache-2.0.
