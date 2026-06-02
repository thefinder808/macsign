# Releasing MacSign (signed + notarized DMG)

Pushing a `v*` tag triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml),
which on a stock macOS runner: runs the tests, **codesigns** the app with the
Developer ID identity, **notarizes** with Apple, **staples**, and attaches the
`osx-arm64` + `osx-x64` DMGs to a GitHub Release.

```bash
git tag v1.2.3
git push origin v1.2.3        # or: Actions → Release → Run workflow
```

The same flow runs locally with no secrets via [`build-macos.sh`](../build-macos.sh)
(`SIGN_IDENTITY=… NOTARY_PROFILE=… ./build-macos.sh`) — CI just automates it and
builds both architectures from a clean checkout of the tagged commit.

## Required secrets (`release` environment)

Until these exist the workflow still runs but produces **unsigned** DMGs.

| Secret | What |
|---|---|
| `APPLE_SIGN_IDENTITY` | `Developer ID Application: NAME (TEAMID)` |
| `APPLE_CERT_P12_BASE64` | base64 of the Developer ID Application `.p12` (cert **+ private key**) |
| `APPLE_CERT_PASSWORD` | the `.p12` export password |
| `APPLE_API_KEY_P8_BASE64` | base64 of the App Store Connect notary `.p8` key |
| `APPLE_API_KEY_ID` | notary key id |
| `APPLE_API_ISSUER` | notary issuer id |

### Producing the inputs

- **Cert `.p12`** — Keychain Access → *login → My Certificates* → right-click the
  Developer ID Application cert → **Export…** → save as `.p12` with a password
  (include the private key).
- **Notary API key** — App Store Connect → *Users and Access → Integrations →
  App Store Connect API* → generate a key (Developer role); download the `.p8`
  once and note the Key ID + Issuer ID.

### Setting them (`gh`)

```bash
REPO=thefinder808/macsign
gh api -X PUT repos/$REPO/environments/release >/dev/null   # create the env

gh secret set APPLE_SIGN_IDENTITY --env release --repo $REPO \
  --body "Developer ID Application: NAME (TEAMID)"
base64 -i cert.p12 | gh secret set APPLE_CERT_P12_BASE64 --env release --repo $REPO
gh secret set APPLE_CERT_PASSWORD --env release --repo $REPO        # paste when prompted
base64 -i AuthKey_XXXX.p8 | gh secret set APPLE_API_KEY_P8_BASE64 --env release --repo $REPO
gh secret set APPLE_API_KEY_ID    --env release --repo $REPO        # paste Key ID
gh secret set APPLE_API_ISSUER    --env release --repo $REPO        # paste Issuer ID
```

## Hardening (important for a public repo)

A code-signing identity in CI is high value, so:

- **Required reviewers** on the `release` environment (Settings → Environments →
  release): every signed run then pauses for one-click approval before the
  secrets are decrypted. Restrict deployment to the `v*` tag pattern.
- **Pinned actions** — `release.yml` pins `actions/*` to commit SHAs so a
  hijacked action tag can't run with the secrets.
- **Safe triggers** — `release.yml` fires only on tag push + manual dispatch
  (both need write access); fork PRs never receive secrets. Do **not** add
  `pull_request`/`pull_request_target` to it.
- **Dedicated, revocable cert** — consider a separate Developer ID Application
  cert used only for CI, so a leak is revoked without disrupting local signing.
- **Rotate on exposure** — revoke the cert (Apple Developer portal) and the
  notary key (App Store Connect) at any time.
