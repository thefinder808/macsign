# MacSign

Native macOS Authenticode signing for Windows artifacts — **no Windows machine, no `osslsigncode`, no `jsign`, no OpenSSL/JVM.** A fully managed .NET 10 engine.

> **Status: all backends shipped.** Signs **PE files** (`.exe`/`.dll`/`.sys`, incl. managed assemblies), **PowerShell scripts** (`.ps1`), and **MSI installers** (`.msi`), using a **local PFX certificate**, a **PKCS#11 token / HSM**, or **Azure Trusted Signing** (a.k.a. Azure Artifact Signing) — for the token and cloud paths the key never leaves the device/cloud — with optional **RFC3161 timestamping**, and **verifies** signatures (integrity + chain report). The Azure path is proven offline by a contract test (delegated path == the signtool-proven in-proc path) and has been signed + verified end-to-end against a live Trusted Signing account; re-verify it anytime with `scripts/verify-azure.sh` (uses your `az login`, no stored credentials). See `OVERVIEW.md` (the original pre-implementation concept, kept for history) and the design doc in the Obsidian vault (`Development/Projects/MacSign/Native Signing Engine.md`).

## Why

The cross-platform tools for signing Windows binaries from a Mac are fiddly CLIs. MacSign reimplements Authenticode natively in .NET so signing is a single dependency-clean, notarizable app — and so the format logic is unit-testable and fully under our control.

## Layout

| Project | What |
|---|---|
| `src/MacSign.Signing` | The engine. No third-party deps; one Microsoft platform package (`System.Security.Cryptography.Pkcs`) for the CMS APIs. |
| `src/MacSign.Signing.Pkcs11` | Optional PKCS#11/HSM backend, quarantined so `Pkcs11Interop` stays out of the core. Loaded only by consumers that sign with a token. |
| `src/MacSign.Signing.Azure` | Optional Azure Trusted Signing backend, quarantined so `Azure.Identity` stays out of the core. A delegating RSA POSTs each digest to the cloud sign endpoint. |
| `src/MacSign.Signing.Msi` | Optional MSI backend, quarantined so the `OpenMcdf` (CFBF) dependency stays out of the core. |
| `src/MacSign.Cli` | A thin console harness (`macsign`) — scriptable signing/verifying. |
| `src/MacSign.App` | The native macOS GUI (.NET 10 + Avalonia) — consumes the engine in-process. Sign / Verify / **Mac apps** / Profiles / Activity, light + dark. |
| `src/MacSign.Fixture` | A trivial class library whose compiled DLL is the unsigned PE the tests/CI sign. |
| `tests/MacSign.Signing.Tests` | xUnit: PE digest, CMS framing, sign→verify round-trip, secret hygiene. |
| `tests/MacSign.App.Tests` | xUnit for the **Mac apps** (`codesign`/`notarytool`) wrapper: exact argv per option, identity allow-listing, `.dmg`-direct notarize, process injection/cancellation. |

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

# Sign a PE in place (optionally RFC3161-timestamped; --timestamp-url accepts a
# comma-separated list of TSAs tried in order, so one outage won't fail the sign):
PFX_PW=secret dotnet run --project src/MacSign.Cli -- \
  sign --pfx test.pfx --password-env PFX_PW --description "My App" \
  --timestamp-url http://timestamp.digicert.com some.dll

# Sign with a PKCS#11 token / HSM instead (key never leaves the device):
PIN=1234 dotnet run --project src/MacSign.Cli -- \
  sign --pkcs11-module /path/to/pkcs11.so --password-env PIN some.dll

# Sign with Azure Trusted Signing (key never leaves Azure). With no token flag the
# token is acquired via Azure.Identity (az login, env service principal, or managed
# identity); or pass one explicitly with --trusted-signing-token[-env]:
dotnet run --project src/MacSign.Cli -- \
  sign --trusted-signing-endpoint eus.codesigning.azure.net \
       --trusted-signing-account my-account \
       --trusted-signing-profile my-profile some.dll

# Verify a signature (reports signer, timestamp, integrity, and chain trust):
dotnet run --project src/MacSign.Cli -- verify some.dll

# Remove an existing signature, in place (PE / PowerShell / MSI):
dotnet run --project src/MacSign.Cli -- remove some.dll
```

`verify` reports **signature integrity** (file unmodified + signer signature valid) separately from **chain trust** — on macOS the Microsoft roots aren't in the system store, so chain trust usually can't be established, but integrity can be asserted authoritatively. It lists **every signer** on a co-signed binary and flags a **nested signature**, and only surfaces an RFC3161 **timestamp it has cryptographically validated** (a forged or grafted token is not shown as the signing time).

## Native macOS app (GUI)

A native macOS GUI (`src/MacSign.App`, .NET 10 + Avalonia) consumes the same engine **in-process** — no shelling out. Five screens: **Sign** (drag-drop files + a credential/options inspector, ⌘S to sign), **Verify** (a Windows artifact's integrity vs. chain-trust report, *or* a Mac `.app`/`.dmg`'s `codesign` signature — signer, Team ID, Hardened Runtime, notarization), **Mac apps** (sign, notarize & staple a `.app` bundle or `.dmg` with your Developer ID), **Profiles** (reusable presets — no secrets stored), and **Activity** (run history). Full light + dark, following the macOS appearance.

> **Mac apps** is the inverse of the engine's day job: rather than signing Windows artifacts, it signs **your Mac apps**. It's a thin, injection-safe wrapper over Apple's own `codesign` / `notarytool` / `stapler` (not a reimplementation) — choose a `.app` or `.dmg`, pick a Developer ID identity, and watch sign → verify → notarize → staple stream in a live log. Before submitting to the notary it runs a **pre-flight** (mounting a `.dmg` to inspect its contents) and stops — with a "Notarize anyway" override — if anything inside isn't signed/hardened, so you don't burn a multi-minute round-trip on a doomed upload.
>
> If the pre-flight finds unsigned `.app` bundles inside your `.dmg`, a **"Sign contents & continue"** button appears alongside "Notarize anyway". Clicking it signs the problematic apps inside the image — with Hardened Runtime and deep signing, using the Developer ID identity already chosen, and applying your Entitlements `.plist` if you've set one (recommended for JIT-enabled apps such as .NET apps that need `allow-jit`) — re-seals the DMG in place, and then proceeds automatically to sign, notarize, and staple. Only the apps that failed the pre-flight checks are re-signed; already-valid signatures are left untouched.

```bash
dotnet run --project src/MacSign.App          # run from source

# Build a signed + notarized DMG (Developer ID + a notarytool keychain profile):
SIGN_IDENTITY="Developer ID Application: NAME (TEAMID)" \
NOTARY_PROFILE=your-notary-profile ./build-macos.sh
# (omit the env vars for an unsigned local build)
```

**Releases are tag-driven:** push a `v*` tag and CI builds, signs, notarizes, and publishes the `arm64` + `x64` DMGs to a GitHub Release (`.github/workflows/release.yml`). Setup + required secrets: [`docs/RELEASE-SIGNING.md`](docs/RELEASE-SIGNING.md).

Prefer supplying the password (or PIN, or Azure token) via an environment variable — `--password-env` / `--trusted-signing-token-env`. The plaintext `--password` / `--trusted-signing-token` flags still work, but MacSign warns about them: an argv secret lands in your shell history and the process list (`ps`). Secrets are never persisted, logged, or placed on a child-process command line.

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
