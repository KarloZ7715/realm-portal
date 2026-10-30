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
# Behaves like the gh calls sign-and-publish.sh makes, including gh's quirk that `gh release <unknown> --help` prints the
# general help and exits 0. Knobs (environment):
#   STUB_OLD_GH=1                    gh without `release verify` / `verify-asset`
#   STUB_FAIL_LIST=1                 listing releases fails (network or API error)
#   STUB_NO_DIGEST=1                 draft uploads have no digest field (published assets always get one)
#   STUB_SWAP_ON_PUBLISH=<name>      publishing also changes that asset's bytes (and its digest)
#   STUB_FAIL_EDIT_AFTER=1           `release edit` is applied on the server but the client reports an error
#   STUB_IMMUTABLE_SETTING=false     immutable releases are off for the repository
#   STUB_IMMUTABLE_ON_PUBLISH=false  the published release is not immutable
#   STUB_VERIFY_RC=1                 `gh release verify` / `verify-asset` fail
mkdir -p "${tmp}/bin"
cat >"${tmp}/bin/gh" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
state="$STUB_STATE"
rel="${state}/release.json"
echo "gh $*" >>"${state}/calls.log"

release_help() {
  cat <<'HELP'
Manage releases

USAGE
  gh release <command> [flags]

GENERAL COMMANDS
  create:        Create a new release
  list:          List releases in a repository

TARGETED COMMANDS
  delete:        Delete a release
  delete-asset:  Delete an asset from a release
  download:      Download release assets
  edit:          Edit a release
  upload:        Upload assets to a release
HELP
  if [[ "${STUB_OLD_GH:-}" != "1" ]]; then
    echo "  verify:        Verify the attestation for a release"
    echo "  verify-asset:  Verify that a given asset originated from a release"
  fi
  echo "  view:          View information about a release"
}

if [[ "${1:-}" == "release" && " $* " == *" --help "* ]]; then
  # Real gh prints the general release help and exits 0 for `--help`, even after an unknown subcommand.
  release_help
  exit 0
fi

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
digest_of() { echo "sha256:$(sha256sum "$1" | cut -d' ' -f1)"; }

case "${pos[0]} ${pos[1]:-}" in
  "api "* | "api --paginate")
    path="${pos[*]: -1}"
    case "$path" in
      repos/*/releases\?per_page=100)
        [[ "${STUB_FAIL_LIST:-}" != "1" ]] || { echo "error connecting to api.github.com" >&2; exit 1; }
        jq '[.]' "$rel" | out ;;
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
    digest="$(digest_of "$file")"
    [[ "${STUB_NO_DIGEST:-}" != "1" ]] || digest=""
    update --arg n "$name" --arg d "$digest" --argjson id "$id" --argjson size "$(stat -c %s "$file")" \
      '.assets = ([.assets[] | select(.name != $n)] + [{id: $id, name: $n, size: $size, uploader: {login: "maintainer"},
        browser_download_url: ("https://github.com/x/untagged-1/" + $n)} + (if $d == "" then {} else {digest: $d} end)])' ;;
  "release edit")
    if [[ " $* " == *" --draft=true "* ]]; then
      update '.draft = true'
    else
      pre=false; [[ " $* " == *" --prerelease "* ]] && pre=true
      if [[ -n "${STUB_SWAP_ON_PUBLISH:-}" ]]; then
        id="$(jq -r --arg n "$STUB_SWAP_ON_PUBLISH" '.assets[] | select(.name == $n) | .id' "$rel")"
        head -c 4096 /dev/urandom >"${state}/assets/${id}"
      fi
      # Published assets always carry a digest.
      for f in "${state}"/assets/*; do
        update --argjson id "${f##*/}" --arg d "$(digest_of "$f")" '.assets |= map(if .id == $id then .digest = $d else . end)'
      done
      update --arg repo "$STUB_REPO" --argjson pre "$pre" --argjson imm "${STUB_IMMUTABLE_ON_PUBLISH:-true}" \
        '.draft = false | .prerelease = $pre | .immutable = $imm
         | .assets |= map(.browser_download_url = ("https://github.com/" + $repo + "/releases/download/" + $tag + "/" + .name))' \
        --arg tag "${pos[2]}"
      [[ "${STUB_FAIL_EDIT_AFTER:-}" != "1" ]] || { echo "error: connection reset" >&2; exit 1; }
    fi ;;
  "release verify" | "release verify-asset")
    [[ "${STUB_OLD_GH:-}" != "1" ]] || { release_help; exit 0; }
    exit "${STUB_VERIFY_RC:-0}" ;;
  *) echo "stub: unexpected gh $*" >&2; exit 1 ;;
