#!/usr/bin/env bash
#
# Re-verify the Azure Trusted Signing path end-to-end against the LIVE account,
# using your interactive `az login` — no stored credentials, no "robot" identity.
# It signs a throwaway copy of a PE via Azure (the key never leaves Azure), then
# verifies the result (and cross-checks the digest with osslsigncode if present).
#
# Config comes from env vars so personal account names stay OUT of git:
#   TS_ENDPOINT   e.g. eus.codesigning.azure.net
#   TS_ACCOUNT    your Trusted Signing account name
#   TS_PROFILE    your certificate profile name
#   TS_TENANT     (optional) tenant GUID or domain, when the signing account is not in
#                 the tenant `az login` defaults to
# A gitignored scripts/azure.env is sourced automatically if it exists.
#
# Usage:  ./scripts/verify-azure.sh [path-to-file]   (defaults to the fixture DLL)
#
set -euo pipefail
cd "$(dirname "$0")/.."

# shellcheck disable=SC1091
[ -f scripts/azure.env ] && source scripts/azure.env
: "${TS_ENDPOINT:?set TS_ENDPOINT (or create scripts/azure.env)}"
: "${TS_ACCOUNT:?set TS_ACCOUNT (or create scripts/azure.env)}"
: "${TS_PROFILE:?set TS_PROFILE (or create scripts/azure.env)}"

echo "Building…"
dotnet build -c Release >/dev/null
CLI=(dotnet run --project src/MacSign.Cli -c Release --no-build --)

SRC="${1:-src/MacSign.Fixture/bin/Release/net10.0/MacSign.Fixture.dll}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
TARGET="$WORK/$(basename "$SRC")"
cp "$SRC" "$TARGET"

echo "Signing via Azure Trusted Signing ($TS_ACCOUNT / $TS_PROFILE) — key never leaves Azure…"
# Only pass --trusted-signing-tenant when TS_TENANT is set; an empty value would be read as
# the next flag's name by the CLI's parser.
TENANT_ARGS=()
[ -n "${TS_TENANT:-}" ] && TENANT_ARGS=(--trusted-signing-tenant "$TS_TENANT")

# The ${arr[@]+"${arr[@]}"} guard is load-bearing, not style: macOS ships bash 3.2, where
# expanding an EMPTY array under `set -u` is an "unbound variable" error. A plain
# "${TENANT_ARGS[@]}" therefore killed the script in the common case of no tenant set.
"${CLI[@]}" sign \
  --trusted-signing-endpoint "$TS_ENDPOINT" \
  --trusted-signing-account "$TS_ACCOUNT" \
  --trusted-signing-profile "$TS_PROFILE" \
  ${TENANT_ARGS[@]+"${TENANT_ARGS[@]}"} \
  --description "MacSign Azure re-verify" "$TARGET"

echo
echo "Verifying…"
"${CLI[@]}" verify "$TARGET"

if command -v osslsigncode >/dev/null 2>&1; then
  echo
  echo "Independent digest check (osslsigncode)…"
  osslsigncode verify "$TARGET" 2>&1 | grep -iE "message digest|calculated|number of verified" || true
fi

echo
echo "OK — the live Azure signing path works."
