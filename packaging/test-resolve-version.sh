#!/usr/bin/env bash
# Self-test for packaging/resolve-version.sh against a temporary Directory.Build.props.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESOLVE="${SCRIPT_DIR}/resolve-version.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
props="${tmp}/Directory.Build.props"
printf '<Project>\r\n  <PropertyGroup>\r\n    <Version>2.3.4</Version>\r\n  </PropertyGroup>\r\n</Project>\r\n' >"$props"

failures=0

expect_ok() {
  local expected="$1"; shift
  local actual
  if actual="$(env -i PATH="$PATH" PROPS_FILE="$props" "$@" "$RESOLVE" 2>/dev/null)" && [[ "$actual" == "$expected" ]]; then
    echo "ok: $* -> ${actual}"
  else
    echo "FAIL: $* -> '${actual:-<error>}' (expected ${expected})"
    failures=$((failures + 1))
  fi
}

expect_fail() {
  if env -i PATH="$PATH" PROPS_FILE="$props" "$@" "$RESOLVE" >/dev/null 2>&1; then
    echo "FAIL: $* should have failed"
    failures=$((failures + 1))
  else
    echo "ok: $* fails"
  fi
}

expect_ok 2.3.4 EVENT_NAME=push REF_NAME=v2.3.4
expect_fail EVENT_NAME=push REF_NAME=v2.3.5
expect_ok 2.3.4-rc.1 EVENT_NAME=push REF_NAME=v2.3.4-rc.1
expect_ok 2.3.4-rc.2 EVENT_NAME=push REF_NAME=v2.3.4-rc.2
expect_fail EVENT_NAME=push REF_NAME=v2.3.5-rc.1
expect_fail EVENT_NAME=push REF_NAME=main
expect_ok 0.0.0-pr.17 EVENT_NAME=pull_request PR_NUMBER=17
expect_fail EVENT_NAME=pull_request PR_NUMBER=
expect_ok 2.3.4 EVENT_NAME=workflow_dispatch
expect_ok 2.3.4 EVENT_NAME=workflow_dispatch INPUT_VERSION=2.3.4
expect_ok 2.3.4-rc.1 EVENT_NAME=workflow_dispatch INPUT_VERSION=2.3.4-rc.1
expect_fail EVENT_NAME=workflow_dispatch INPUT_VERSION=9.9.9
expect_fail EVENT_NAME=workflow_dispatch INPUT_VERSION='2.3.4-bad version'
expect_fail EVENT_NAME=schedule

# The real Directory.Build.props must resolve too.
if ! env -i PATH="$PATH" EVENT_NAME=workflow_dispatch "$RESOLVE" >/dev/null; then
  echo "FAIL: repository Directory.Build.props does not resolve"
  failures=$((failures + 1))
fi

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} failure(s)"
  exit 1
fi
echo "all resolve-version checks passed"
