#!/usr/bin/env bash
# Sign a draft release's SHA256SUMS with minisign, upload the signature, and publish the release (#116).
#
# The release workflow stops at a draft. This script is the manual step that follows, run on the maintainer's machine:
#   1. check that the release is a draft with exactly the assets CI uploaded;
#   2. download SHA256SUMS and check it against the digests GitHub reports for the other assets;
#   3. sign it with `minisign -S` (minisign itself asks for the key password) or take a ready-made signature;
#   4. verify the signature against the committed public key, with the trusted comment "realm-portal <tag>";
#   5. upload SHA256SUMS.minisig to the draft, download it again and verify again;
#   6. after confirmation, publish the release and check that it is immutable, has the expected download URLs and a valid
#      GitHub release attestation.
#
# The script never reads the key password or the secret key; it only passes the key path to minisign. It uses no tokens
# of its own: `gh` must already be logged in with access to the repository.
#
# See docs/linux-packaging.md ("Signing and publishing a release").
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
usage: sign-and-publish.sh [options] <tag>

  <tag>                 release tag, e.g. v1.2.0 or v1.2.0-rc.1

options:
  --key <path>          minisign secret key to sign with (or set MINISIGN_KEY); not needed with --signature
  --signature <path>    use this ready-made SHA256SUMS.minisig instead of signing
  --pubkey <path>       public key to verify with (default: minisign.pub at the repository root)
  --repo <owner/name>   repository (default: KarloZ7715/realm-portal)
  --dry-run             do every check and sign locally, but do not upload or publish
  --yes                 do not ask before publishing
  -h, --help            show this help
USAGE
  exit 2
}

die() {
  echo "error: $*" >&2
  exit 1
}

say() {
  echo "==> $*"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

repo="KarloZ7715/realm-portal"
key="${MINISIGN_KEY:-}"
signature=""
pubkey="${ROOT_DIR}/minisign.pub"
dry_run=false
assume_yes=false
tag=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --key) [[ $# -ge 2 ]] || usage; key="$2"; shift 2 ;;
    --signature) [[ $# -ge 2 ]] || usage; signature="$2"; shift 2 ;;
    --pubkey) [[ $# -ge 2 ]] || usage; pubkey="$2"; shift 2 ;;
    --repo) [[ $# -ge 2 ]] || usage; repo="$2"; shift 2 ;;
    --dry-run) dry_run=true; shift ;;
    --yes) assume_yes=true; shift ;;
    -h | --help) usage ;;
    -*) echo "error: unknown option $1" >&2; usage ;;
    *) [[ -z "$tag" ]] || usage; tag="$1"; shift ;;
  esac
done
[[ -n "$tag" ]] || usage

# --- Inputs -------------------------------------------------------------------------------------------------------
[[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$ ]] \
  || die "tag must look like v1.2.0 or v1.2.0-rc.1 (got '${tag}')"
[[ "$repo" =~ ^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$ ]] || die "bad --repo '${repo}'"

command -v gh >/dev/null 2>&1 || die "gh (GitHub CLI) is not installed: https://cli.github.com/"
# Checked now because these run after publishing, when the release can no longer go back to draft.
for sub in verify verify-asset; do
  gh release "$sub" --help >/dev/null 2>&1 \
    || die "this gh has no 'gh release ${sub}'; upgrade the GitHub CLI: https://cli.github.com/"
done
command -v minisign >/dev/null 2>&1 || die "minisign is not installed: https://jedisct1.github.io/minisign/"
command -v sha256sum >/dev/null 2>&1 || die "sha256sum (coreutils) is not installed"

version="${tag#v}"
deb_version="$("${ROOT_DIR}/packaging/linux/semver-to-deb-version.sh" "$version")"
if [[ "$version" == *-* ]]; then is_prerelease=true; else is_prerelease=false; fi
trusted_comment="realm-portal ${tag}"
binaries=(
  "realm-portal_${deb_version}_amd64.deb"
  "RealmPortal-${version}-x86_64.AppImage"
  "RealmPortal-${version}-win-x64.zip"
)
sig_name="SHA256SUMS.minisig"

