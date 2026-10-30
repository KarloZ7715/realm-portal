#!/usr/bin/env bash
# Self-test for packaging/sign-and-publish.sh. Uses the real minisign with throwaway keys made here (no password) and a
# stub `gh` on PATH that serves one fake release from a temp dir. Nothing talks to GitHub.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUT="${SCRIPT_DIR}/sign-and-publish.sh"
command -v minisign >/dev/null 2>&1 || { echo "error: minisign is required for this test" >&2; exit 1; }
command -v jq >/dev/null 2>&1 || { echo "error: jq is required for this test (the gh stub uses it)" >&2; exit 1; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
REPO="example/realm-portal"

# --- Throwaway keys -----------------------------------------------------------------------------------------------
minisign -G -W -f -p "${tmp}/good.pub" -s "${tmp}/good.key" >/dev/null
minisign -G -W -f -p "${tmp}/other.pub" -s "${tmp}/other.key" >/dev/null

# --- gh stub ------------------------------------------------------------------------------------------------------
mkdir -p "${tmp}/bin"
cat >"${tmp}/bin/gh" <<'STUB'
#!/usr/bin/env bash
# Minimal stand-in for the gh calls sign-and-publish.sh makes. State lives in $STUB_STATE.
set -euo pipefail
state="$STUB_STATE"
rel="${state}/release.json"
echo "gh $*" >>"${state}/calls.log"

jqf=""; accept=""; pos=()
args=("$@")
i=0
while [[ $i -lt ${#args[@]} ]]; do
  a="${args[$i]}"
  case "$a" in
    --jq) jqf="${args[$((i + 1))]}"; i=$((i + 2)) ;;
    -H) accept="${args[$((i + 1))]}"; i=$((i + 2)) ;;
    --repo) i=$((i + 2)) ;;
    *) pos+=("$a"); i=$((i + 1)) ;;
  esac
done

out() { if [[ -n "$jqf" ]]; then jq -r "$jqf"; else cat; fi; }
update() { jq "$@" "$rel" >"${rel}.new" && mv "${rel}.new" "$rel"; }

case "${pos[0]} ${pos[1]:-}" in
  "api "* | "api --paginate")
    path="${pos[*]: -1}"
    case "$path" in
      repos/*/releases\?per_page=100) jq '[.]' "$rel" | out ;;
      repos/*/releases/assets/*) [[ "$accept" == *octet-stream* ]] || exit 1; cat "${state}/assets/${path##*/}" ;;
      repos/*/releases/tags/*)
        [[ "$(jq -r .draft "$rel")" == "false" ]] || { echo "HTTP 404" >&2; exit 1; }
        out <"$rel" ;;
      repos/*/releases/*) out <"$rel" ;;
      repos/*/immutable-releases) printf '{"enabled":%s}' "${STUB_IMMUTABLE_SETTING:-true}" | out ;;
      *) echo "stub: unexpected api path ${path}" >&2; exit 1 ;;
    esac ;;
  "release upload")
    file="${pos[3]}"; name="$(basename "$file")"
    id=$(( $(jq '[.assets[].id] | max' "$rel") + 1 ))
    cp "$file" "${state}/assets/${id}"
    digest="sha256:$(sha256sum "$file" | cut -d' ' -f1)"
    update --arg n "$name" --arg d "$digest" --argjson id "$id" \
      '.assets = ([.assets[] | select(.name != $n)] + [{id: $id, name: $n, digest: $d, browser_download_url: ("https://github.com/x/untagged-1/" + $n)}])' ;;
  "release edit")
    if [[ " $* " == *" --draft=true "* ]]; then
      update '.draft = true'
    else
      pre=false; [[ " $* " == *" --prerelease "* ]] && pre=true
      update --arg repo "$STUB_REPO" --argjson pre "$pre" --argjson imm "${STUB_IMMUTABLE_ON_PUBLISH:-true}" \
        '.draft = false | .prerelease = $pre | .immutable = $imm
         | .assets |= map(.browser_download_url = ("https://github.com/" + $repo + "/releases/download/" + $tag + "/" + .name))' \
        --arg tag "${pos[2]}"
    fi ;;
  "release verify" | "release verify-asset")
    [[ " $* " != *" --help "* ]] || exit 0
    exit "${STUB_VERIFY_RC:-0}" ;;
  *) echo "stub: unexpected gh $*" >&2; exit 1 ;;
esac
STUB
chmod +x "${tmp}/bin/gh"