esac
STUB
chmod +x "${tmp}/bin/gh"

# new_release <tag> [draft] [prerelease]: a draft with the three packages and SHA256SUMS, as CI leaves it.
# With no_digest=1 the assets have no digest field.
no_digest=""
new_release() {
  local tag="$1" draft="${2:-true}" ver deb pre
  ver="${tag#v}"
  deb="$("${SCRIPT_DIR}/linux/semver-to-deb-version.sh" "$ver")"
  pre="${3:-$([[ "$ver" == *-* ]] && echo true || echo false)}"
  rm -rf "${tmp}/state" && mkdir -p "${tmp}/state/assets" "${tmp}/state/files"
  : >"${tmp}/state/calls.log"
  local names=("realm-portal_${deb}_amd64.deb" "RealmPortal-${ver}-x86_64.AppImage" "RealmPortal-${ver}-win-x64.zip")
  local n id=1 assets="[]" d
  for n in "${names[@]}"; do
    head -c 4096 /dev/urandom >"${tmp}/state/files/${n}"
  done
  (cd "${tmp}/state/files" && sha256sum "${names[@]}" >SHA256SUMS)
  for n in "${names[@]}" SHA256SUMS; do
    cp "${tmp}/state/files/${n}" "${tmp}/state/assets/${id}"
    d="sha256:$(sha256sum "${tmp}/state/files/${n}" | cut -d' ' -f1)"
    [[ "$no_digest" != "1" ]] || d=""
    assets="$(jq --arg n "$n" --arg d "$d" --argjson id "$id" --argjson size "$(stat -c %s "${tmp}/state/files/${n}")" \
      '. + [{id: $id, name: $n, size: $size, uploader: {login: "github-actions[bot]"},
        browser_download_url: ("https://github.com/x/untagged-1/" + $n)} + (if $d == "" then {} else {digest: $d} end)]' \
      <<<"$assets")"
    id=$((id + 1))
  done
  jq -n --arg tag "$tag" --argjson draft "$draft" --argjson pre "$pre" --argjson assets "$assets" \
    '{id: 42, tag_name: $tag, draft: $draft, prerelease: $pre, immutable: false, assets: $assets}' >"${tmp}/state/release.json"
}

# replace_asset <name> <file> [uploader]: swap an asset's content, size and reported digest (a consistent replacement).
replace_asset() {
  local name="$1" file="$2" uploader="${3:-github-actions[bot]}"
  local id
  id="$(jq -r --arg n "$name" '.assets[] | select(.name == $n) | .id' "${tmp}/state/release.json")"
  cp "$file" "${tmp}/state/assets/${id}"
  jq --arg n "$name" --arg d "sha256:$(sha256sum "$file" | cut -d' ' -f1)" --arg u "$uploader" \
    --argjson size "$(stat -c %s "$file")" \
    '.assets |= map(if .name == $n then .digest = $d | .size = $size | .uploader.login = $u else . end)' \
    "${tmp}/state/release.json" >"${tmp}/r.json"
  mv "${tmp}/r.json" "${tmp}/state/release.json"
}

run() {
  env -u GITHUB_ACTIONS PATH="${tmp}/bin:${PATH}" STUB_STATE="${tmp}/state" STUB_REPO="$REPO" SIGN_PUBLISH_RETRY_SECONDS=0 \
    "${extra_env[@]}" "$SUT" --repo "$REPO" "$@" </dev/null >"${tmp}/out.log" 2>&1
}

