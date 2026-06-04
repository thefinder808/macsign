---
name: Bug report
about: Something isn't working the way you expect
title: ''
labels: bug
assignees: ''
---

> ⚠️ **Security issue?** Do **not** file it here — report it privately via
> [Security → Report a vulnerability](https://github.com/thefinder808/macsign/security/advisories/new).
> See [SECURITY.md](../SECURITY.md).

**What happened**
A clear description of the bug.

**What you expected**
What you expected to happen instead.

**Steps to reproduce**
1. …
2. …
3. …

If it involves a specific file (a PE/MSI/PS1, a `.app`/`.dmg`), the smallest sample
that reproduces it helps a lot. Please don't attach anything signed with a real key.

**Environment**
- MacSign version (or commit): <!-- e.g. v0.6.0, or `macsign --version` -->
- GUI or CLI:
- macOS version:
- .NET SDK version (if building from source): <!-- `dotnet --version` -->
- Credential type: <!-- PFX / PKCS#11 token / Azure Trusted Signing -->

**Logs / output**
Paste the relevant CLI output or the GUI activity log. **Redact any secrets, paths, or
identity details you don't want public.**
