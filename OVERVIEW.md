# MacSign — concept overview

> **Status:** concept / not started. Working name **MacSign** (folder: `macsign`).
> Spun out of the payload-signing feature shipped in **WrapTune-MacOS v1.1.0**.
> This is a high-level seed doc to resume from in a later session — not a plan or spec yet.

## One-line pitch

A native macOS GUI to **Authenticode-sign Windows apps, installers, and scripts** —
local certs *and* cloud signing — **without a Windows machine**.

## Why (problem + audience)

- Signing Windows artifacts (`.exe` / `.dll` / `.sys` / `.msi` / `.cab` / `.cat` /
  `.appx` / `.ps1`) from a Mac is painful: the canonical tool (`SignTool`) is
  Windows-only, and the cross-platform options (`osslsigncode`, `jsign`) are powerful
  but fiddly CLIs with sharp edges (we hit several — see "Carry-over learnings").
- **Audience is broader than WrapTune's Intune niche:** any cross-platform dev /
  maintainer / admin on macOS who ships Windows software — Electron, Tauri, .NET,
  installers, PowerShell. Bigger pond.
- **Gap in the market:** polished *native macOS* signing GUIs basically don't exist;
  most signing GUIs are Windows-only.
- **Honest caveat:** the moat is UX/convenience, not novel tech — it's a friendly
  front-end over two open-source CLIs. CLI-savvy users will use the CLIs directly;
  the value is for everyone who doesn't want to learn the flags or hit the traps.
  → Validate demand cheaply (Reddit/HN/landing page) before polishing.

## Core idea & architecture

- **Reuse the existing signing core.** WrapTune-MacOS already has a clean,
  engine-independent, BCL-only signing library at
  `/Users/thefinder808/Development/WrapTuneMacOS/src/WrapTuneMacOS.Signing/`
  (+ user/architecture doc `…/docs/PAYLOAD-SIGNING.md`). Extract it to a **shared
  library** both apps consume, so fixes land once. Most of the hard work is done.
- **Two backends** (already implemented):
  - **`osslsigncode`** → local certs: **PFX/.p12** and **PKCS#11 / HSM**. Also the
    only one that can **verify** existing signatures.
  - **`jsign`** → cloud KMS: **Azure Artifact Signing** (formerly Trusted Signing) is
    done; jsign also supports a long tail — **Azure Key Vault, AWS KMS, GCP KMS,
    HashiCorp Vault, DigiCert ONE**, etc. — same invocation shape.
- **Signers are user-installed via Homebrew** (`brew install osslsigncode` / `jsign`),
  **not bundled** — keeps the notarized app dependency-clean (no bundled OpenSSL/JVM).
- **Same stack as WrapTune:** .NET 10 + Avalonia, Developer ID signed + notarized DMG,
  tag-driven CI release. (Signing identity already on this machine.)

## Simple GUI sketch (single window)

1. **Files to sign** — drag-drop / picker, multiple files. Show detected type + current
   signature status (signed/unsigned, signer subject).
2. **Credential mode** — PFX · PKCS#11 · Cloud (backend dropdown). Mode-specific fields
   with **tooltips** + a **live prerequisite check** (is the signer installed? is `az`
   logged in? RBAC reminder) — carry these straight over from WrapTune.
3. **Options** — timestamp URL (where applicable), description/URL, "re-sign
   already-signed files" toggle (default: skip).
4. **Sign** → streamed log with **actionable errors** (e.g. the 403→RBAC hint).
5. **Verify** — `osslsigncode verify`; show subject / issuer / timestamp.

## Scope

**v1 (ship the smallest useful thing — mostly already built):**
- Sign one or more files in place (PE / MSI / scripts).
- Backends: **PFX, PKCS#11, Azure Artifact Signing** (everything WrapTune already has).
- Prereq checks, tooltips, actionable errors, secure secret handling (all carried over).
- Verify (signature present + subject/issuer/timestamp).

**Later / maybe (resist doing on day one):**
- More jsign cloud backends (Azure Key Vault → most likely next; AWS/GCP KMS; Vault).
- Batch/folder signing, drag-onto-dock-icon, saved profiles, CLI/headless mode.

## Carry-over learnings (already solved in WrapTune — don't relearn)

- Secrets via env var (`--storepass env:`) or a `0600` `-readpass` file — **never argv**
  (`ps` leak). Secrets never persisted.
- jsign `--keystore` wants the **bare host**; a pasted portal "Account URI" (`https://…/`)
  → double-slash **404**. Normalize (strip scheme + trailing slash).
- **Azure RBAC:** signing identity needs the **"Artifact Signing Certificate Profile
  Signer"** role (GUID `2837e146-70d7-4cfd-ad55-7efa6464f958`), or a *valid* token still
  **403s** (not 401). Surface this proactively + in the error.
- jsign (picocli) prints the real error **first**, a useless "Try --help" trailer **last**
  (opposite of osslsigncode) — summarize accordingly.
- `osslsigncode verify` on macOS reports chain "failed" because the macOS CA bundle lacks
  the Microsoft roots — **not a defect**; Windows trusts them.
- `.cmd`/`.bat` are **not** Authenticode-signable; jsign needs a JVM; osslsigncode needs
  OpenSSL.

## Open questions to decide next session

- **Name** (working: MacSign) + identity.
- **Code sharing:** extract `WrapTuneMacOS.Signing` to its own package/repo, monorepo, or
  copy? (Prefer shared so fixes land once.)
- v1 cloud backends beyond Azure; how deep the Verify feature goes.
- Distribution/positioning: free + BuyMeACoffee like WrapTune?
- **Validate demand before building polish.**

## Relationship to WrapTune-MacOS

Additive, not a fork. WrapTune keeps its inline sign+wrap one-pass workflow; MacSign is a
standalone signer for the broader "sign Windows stuff on a Mac" use case. Ideally both
consume the same extracted signing library. Source to draw from:
`…/WrapTuneMacOS/src/WrapTuneMacOS.Signing/` and `…/WrapTuneMacOS/docs/PAYLOAD-SIGNING.md`.