failures=0
extra_env=()
pass() { echo "ok: $1"; }
fail() { echo "FAIL: $1"; sed 's/^/    /' "${tmp}/out.log"; failures=$((failures + 1)); }
published() { grep -q -- '--draft=false' "${tmp}/state/calls.log"; }
uploaded() { grep -q '^gh release upload' "${tmp}/state/calls.log"; }
signed() { grep -q 'Signing SHA256SUMS' "${tmp}/out.log"; }
said() { grep -qF -- "$1" "${tmp}/out.log"; }
is_draft() { test "$(jq -r .draft "${tmp}/state/release.json")" = "$1"; }
sha_of() { sha256sum "$1" | cut -d' ' -f1; }

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
jq '.assets += [{id: 9, name: "SHA256SUMS.minisig", browser_download_url: "x", size: 0, uploader: {login: "maintainer"},
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
expect_fail "published release points to --verify-published" "already published. To check it, run: .*--verify-published v1.2.0" \
  "${GOOD[@]}" --yes v1.2.0
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

# Confirmation comes before signing
new_release v1.2.0
expect_fail "no --yes without a terminal" "pass --yes" "${GOOD[@]}" v1.2.0
check "  no confirmation: not signed" not signed
check "  no confirmation: nothing uploaded" not uploaded
check "  no confirmation: table was printed first" said "$(sha_of "${tmp}/state/files/SHA256SUMS")"

if command -v script >/dev/null 2>&1; then
  # tty_run <answer> <args...>: run under a terminal (util-linux script) and type <answer> at the prompt.
  tty_run() {
    local answer="$1" cmd
    shift
    printf -v cmd '%q ' env -u GITHUB_ACTIONS PATH="${tmp}/bin:${PATH}" STUB_STATE="${tmp}/state" STUB_REPO="$REPO" \
      SIGN_PUBLISH_RETRY_SECONDS=0 "$SUT" --repo "$REPO" "$@"
    { sleep 2; printf '%s\n' "$answer"; sleep 3; } | script -qec "$cmd" /dev/null >"${tmp}/out.log" 2>&1
  }
  new_release v1.2.0
  if tty_run v1.2.0 "${GOOD[@]}" v1.2.0 && is_draft false; then
    pass "typing the tag signs and publishes"
  else
    fail "typing the tag signs and publishes"
  fi
  prompt_line="$(grep -n 'Type the tag' "${tmp}/out.log" | head -1 | cut -d: -f1)"
  sign_line="$(grep -n 'Signing SHA256SUMS' "${tmp}/out.log" | head -1 | cut -d: -f1)"
  check "  prompt comes before signing" test "${prompt_line:-999}" -lt "${sign_line:-0}"
  new_release v1.2.0
  if tty_run v1.2.1 "${GOOD[@]}" v1.2.0; then fail "wrong answer is refused (should have failed)"; else pass "wrong answer is refused"; fi
  check "  wrong answer: not signed" not signed
  check "  wrong answer: nothing uploaded" not uploaded
else
  echo "skip: interactive prompt tests (no 'script' command)"
fi

# The table is printed with --yes too.
new_release v1.2.0
expect_ok "--yes still prints the asset table" "${GOOD[@]}" --yes v1.2.0
check "  table: zip digest" said "$(sha_of "${tmp}/state/files/RealmPortal-1.2.0-win-x64.zip")"
check "  table: uploader" said "github-actions[bot]"

# gh without release verify / verify-asset: refuse before doing anything.
new_release v1.2.0
extra_env=(STUB_OLD_GH=1)
expect_fail "gh without release verify is refused" "no 'gh release verify'" "${GOOD[@]}" --yes v1.2.0
check "  old gh: not signed" not signed
check "  old gh: nothing uploaded" not uploaded
check "  old gh: not published" not published
extra_env=()

