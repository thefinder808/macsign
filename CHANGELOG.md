# Changelog

All notable changes to MacSign are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims to
follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **The signing engine is now published to NuGet** as four packages —
  `MacSign.Signing` (PE + PowerShell formats, PFX signing, RFC3161 timestamping,
  verification), `MacSign.Signing.Msi`, `MacSign.Signing.Pkcs11`, and
  `MacSign.Signing.Azure` — so other apps (first consumer: WrapTune MacOS) can sign
  in-process instead of shelling out to external tools. A new `nuget.yml` workflow
  packs and pushes on every release tag, authenticated via nuget.org **Trusted
  Publishing** (GitHub OIDC exchanged for a one-hour API key) — no long-lived
  secret to store or rotate.

## [1.1.5] — 2026-06-08

### Fixed
- **Azure Trusted Signing now works when MacSign is launched from Finder/Dock/Launchpad.**
  A macOS app launched from the Finder inherits only the minimal launchd `PATH`
  (`/usr/bin:/bin:/usr/sbin:/sbin`), which hides a Homebrew-installed `az`. Azure.Identity's
  `AzureCliCredential` then reported *"Azure CLI not installed"* and the whole credential
  chain failed — even after a successful `az login`. MacSign now restores the standard
  Homebrew tool directories (`/opt/homebrew/bin`, `/usr/local/bin`) on its `PATH` at startup,
  so your existing `az login` token is found. (Launching from a terminal already inherited
  these and was unaffected.)

### Changed
- The Azure credential hint on the Sign screen no longer claims a token was *"acquired
  automatically"* before any token is fetched; it now describes the credential source instead.

## [1.1.4] — 2026-06-04

### Fixed
- **Settings are now saved atomically.** `settings.json` is written to a temp file and
  atomically renamed into place, so a crash or full disk mid-write can no longer truncate
  it. If an existing settings file is unreadable, it's preserved as `settings.json.bak`
  rather than silently discarded — your profiles, activity, and preferences stay recoverable.
- **The updater no longer leaves abandoned download disk images behind.** A download whose
  verify/install didn't complete is cleaned out of the temp folder before the next attempt,
  so failed update retries can't pile up.

### Changed
- Internal resource-hygiene: certificate handles obtained while verifying a signature, and
  the PKCS#11 signer's leaf certificate, are now disposed promptly instead of waiting on
  finalization. No user-visible behavior change.

## [1.1.3] — 2026-06-04

### Fixed
- **Update dialog: the primary "Install & Relaunch" button was clipped** off the right
  edge of the fixed-width window. The action row is now two rows that fit the dialog,
  and the dialog is keyboard-operable (Enter = Install, Esc = Later). The dialog is
  drawn by the running version, so this applies to updates triggered from 1.1.3 onward.

## [1.1.2] — 2026-06-04

### Changed
- Release DMGs are now **code-signed with the Developer ID Application identity**, in
  addition to being notarized and stapled — completing the standard macOS distribution
  layout (app signed · DMG signed · DMG notarized · ticket stapled). No application
  changes; this is a packaging/release update.

## [1.1.1] — 2026-06-04

