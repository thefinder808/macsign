# Security Policy

MacSign is a code-signing tool: it handles signing keys, certificates, and the
integrity of the artifacts it produces. Security reports are taken seriously.

## Reporting a vulnerability

**Please do not open a public issue or pull request for a security problem.**

Report it privately through GitHub's **[Private vulnerability reporting](https://github.com/thefinder808/macsign/security/advisories/new)**
(Security → *Report a vulnerability* on the repository). That keeps the details
confidential until a fix is available.

When you report, please include:

- the version (or commit) and your OS / .NET version,
- a description of the issue and its impact,
- steps to reproduce, ideally with a minimal sample (a crafted file, command, or
  the smallest repro you can share), and
- any suggested remediation if you have one.

This is a small project, so please allow a few days for an initial response.
Coordinated disclosure is appreciated — once a fix ships, credit will gladly be
given in the release notes if you'd like it.

## Scope

In scope (examples):

- memory-safety / crash bugs reachable from a **hostile input file** (the engine
  is designed to never throw on the verify path and to reject malformed PE/MSI/PS1
  rather than corrupt or crash),
- a signature that verifies as valid when it should not (or vice-versa), or a
  timestamp accepted as valid when forged or grafted,
- **secret handling** — any path where a password, PIN, token, or key material is
  logged, persisted to disk, or placed on a child-process command line,
- command/argument injection through file paths, identity names, or profile names.

Out of scope:

- the security of third-party tools MacSign drives (Apple's `codesign` /
  `notarytool` / `stapler`, Azure Trusted Signing), or of the certificates/keys you
  supply,
- issues that require an already-compromised local machine or keychain,
- chain-trust results on macOS (the Microsoft roots aren't in the macOS trust store
  by design — see the README; integrity is still asserted authoritatively).

## Supported versions

Fixes are made against `main` and shipped in the next release. Only the **latest
release** is supported; please reproduce on it (or on `main`) before reporting.