# Substituted asset: the zip is replaced and SHA256SUMS regenerated so that both are consistent.
substitute_zip() { # <uploader>
  head -c 4096 /dev/urandom >"${tmp}/evil.zip"
  cp "${tmp}/evil.zip" "${tmp}/state/files/RealmPortal-1.2.0-win-x64.zip"
  (cd "${tmp}/state/files" && sha256sum realm-portal_1.2.0_amd64.deb RealmPortal-1.2.0-x86_64.AppImage \
    RealmPortal-1.2.0-win-x64.zip >SHA256SUMS)
  replace_asset RealmPortal-1.2.0-win-x64.zip "${tmp}/evil.zip" "$1"
  replace_asset SHA256SUMS "${tmp}/state/files/SHA256SUMS" "$1"
}
new_release v1.2.0
substitute_zip someone
expect_fail "substituted assets from another uploader are refused" "uploaded by 'someone'" "${GOOD[@]}" --yes v1.2.0
check "  substituted: not signed" not signed
check "  substituted: nothing uploaded" not uploaded
new_release v1.2.0
substitute_zip "github-actions[bot]"
# Uploaded with the workflow's own identity, the swap passes every automatic check; only the table shows it.
expect_ok "substituted assets via the workflow identity pass the automatic checks" "${GOOD[@]}" --dry-run v1.2.0
check "  substituted: the changed zip digest is in the table" said "$(sha_of "${tmp}/evil.zip")"

# A draft without digest fields: the script downloads and hashes the assets itself.
no_digest=1
new_release v1.2.0
extra_env=(STUB_NO_DIGEST=1)
expect_ok "draft without digests is hashed locally" "${GOOD[@]}" --yes v1.2.0
check "  no digest: said so" said "GitHub reports no digest"
check "  no digest: published" is_draft false
extra_env=()
no_digest=""

# Network or API failure when looking up the release.
new_release v1.2.0
extra_env=(STUB_FAIL_LIST=1)
expect_fail "lookup failure is not reported as a missing release" "could not list the releases .*network or API error" \
  "${GOOD[@]}" --yes v1.2.0
check "  lookup failure: not 'no release'" not said "no release for"
extra_env=()

# --repo must be owner/name.
for bad in "../.." "owner/.." "owner/.hidden" "a/b/c" "-x/y" "owner/" "owner/na me"; do
  expect_fail "--repo '${bad}' is refused" "--repo must be owner/name" "${GOOD[@]}" --repo "$bad" --yes v1.2.0
done

# Public key format.
new_release v1.2.0
{ echo "untrusted comment: release key, see the example in docs"; sed -n 2p "${tmp}/good.pub"; } >"${tmp}/example.pub"
expect_ok "comment containing 'example' is accepted" --pubkey "${tmp}/example.pub" --key "${tmp}/good.key" --dry-run v1.2.0
{ sed 's/$/\r/' "${tmp}/good.pub"; printf '\r\n\n'; } >"${tmp}/crlf.pub"
expect_ok "CRLF and trailing blank lines are accepted" --pubkey "${tmp}/crlf.pub" --key "${tmp}/good.key" --dry-run v1.2.0
{ echo "untrusted comment: minisign public key"; { printf 'Ed'; head -c 40 /dev/zero; } | base64 -w0; echo; } >"${tmp}/zero.pub"
expect_fail "all-zero key is a placeholder" "all-zero" --pubkey "${tmp}/zero.pub" --key "${tmp}/good.key" --dry-run v1.2.0
{ echo "untrusted comment: minisign public key"; { printf 'Xy'; head -c 40 /dev/urandom; } | base64 -w0; echo; } >"${tmp}/alg.pub"
expect_fail "key with another algorithm" "not a minisign Ed25519 public key" \
  --pubkey "${tmp}/alg.pub" --key "${tmp}/good.key" --dry-run v1.2.0
{ echo "untrusted comment: minisign public key"; { printf 'Ed'; head -c 41 /dev/urandom; } | base64 -w0; echo; } >"${tmp}/len.pub"
expect_fail "key of the wrong length" "not a minisign public key" --pubkey "${tmp}/len.pub" --key "${tmp}/good.key" --dry-run v1.2.0

# The committed release key passes the key checks (the run then stops at the stubbed lookup failure).
new_release v1.2.0
extra_env=(STUB_FAIL_LIST=1)
expect_fail "committed minisign.pub passes the key checks" "could not list the releases" \
  --key "${tmp}/good.key" --dry-run v1.2.0