# The public key must be a real minisign public key: an untrusted comment line and one "RW..." key line.
[[ -f "$pubkey" ]] || die "public key ${pubkey} not found. Commit the release public key as minisign.pub (see docs/linux-packaging.md) or pass --pubkey."
mapfile -t pub_lines <"$pubkey"
if grep -qiE 'placeholder|replace[ _-]?me|example|test[ _-]?only|not a real key' <<<"${pub_lines[0]:-}"; then
  die "${pubkey} is a placeholder, not the release public key"
fi
if [[ ${#pub_lines[@]} -ne 2 || "${pub_lines[0]}" != "untrusted comment: "* || ! "${pub_lines[1]}" =~ ^RW[A-Za-z0-9+/]{54}$ ]]; then
  die "${pubkey} is not a minisign public key (expected an 'untrusted comment:' line and one 'RW...' key line)"
fi

if [[ -n "$signature" ]]; then
  [[ -f "$signature" ]] || die "signature file ${signature} not found"
else
  [[ -n "$key" ]] || die "no secret key: pass --key <path>, set MINISIGN_KEY, or pass --signature <file>"
  [[ -f "$key" ]] || die "secret key ${key} not found"
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- Helpers ------------------------------------------------------------------------------------------------------
# Prints "id<TAB>name<TAB>digest" for each asset of the release.
list_assets() {
  gh api "repos/${repo}/releases/${release_id}" \
    --jq '.assets[] | [(.id | tostring), .name, (.digest // "")] | @tsv'
}

# load_assets: fills asset_id[name] and asset_digest[name] (digest without the "sha256:" prefix).
declare -A asset_id=() asset_digest=()
load_assets() {
  asset_id=()
  asset_digest=()
  local id name digest
  while IFS=$'\t' read -r id name digest; do
    [[ -n "$name" ]] || continue
    [[ -z "${asset_id[$name]+x}" ]] || die "release has two assets named ${name}"
    [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] || die "GitHub reports no SHA-256 digest for ${name}"
    asset_id[$name]="$id"
    asset_digest[$name]="${digest#sha256:}"
  done < <(list_assets)
}

# expect_assets <name>...: the release has exactly these assets.
expect_assets() {
  local want have
  want="$(printf '%s\n' "$@" | sort)"
  have="$(printf '%s\n' "${!asset_id[@]}" | sort)"
  [[ "$have" == "$want" ]] || die "unexpected release assets. Have: $(echo "$have" | paste -sd' ') Want: $(echo "$want" | paste -sd' ')"
}

# download <asset name> <dest>: downloads by asset id (works for drafts) and checks GitHub's digest.
download() {
  local name="$1" dest="$2"
  gh api -H "Accept: application/octet-stream" "repos/${repo}/releases/assets/${asset_id[$name]}" >"$dest"
  local got
  got="$(sha256sum "$dest" | cut -d' ' -f1)"
  [[ "$got" == "${asset_digest[$name]}" ]] || die "downloaded ${name} does not match the digest GitHub reports"
}

# check_sums <file>: SHA256SUMS lists exactly the three packages, with the digests GitHub reports for them.
check_sums() {
  local file="$1" line digest name
  local -A seen=()
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^([0-9a-f]{64})\ \ ([^[:space:]]+)$ ]] || die "malformed SHA256SUMS line: ${line}"
    digest="${BASH_REMATCH[1]}"
    name="${BASH_REMATCH[2]}"
    [[ -z "${seen[$name]+x}" ]] || die "SHA256SUMS lists ${name} twice"
    seen[$name]=1
    [[ -n "${asset_digest[$name]+x}" ]] || die "SHA256SUMS lists ${name}, which is not a release asset"
    [[ "$digest" == "${asset_digest[$name]}" ]] || die "SHA256SUMS digest for ${name} does not match the uploaded asset"
  done <"$file"
  for name in "${binaries[@]}"; do
    [[ -n "${seen[$name]+x}" ]] || die "SHA256SUMS does not list ${name}"
  done
  [[ ${#seen[@]} -eq ${#binaries[@]} ]] || die "SHA256SUMS lists unexpected files"
}

# verify_sig <sums> <sig>: prehashed signature by the committed key, trusted comment exactly "realm-portal <tag>".
verify_sig() {
  local sums="$1" sig="$2" comment
  if ! comment="$(minisign -V -H -Q -p "$pubkey" -m "$sums" -x "$sig")"; then
    die "signature does not verify with ${pubkey}"
  fi
  [[ "$comment" == "$trusted_comment" ]] \
    || die "trusted comment is '${comment}', expected '${trusted_comment}'"
}

# --- 1. The release must be a draft -------------------------------------------------------------------------------
say "Looking up ${tag} in ${repo}"
mapfile -t ids < <(gh api --paginate "repos/${repo}/releases?per_page=100" --jq ".[] | select(.tag_name == \"${tag}\") | .id")
[[ ${#ids[@]} -gt 0 ]] || die "no release for ${tag}. Push the tag and wait for the release workflow to create the draft."
[[ ${#ids[@]} -eq 1 ]] || die "${#ids[@]} releases use tag ${tag}"
release_id="${ids[0]}"

IFS=$'\t' read -r is_draft draft_prerelease < <(gh api "repos/${repo}/releases/${release_id}" --jq '[.draft, .prerelease] | @tsv')
[[ "$is_draft" == "true" ]] || die "release ${tag} is not a draft (already published); nothing to do"
[[ "$draft_prerelease" == "$is_prerelease" ]] \
  || die "draft prerelease flag is ${draft_prerelease}, expected ${is_prerelease} for ${tag}"

load_assets
if [[ -n "${asset_id[$sig_name]+x}" ]]; then
  say "The draft already has ${sig_name} from an earlier run; it will be replaced"
  expect_assets "${binaries[@]}" SHA256SUMS "$sig_name"
else
  expect_assets "${binaries[@]}" SHA256SUMS
fi

# --- 2. SHA256SUMS matches the uploaded assets --------------------------------------------------------------------
say "Checking SHA256SUMS against the uploaded assets"
download SHA256SUMS "${tmp}/SHA256SUMS"
check_sums "${tmp}/SHA256SUMS"

# --- 3 and 4. Sign and verify -------------------------------------------------------------------------------------
if [[ -n "$signature" ]]; then
  say "Using the signature in ${signature}"
  cp -- "$signature" "${tmp}/${sig_name}"
else
  say "Signing SHA256SUMS with minisign (it asks for the key password)"
  minisign -S -s "$key" -m "${tmp}/SHA256SUMS" -x "${tmp}/${sig_name}" \
    -c "signature of SHA256SUMS for realm-portal ${tag}" -t "$trusted_comment"
fi
verify_sig "${tmp}/SHA256SUMS" "${tmp}/${sig_name}"
say "Signature verified with $(basename "$pubkey") (trusted comment: ${trusted_comment})"

enabled="$(gh api "repos/${repo}/immutable-releases" --jq '.enabled')"
[[ "$enabled" == "true" ]] || die "immutable releases are not enabled for ${repo}; enable them before publishing"

if [[ "$dry_run" == "true" ]]; then
  say "Dry run: all checks passed. Not uploading ${sig_name} and not publishing ${tag}."
  exit 0
fi

# --- 5. Upload and check what the draft now serves ----------------------------------------------------------------
say "Uploading ${sig_name} to the draft"
gh release upload "$tag" "${tmp}/${sig_name}" --repo "$repo" --clobber

load_assets
expect_assets "${binaries[@]}" SHA256SUMS "$sig_name"
mkdir "${tmp}/check"
download SHA256SUMS "${tmp}/check/SHA256SUMS"
download "$sig_name" "${tmp}/check/${sig_name}"
cmp -s "${tmp}/SHA256SUMS" "${tmp}/check/SHA256SUMS" || die "SHA256SUMS on the draft changed during signing"
cmp -s "${tmp}/${sig_name}" "${tmp}/check/${sig_name}" || die "uploaded ${sig_name} differs from the local one"
check_sums "${tmp}/check/SHA256SUMS"
verify_sig "${tmp}/check/SHA256SUMS" "${tmp}/check/${sig_name}"
say "Draft serves the signed SHA256SUMS"

# --- 6. Publish ---------------------------------------------------------------------------------------------------
if [[ "$assume_yes" != "true" ]]; then
  [[ -t 0 ]] || die "not a terminal; pass --yes to publish without asking"
  kind="stable (latest)"
  [[ "$is_prerelease" == "true" ]] && kind="prerelease (not latest)"
  echo "Publishing makes ${tag} public as a ${kind} release. It is immutable afterwards."
  read -r -p "Type the tag to publish it: " answer
  [[ "$answer" == "$tag" ]] || die "not confirmed; the release is still a draft"
fi

say "Publishing ${tag}"
if [[ "$is_prerelease" == "true" ]]; then
  gh release edit "$tag" --repo "$repo" --draft=false --prerelease --latest=false
else
  gh release edit "$tag" --repo "$repo" --draft=false --latest=true
fi

# --- Post-publish checks (these used to run in the release workflow) ----------------------------------------------
published=""
for _ in 1 2 3 4 5; do
  if published="$(gh api "repos/${repo}/releases/tags/${tag}" --jq '[.draft, .prerelease, .immutable] | @tsv' 2>/dev/null)"; then
    break
  fi
  published=""
  sleep "${SIGN_PUBLISH_RETRY_SECONDS:-3}"
done
[[ -n "$published" ]] || die "could not read the published release ${tag}; check it by hand"
IFS=$'\t' read -r p_draft p_prerelease p_immutable <<<"$published"

if [[ "$p_immutable" != "true" ]]; then
  # A release that is not immutable can still go back to draft, so do that rather than leave it public.
  echo "error: ${tag} was published but is NOT immutable. Reverting it to draft." >&2
  gh release edit "$tag" --repo "$repo" --draft=true || echo "error: reverting to draft failed; do it by hand" >&2
  exit 1
fi

# From here on the release is immutable: it cannot go back to draft and its assets cannot change. If a check fails,
# leave it published, tell users in the release notes, and cut a new patch release.
fail_published() {
  echo "error: $*" >&2
  echo "error: ${tag} is published and immutable, so it cannot be reverted. Edit its notes to warn users and cut a new patch release." >&2
  exit 1
}
[[ "$p_draft" == "false" ]] || fail_published "release still reports draft=${p_draft}"
[[ "$p_prerelease" == "$is_prerelease" ]] || fail_published "prerelease flag is ${p_prerelease}, expected ${is_prerelease}"

while IFS=$'\t' read -r name url; do
  [[ -n "$name" ]] || continue
  [[ "$url" == "https://github.com/${repo}/releases/download/${tag}/${name}" ]] \
    || fail_published "download URL for ${name} is ${url}"
done < <(gh api "repos/${repo}/releases/tags/${tag}" --jq '.assets[] | [.name, .browser_download_url] | @tsv')

# GitHub creates the release attestation when the release is published; give it a moment.
attested=false
for _ in 1 2 3 4 5; do
  if gh release verify "$tag" --repo "$repo" >/dev/null 2>&1; then
    attested=true
    break
  fi
  sleep "${SIGN_PUBLISH_RETRY_SECONDS:-5}"
done
[[ "$attested" == "true" ]] || fail_published "gh release verify failed (no valid release attestation)"
for name in SHA256SUMS "$sig_name"; do
  gh release verify-asset "$tag" "${tmp}/${name}" --repo "$repo" >/dev/null \
    || fail_published "gh release verify-asset failed for ${name}"
done

say "Published ${tag}: immutable, attested, SHA256SUMS signed (trusted comment: ${trusted_comment})"
