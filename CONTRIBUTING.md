# Contributing to MacSign

Thanks for your interest in improving MacSign! This is a focused project, but
issues and pull requests are welcome.

## Getting set up

MacSign is a .NET 10 solution (`MacSign.slnx`). You need the **.NET 10 SDK** and
**macOS** (the GUI and the Apple-signing wrapper are macOS-only; the core engine and
CLI are cross-platform).

```bash
dotnet build -c Release
dotnet test
```

- `dotnet run --project src/MacSign.Cli -- help` — the `macsign` CLI.
- `dotnet run --project src/MacSign.App` — the native macOS GUI.

See [`README.md`](README.md) for the project layout and what each `src/` project does.

## How the codebase is organized

- **`src/MacSign.Signing`** is the engine and stays **dependency-clean** — zero
  third-party NuGet packages (only the Microsoft platform package
  `System.Security.Cryptography.Pkcs`). Native / third-party dependencies live in the
  **quarantined backend packages** (`MacSign.Signing.Pkcs11`, `.Azure`, `.Msi`) that
  self-register via a `Register()` hook. Please keep new native deps out of the core.
- **`src/MacSign.App`** is the Avalonia GUI (MVVM). The Apple-tool wrapper
  (`AppleSigningService` + `ProcessRunner`) is intentionally Avalonia-free.

## Submitting changes

1. **Open an issue first** for anything non-trivial, so we can agree on the approach.
2. **Branch off `main`** — one branch per change (`feat/…`, `fix/…`, `docs/…`).
3. **Add or update tests.** New format/credential/verify behavior needs coverage;
   the test suites are `tests/MacSign.Signing.Tests` (engine) and
   `tests/MacSign.App.Tests` (GUI/Apple wrapper, via a fake process runner).
4. **`dotnet build -c Release && dotnet test` must pass**, and so must CI — which on a
   Windows runner verifies a macOS-signed fixture with the authoritative
   **Windows `signtool`** (`.github/workflows/ci.yml`). Self-consistency (our own
   verify) is necessary but **not** sufficient proof; the signtool gate must stay green.
5. **Open a PR to `main`.** CI must be green before merge.

### Commit messages

Short, imperative, with a type prefix matching the existing history:
`feat:` / `fix:` / `docs:` / `harden:` / `tweak:` / `chore:` (optionally scoped, e.g.
`fix(pe): …`).

## Security — never commit signing material

This is a signing tool, so be careful what lands in git:

- **Never commit** `.pfx` / `.p12` / `.p8` / `.pem` / private keys, passwords, tokens,
  Azure credentials, or keychain profile contents. `.gitignore` already excludes the
  common cases (`*.pfx`, `*.p12`, `secrets/`, `scripts/azure.env`).
- Pass secrets to the CLI via the **`--password-env` / `--trusted-signing-token-env`**
  environment forms, never plaintext on argv.
- Found a vulnerability? Please follow [`.github/SECURITY.md`](.github/SECURITY.md)
  and report it privately rather than in a public issue/PR.

## Code of conduct

By participating you agree to abide by the
[Code of Conduct](CODE_OF_CONDUCT.md).