check "  committed key: not refused as a key" not said "minisign.pub"
extra_env=()

# Refuse to run in GitHub Actions.
extra_env=(GITHUB_ACTIONS=true)
expect_fail "refuses to run in GitHub Actions" "refusing to run in GitHub Actions" "${GOOD[@]}" --yes v1.2.0
extra_env=()

# After publishing
new_release v1.2.0
extra_env=(STUB_IMMUTABLE_ON_PUBLISH=false)
expect_fail "mutable release is reverted to draft" "NOT immutable" "${GOOD[@]}" --yes v1.2.0
check "  mutable: back to draft" is_draft true
extra_env=()

new_release v1.2.0
extra_env=(STUB_VERIFY_RC=1)
expect_fail "missing attestation is reported" "cannot be reverted" "${GOOD[@]}" --yes v1.2.0
check "  missing attestation: not reported as published" not said "Published v1.2.0"
extra_env=()

new_release v1.2.0
extra_env=(STUB_SWAP_ON_PUBLISH=RealmPortal-1.2.0-win-x64.zip)
expect_fail "asset changed while publishing is caught" "published RealmPortal-1.2.0-win-x64.zip differs" \
  "${GOOD[@]}" --yes v1.2.0
check "  changed on publish: irreversible guidance" said "cannot be reverted"
check "  changed on publish: not reported as published" not said "Published v1.2.0"
extra_env=()

# --verify-published
new_release v1.2.0
expect_ok "publish for --verify-published" "${GOOD[@]}" --yes v1.2.0
: >"${tmp}/state/calls.log"
expect_ok "--verify-published passes on a good release (no secret key)" --pubkey "${tmp}/good.pub" --verify-published v1.2.0
check "  verify only: says Verified" said "Verified v1.2.0"
check "  verify only: nothing uploaded" not uploaded
check "  verify only: nothing edited" not grep -q '^gh release edit' "${tmp}/state/calls.log"

head -c 4096 /dev/urandom >"${tmp}/evil2.zip"
replace_asset RealmPortal-1.2.0-win-x64.zip "${tmp}/evil2.zip"
expect_fail "--verify-published catches a package that does not match SHA256SUMS" "does not match the uploaded asset" \
  --pubkey "${tmp}/good.pub" --verify-published v1.2.0
check "  verify only: irreversible guidance" said "cannot be reverted"

new_release v1.2.0
expect_fail "--verify-published on a draft" "still a draft" --pubkey "${tmp}/good.pub" --verify-published v1.2.0

new_release v1.2.0
expect_ok "publish for the other-key check" "${GOOD[@]}" --yes v1.2.0
expect_fail "--verify-published with another public key" "does not verify" --pubkey "${tmp}/other.pub" --verify-published v1.2.0

new_release v1.2.0
expect_ok "publish for the mutable check" "${GOOD[@]}" --yes v1.2.0
jq '.immutable = false' "${tmp}/state/release.json" >"${tmp}/r.json" && mv "${tmp}/r.json" "${tmp}/state/release.json"
expect_fail "--verify-published reports a mutable release without changing it" "NOT immutable.*--draft=true" \
  --pubkey "${tmp}/good.pub" --verify-published v1.2.0
check "  verify only: still published" is_draft false

# Publish applied on the server but the client got an error: the way on is --verify-published.
new_release v1.2.0
extra_env=(STUB_FAIL_EDIT_AFTER=1)
expect_fail "publish error points to --verify-published" "result is unknown.*--verify-published v1.2.0" "${GOOD[@]}" --yes v1.2.0
extra_env=()
expect_fail "rerun after an unknown publish result points to --verify-published" "--verify-published v1.2.0" \
  "${GOOD[@]}" --yes v1.2.0
expect_ok "--verify-published after an unknown publish result" --pubkey "${tmp}/good.pub" --verify-published v1.2.0

if [[ "$failures" -gt 0 ]]; then
  echo "${failures} sign-and-publish test(s) failed" >&2
  exit 1
fi
echo "All sign-and-publish tests passed"