# new_release <tag> [draft] [prerelease]: a draft with the three packages and SHA256SUMS, as CI leaves it.
new_release() {
  local tag="$1" draft="${2:-true}" ver deb pre
  ver="${tag#v}"
  deb="$("${SCRIPT_DIR}/linux/semver-to-deb-version.sh" "$ver")"
  pre="${3:-$([[ "$ver" == *-* ]] && echo true || echo false)}"
  rm -rf "${tmp}/state" && mkdir -p "${tmp}/state/assets" "${tmp}/state/files"
  : >"${tmp}/state/calls.log"
  local names=("realm-portal_${deb}_amd64.deb" "RealmPortal-${ver}-x86_64.AppImage" "RealmPortal-${ver}-win-x64.zip")
  local n id=1 assets="[]"
  for n in "${names[@]}"; do
    head -c 4096 /dev/urandom >"${tmp}/state/files/${n}"
  done
  (cd "${tmp}/state/files" && sha256sum "${names[@]}" >SHA256SUMS)
  for n in "${names[@]}" SHA256SUMS; do
    cp "${tmp}/state/files/${n}" "${tmp}/state/assets/${id}"
    assets="$(jq --arg n "$n" --arg d "sha256:$(sha256sum "${tmp}/state/files/${n}" | cut -d' ' -f1)" --argjson id "$id" \
      '. + [{id: $id, name: $n, digest: $d, browser_download_url: ("https://github.com/x/untagged-1/" + $n)}]' <<<"$assets")"
    id=$((id + 1))
  done
  jq -n --arg tag "$tag" --argjson draft "$draft" --argjson pre "$pre" --argjson assets "$assets" \
    '{id: 42, tag_name: $tag, draft: $draft, prerelease: $pre, immutable: false, assets: $assets}' >"${tmp}/state/release.json"
}

# replace_asset <name> <file>: swap an asset's content and its reported digest (a consistent but wrong upload).
replace_asset() {
  local name="$1" file="$2" id
  id="$(jq -r --arg n "$name" '.assets[] | select(.name == $n) | .id' "${tmp}/state/release.json")"
  cp "$file" "${tmp}/state/assets/${id}"
  jq --arg n "$name" --arg d "sha256:$(sha256sum "$file" | cut -d' ' -f1)" \
    '.assets |= map(if .name == $n then .digest = $d else . end)' "${tmp}/state/release.json" >"${tmp}/r.json"
  mv "${tmp}/r.json" "${tmp}/state/release.json"
}

run() {
  env PATH="${tmp}/bin:${PATH}" STUB_STATE="${tmp}/state" STUB_REPO="$REPO" SIGN_PUBLISH_RETRY_SECONDS=0 \
    "${extra_env[@]}" "$SUT" --repo "$REPO" "$@" </dev/null >"${tmp}/out.log" 2>&1
}

failures=0
extra_env=()
pass() { echo "ok: $1"; }
fail() { echo "FAIL: $1"; sed 's/^/    /' "${tmp}/out.log"; failures=$((failures + 1)); }
published() { grep -q -- '--draft=false' "${tmp}/state/calls.log"; }
uploaded() { grep -q '^gh release upload' "${tmp}/state/calls.log"; }

expect_ok() { # <name> <args...>
  local name="$1"; shift
  if run "$@"; then pass "$name"; else fail "$name (exit $?)"; fi
}
expect_fail() { # <name> <message regex> <args...>
  local name="$1" msg="$2"; shift 2
  if run "$@"; then
    fail "$name (should have failed)"
  elif ! grep -qE -- "$msg" "${tmp}/out.log"; then
    fail "$name (wrong error, wanted /${msg}/)"
  else
    pass "$name"
  fi
}
check() { # <name> <condition...>
  local name="$1"; shift
  if "$@"; then pass "$name"; else fail "$name"; fi
}
not() { ! "$@"; }

GOOD=(--pubkey "${tmp}/good.pub" --key "${tmp}/good.key")

# Happy paths
new_release v1.2.0
expect_ok "stable release is signed and published" "${GOOD[@]}" --yes v1.2.0
check "  stable: published as latest" grep -q -- '--draft=false --latest=true' "${tmp}/state/calls.log"
check "  stable: draft is now published" test "$(jq -r .draft "${tmp}/state/release.json")" = false
check "  stable: signature has the tag as trusted comment" \
  test "$(minisign -V -H -Q -p "${tmp}/good.pub" -m "${tmp}/state/files/SHA256SUMS" -x "${tmp}/state/assets/5")" = "realm-portal v1.2.0"

new_release v1.2.0-rc.1
expect_ok "rc release is published as prerelease" "${GOOD[@]}" --yes v1.2.0-rc.1
check "  rc: --prerelease --latest=false" grep -q -- '--draft=false --prerelease --latest=false' "${tmp}/state/calls.log"

new_release v1.2.0
minisign -S -s "${tmp}/good.key" -m "${tmp}/state/files/SHA256SUMS" -x "${tmp}/premade.minisig" -t "realm-portal v1.2.0" >/dev/null
expect_ok "ready-made signature is accepted" --pubkey "${tmp}/good.pub" --signature "${tmp}/premade.minisig" --yes v1.2.0

new_release v1.2.0
: >"${tmp}/state/assets/9"
jq '.assets += [{id: 9, name: "SHA256SUMS.minisig", browser_download_url: "x",
    digest: "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}]' \
  "${tmp}/state/release.json" >"${tmp}/r.json" && mv "${tmp}/r.json" "${tmp}/state/release.json"
expect_ok "signature left by an earlier run is replaced" "${GOOD[@]}" --yes v1.2.0

# Dry run
new_release v1.2.0
expect_ok "dry run passes" "${GOOD[@]}" --dry-run v1.2.0
check "  dry run: nothing uploaded" not uploaded
check "  dry run: nothing published" not published