### Fixed
- **Auto-updater rejected every legitimate release.** The trust gate verified the
  downloaded `.dmg` container's own code signature, but release DMGs are notarized and
  stapled while the image itself is not codesigned (the `.app` inside is). The updater
  now verifies the notarized, Developer-ID-signed **app inside** the DMG (plus the
  DMG's stapled ticket), so one-click update works. It still fails safe — anything that
  doesn't verify is never installed.
- **Preferences "Updates" card was clipped at the bottom.** The Preferences screen used
  `ScrollViewer` padding, whose trailing edge falls outside the scrollable area, so the
  last card couldn't be fully scrolled into view. Fixed by moving the spacing inside the
  scroll content.

## [1.1.0] — 2026-06-04

### Added
- **In-app auto-updates.** MacSign checks the GitHub Releases API for a newer version
  on launch (throttled to once a day, on by default), via **Help → "Check for
  Updates…"**, and from a new **Preferences → Updates** section. A found update offers
  one-click **download → verify → install → relaunch**.
- The trust anchor is the downloaded DMG's **own Apple notarization + Developer ID Team
  ID** — it installs only when the artifact is codesigned by that identity and notarized
  (stapled + Gatekeeper-accepted), so no separate appcast or signing key is needed.
- Graceful degradation: if verification fails or the install directory isn't writable,
  it never installs — it offers the release page / "drag to Applications" instead.

## [1.0.0] — 2026-06-04

First stable release. MacSign is feature-complete and open-source: a fully-managed
.NET 10 Authenticode engine (PE / PowerShell / MSI; PFX / PKCS#11 / Azure Trusted
Signing; RFC3161 timestamping; verify + remove) with a native macOS GUI, plus signing,
notarizing, and stapling of Mac `.app`/`.dmg` artifacts. This release adds the
open-source licensing and project files and removes personal defaults from the UI.

### Added
- `LICENSE` (Apache-2.0) + `NOTICE`, and community-health files: `SECURITY.md`,
  `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, issue/PR templates, and `FUNDING.yml`.
- `macsign --version` (also `-v` / `version`).
- A project-wide `<Version>` in `Directory.Build.props`.

### Changed
- Sign screen now defaults to the **PFX** credential mode with empty Azure
  account/certificate-profile fields; the Mac signing screen's notary-profile field
  starts empty. (Generic placeholder hints replace previously pre-filled values.)
- Test fixtures use a generic signing identity instead of a real Developer ID.

## [0.6.0] — 2026-06-04

### Added
- Native menu bar gains **Edit** (Cut/Copy/Paste/Select All), **Window**
  (Minimize/Zoom), and **Help** (GitHub/Issues/Releases/About) menus, plus an About box.
- **Remove signature** in the GUI: the Verify screen can strip a signed Authenticode
  file in place (two-step confirm), completing the Sign / Verify / Remove triad.
- In-app **notary-profile setup**: create the `notarytool` keychain profile from an App
  Store Connect API key without using Terminal.
- **Preferences** screen (⌘,): appearance/theme override, signing defaults, and
  activity/data housekeeping.

### Changed
- Sidebar is grouped into sections (Windows · macOS · Library · App).
- The macOS signing screen is labeled **Sign** in the UI for parallelism with the
  Windows Sign screen.

## [0.5.0] — 2026-06-03

### Added
- **Sign contents & continue**: when pre-flight finds unsigned `.app` bundles inside a
  `.dmg`, sign only those (Hardened Runtime + deep + optional entitlements), re-seal the
  image in place, then continue to notarize + staple.

## [0.4.0] — 2026-06-03

### Added
- The **Verify** screen also verifies Mac artifacts: a `.app`/`.dmg` reports signer,
  Team ID, Hardened Runtime, notarization + stapling, and Gatekeeper status.
- A **notarization pre-flight** runs before submitting — for a `.dmg` it mounts and
  checks each `.app` inside, stopping with the offending binaries (and a "Notarize
  anyway" override) rather than burning a round-trip on a doomed upload.

## [0.3.0] — 2026-06-03

### Added
- New **Mac apps** screen: sign + notarize + staple a `.app` bundle or `.dmg` by driving
  Apple's own `codesign` / `notarytool` / `stapler` through an injection-safe wrapper.
- Sign-screen list management: per-row remove, Clear, Clear signed, a select-all toggle,
  and list virtualization.

### Changed
- All GitHub Actions bumped to Node 24 and pinned to commit SHAs.

## [0.2.0] — 2026-06-02

### Added
- `macsign remove` — strip an existing signature (PE / PowerShell / MSI).
- Multi-TSA fallback (comma-separated `--timestamp-url`, tried in order).
- Verify reports every co-signer and flags a nested signature.

### Changed / Hardened
- `verify` never throws — it returns a Failed report on any error (hostile input safe).
- Strict PE attribute-certificate-table recognition (no crash / no corrupt re-sign on
  crafted input).
- RFC3161 timestamps are cryptographically validated on verify (not just decoded), so a
  forged or grafted token is not reported as the signing time.
- The Azure-delegated signature is self-verified before it is embedded.
- The CLI warns when a secret is passed in plaintext on argv.
- GUI: signing runs off the UI thread with Cancel + "N of M" progress.

## [0.1.0] — 2026-06-02

### Added
- Initial release. Fully-managed .NET 10 Authenticode engine — **PE** (`.exe`/`.dll`/
  `.sys`), **PowerShell** (`.ps1`), and **MSI** — with **PFX**, **PKCS#11 / HSM**, and
  **Azure Trusted Signing** credentials, optional **RFC3161 timestamping**, and signature
  **verify** (integrity vs. chain trust).
- Native macOS GUI (Avalonia): Sign / Verify / Profiles / Activity.
- Tag-driven release CI that builds, signs, and notarizes the `arm64` + `x64` DMGs.

[Unreleased]: https://github.com/thefinder808/macsign/compare/v1.1.5...HEAD
[1.1.5]: https://github.com/thefinder808/macsign/releases/tag/v1.1.5
[1.1.4]: https://github.com/thefinder808/macsign/releases/tag/v1.1.4
[1.1.0]: https://github.com/thefinder808/macsign/releases/tag/v1.1.0
[1.0.0]: https://github.com/thefinder808/macsign/releases/tag/v1.0.0
[0.6.0]: https://github.com/thefinder808/macsign/releases/tag/v0.6.0
[0.5.0]: https://github.com/thefinder808/macsign/releases/tag/v0.5.0
[0.4.0]: https://github.com/thefinder808/macsign/releases/tag/v0.4.0
[0.3.0]: https://github.com/thefinder808/macsign/releases/tag/v0.3.0
[0.2.0]: https://github.com/thefinder808/macsign/releases/tag/v0.2.0
[0.1.0]: https://github.com/thefinder808/macsign/releases/tag/v0.1.0
