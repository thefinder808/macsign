# MacSign

Native macOS Authenticode signing for Windows artifacts — **no Windows machine, no `osslsigncode`, no `jsign`, no OpenSSL/JVM.** A fully managed .NET 10 engine.

> **Status: Phase 7.** Signs **PE files** (`.exe`/`.dll`/`.sys`, incl. managed assemblies) and **PowerShell scripts** (`.ps1`), using a **local PFX certificate** or a **PKCS#11 token / HSM** (key never leaves the device), with optional **RFC3161 timestamping** — and **verifies** signatures (integrity + chain report). Azure Trusted Signing and MSI come in later phases. See `OVERVIEW.md` and the design doc in the Obsidian vault (`Development/Projects/MacSign/Native Signing Engine.md`).

## Why

The cross-platform tools for signing Windows binaries from a Mac are fiddly CLIs. MacSign reimplements Authenticode natively in .NET so signing is a single dependency-clean, notarizable app — and so the format logic is unit-testable and fully under our control.

## Layout

| Project | What |
|---|---|
| `src/MacSign.Signing` | The engine. No third-party deps; one Microsoft platform package (`System.Security.Cryptography.Pkcs`) for the CMS APIs. |
| `src/MacSign.Signing.Pkcs11` | Optional PKCS#11/HSM backend, quarantined so `Pkcs11Interop` stays out of the core. Loaded only by consumers that sign with a token. |
| `src/MacSign.Cli` | A thin console harness (`macsign`) — manual signing and the seed of the eventual GUI. |
| `src/MacSign.Fixture` | A trivial class library whose compiled DLL is the unsigned PE the tests/CI sign. |
| `tests/MacSign.Signing.Tests` | xUnit: PE digest, CMS framing, sign→verify round-trip, secret hygiene. |

## Build & test

```bash
dotnet build -c Release
dotnet test
```

## Sign something

```bash
# Make a throwaway self-signed code-signing cert (test/dev only):
PFX_PW=secret dotnet run --project src/MacSign.Cli -- \
  gen-test-cert --pfx test.pfx --cer test.cer --password-env PFX_PW

# Sign a PE in place (optionally RFC3161-timestamped):
PFX_PW=secret dotnet run --project src/MacSign.Cli -- \
  sign --pfx test.pfx --password-env PFX_PW --description "My App" \
  --timestamp-url http://timestamp.digicert.com some.dll

# Sign with a PKCS#11 token / HSM instead (key never leaves the device):
PIN=1234 dotnet run --project src/MacSign.Cli -- \
  sign --pkcs11-module /path/to/pkcs11.so --password-env PIN some.dll

# Verify a signature (reports signer, timestamp, integrity, and chain trust):
dotnet run --project src/MacSign.Cli -- verify some.dll
```

`verify` reports **signature integrity** (file unmodified + signer signature valid) separately from **chain trust** — on macOS the Microsoft roots aren't in the system store, so chain trust usually can't be established, but integrity can be asserted authoritatively.

The password is read from an environment variable (or `--password`), never logged, and never placed on a child-process command line.

## Verifying the signature

The authoritative check is **Windows `signtool`**, run in CI (`.github/workflows/ci.yml`): the macOS job signs a fixture PE and uploads it; a `windows-latest` job runs `signtool verify /pa` against it. That gate proves the cross-platform claim and must stay green.

Locally on macOS you can sanity-check with `osslsigncode` (chain trust will fail for a self-signed cert — that's expected; supply the cert as a trusted root to get a full pass):

```bash
osslsigncode verify some.dll                       # parses + checks digest/signature
openssl x509 -inform DER -in test.cer -out test.pem
osslsigncode verify -CAfile test.pem -ignore-timestamp some.dll   # full pass
```

## Support

If MacSign saves you a Windows VM, you can support development here: https://www.buymeacoffee.com/thefinder808