# Refusals before anything is uploaded
new_release v1.2.0 false
expect_fail "published release is refused" "not a draft" "${GOOD[@]}" --yes v1.2.0
check "  not a draft: nothing uploaded" not uploaded

new_release v1.2.0
expect_fail "missing public key" "public key .* not found" --pubkey "${tmp}/none.pub" --key "${tmp}/good.key" --yes v1.2.0
printf 'untrusted comment: PLACEHOLDER, not a real key\nRWQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n' >"${tmp}/placeholder.pub"
expect_fail "placeholder public key" "placeholder" --pubkey "${tmp}/placeholder.pub" --key "${tmp}/good.key" --yes v1.2.0
printf 'just some text\n' >"${tmp}/junk.pub"
expect_fail "malformed public key" "not a minisign public key" --pubkey "${tmp}/junk.pub" --key "${tmp}/good.key" --yes v1.2.0
expect_fail "no secret key" "no secret key" --pubkey "${tmp}/good.pub" --yes v1.2.0
expect_fail "bad tag" "tag must look like" "${GOOD[@]}" --yes 1.2.0
check "  input errors: nothing uploaded" not uploaded

minisign -S -s "${tmp}/good.key" -m "${tmp}/state/files/SHA256SUMS" -x "${tmp}/wrongtag.minisig" -t "realm-portal v1.1.9" >/dev/null
expect_fail "wrong trusted comment" "trusted comment is 'realm-portal v1.1.9'" \
  --pubkey "${tmp}/good.pub" --signature "${tmp}/wrongtag.minisig" --yes v1.2.0
minisign -S -s "${tmp}/good.key" -m "${tmp}/state/files/SHA256SUMS" -x "${tmp}/default.minisig" >/dev/null
expect_fail "default (timestamp) trusted comment" "expected 'realm-portal v1.2.0'" \
  --pubkey "${tmp}/good.pub" --signature "${tmp}/default.minisig" --yes v1.2.0
expect_fail "key that does not match the public key" "does not verify" \
  --pubkey "${tmp}/good.pub" --key "${tmp}/other.key" --yes v1.2.0
minisign -S -l -s "${tmp}/good.key" -m "${tmp}/state/files/SHA256SUMS" -x "${tmp}/legacy.minisig" -t "realm-portal v1.2.0" >/dev/null
expect_fail "legacy (non-prehashed) signature" "does not verify" \
  --pubkey "${tmp}/good.pub" --signature "${tmp}/legacy.minisig" --yes v1.2.0
check "  bad signatures: nothing uploaded" not uploaded

# Tampered SHA256SUMS: a consistent upload whose listed digest does not match the package.
new_release v1.2.0
sed 's/^./0/' "${tmp}/state/files/SHA256SUMS" >"${tmp}/tampered"
replace_asset SHA256SUMS "${tmp}/tampered"
expect_fail "tampered SHA256SUMS" "does not match the uploaded asset" "${GOOD[@]}" --yes v1.2.0
check "  tampered: nothing uploaded" not uploaded

# SHA256SUMS whose bytes do not match the digest GitHub reports.
new_release v1.2.0
printf 'x' >>"${tmp}/state/assets/4"
expect_fail "SHA256SUMS download differs from GitHub's digest" "does not match the digest GitHub reports" "${GOOD[@]}" --yes v1.2.0

new_release v1.2.0
jq '.assets += [{id: 8, name: "extra.txt", digest: ("sha256:" + ("a" * 64)), browser_download_url: "x"}]' \
  "${tmp}/state/release.json" >"${tmp}/r.json" && mv "${tmp}/r.json" "${tmp}/state/release.json"
expect_fail "unexpected extra asset" "unexpected release assets" "${GOOD[@]}" --yes v1.2.0

new_release v1.2.0 true true
expect_fail "stable tag on a prerelease draft" "prerelease flag" "${GOOD[@]}" --yes v1.2.0

new_release v1.2.0
extra_env=(STUB_IMMUTABLE_SETTING=false)
expect_fail "immutable releases disabled" "immutable releases are not enabled" "${GOOD[@]}" --yes v1.2.0
check "  immutable disabled: nothing uploaded" not uploaded
extra_env=()

# Confirmation
new_release v1.2.0
expect_fail "no --yes without a terminal" "pass --yes" "${GOOD[@]}" v1.2.0
check "  no confirmation: not published" not published

# After publishing
new_release v1.2.0
extra_env=(STUB_IMMUTABLE_ON_PUBLISH=false)
expect_fail "mutable release is reverted to draft" "NOT immutable" "${GOOD[@]}" --yes v1.2.0
check "  mutable: back to draft" test "$(jq -r .draft "${tmp}/state/release.json")" = true
extra_env=()

new_release v1.2.0
extra_env=(STUB_VERIFY_RC=1)
expect_fail "missing attestation is reported" "cannot be reverted" "${GOOD[@]}" --yes v1.2.0
extra_env=()

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} sign-and-publish test(s) failed" >&2
  exit 1
fi
echo "All sign-and-publish tests passed"
