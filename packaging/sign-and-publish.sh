#!/usr/bin/env bash
# Sign a draft release's SHA256SUMS with minisign, upload the signature, and publish the release (#116).
#
# The release workflow stops at a draft. This script is the manual step that follows, run on the maintainer's machine:
#   1. check that the release is a draft whose assets were all uploaded by the release workflow (github-actions[bot]);
#   2. print each asset's name, size, SHA-256 and uploader, to compare with the workflow run's job summary;
#   3. check SHA256SUMS against those digests;
#   4. ask for confirmation (type the tag), then sign SHA256SUMS with `minisign -S` (minisign asks for the key
#      password) or take a ready-made signature, and verify it against the committed public key with the trusted
#      comment "realm-portal <tag>";
#   5. upload SHA256SUMS.minisig, download it again and verify again, and check that no other asset changed;
#   6. publish, then check the published release: immutable, expected assets and URLs, unchanged digests, the
#      signature, the packages against SHA256SUMS, and GitHub's release attestation.
#
# With --verify-published it runs only the checks of step 6 on an already published release.
#
# The script never reads the key password or the secret key; it only passes the key path to minisign. It uses no tokens
# of its own: `gh` must already be logged in with access to the repository.
#
# What the checks cover and what they don't is described in docs/linux-packaging.md ("Signing and publishing a release").
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
usage: sign-and-publish.sh [options] <tag>
       sign-and-publish.sh --verify-published [options] <tag>

  <tag>                 release tag, e.g. v1.2.0 or v1.2.0-rc.1

options:
  --key <path>          minisign secret key to sign with (or set MINISIGN_KEY); not needed with --signature
  --signature <path>    use this ready-made SHA256SUMS.minisig instead of signing
  --pubkey <path>       public key to verify with (default: minisign.pub at the repository root)
  --repo <owner/name>   repository (default: KarloZ7715/realm-portal)
  --dry-run             do every check and sign locally, but do not upload or publish
  --yes                 do not ask before signing (the asset table is still printed)
  --verify-published    only check an already published release
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
SELF="packaging/sign-and-publish.sh"
CI_UPLOADER="github-actions[bot]"
RETRY_SECONDS="${SIGN_PUBLISH_RETRY_SECONDS:-5}"

repo="KarloZ7715/realm-portal"
key="${MINISIGN_KEY:-}"
signature=""
pubkey="${ROOT_DIR}/minisign.pub"
dry_run=false
assume_yes=false
verify_only=false
tag=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --key) [[ $# -ge 2 ]] || usage; key="$2"; shift 2 ;;
    --signature) [[ $# -ge 2 ]] || usage; signature="$2"; shift 2 ;;
    --pubkey) [[ $# -ge 2 ]] || usage; pubkey="$2"; shift 2 ;;
    --repo) [[ $# -ge 2 ]] || usage; repo="$2"; shift 2 ;;
    --dry-run) dry_run=true; shift ;;
    --yes) assume_yes=true; shift ;;
    --verify-published) verify_only=true; shift ;;
    -h | --help) usage ;;
    -*) echo "error: unknown option $1" >&2; usage ;;
    *) [[ -z "$tag" ]] || usage; tag="$1"; shift ;;
  esac
done
[[ -n "$tag" ]] || usage

# --- Environment and inputs ---------------------------------------------------------------------------------------
[[ "${GITHUB_ACTIONS:-}" != "true" ]] || die "refusing to run in GitHub Actions; signing is a manual step on the maintainer's machine"

[[ "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$ ]] \
  || die "tag must look like v1.2.0 or v1.2.0-rc.1 (got '${tag}')"
# owner: GitHub user or organization name; name: letters, digits, '.', '_', '-', not starting with '.'.
[[ "$repo" =~ ^[A-Za-z0-9]([A-Za-z0-9]|-[A-Za-z0-9]){0,38}/[A-Za-z0-9_-][A-Za-z0-9._-]{0,99}$ ]] \
  || die "--repo must be owner/name (got '${repo}')"

for tool in gh minisign sha256sum cmp base64 od tr sort paste stat; do
  command -v "$tool" >/dev/null 2>&1 || die "${tool} is not installed (needs: gh, minisign and coreutils)"
done
# `gh release <unknown> --help` exits 0 and prints the general help, so look for the commands in that help instead.
# They are needed after publishing, when the release can no longer go back to draft.
release_help="$(gh release --help 2>&1 || true)"
for sub in verify verify-asset; do
  grep -qE "^[[:space:]]+${sub}:" <<<"$release_help" \
    || die "this gh has no 'gh release ${sub}'; upgrade the GitHub CLI: https://cli.github.com/"
done

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
ci_assets=("${binaries[@]}" SHA256SUMS)

# Public key rule: an "untrusted comment:" line and one key line, ignoring CR and trailing blank lines. The key line must
# be base64 of 42 bytes starting with "Ed", with a key ID and key that are not all zeros. A comment containing
# "placeholder" is refused.
[[ -f "$pubkey" ]] || die "public key ${pubkey} not found. Commit the release public key as minisign.pub (see docs/linux-packaging.md) or pass --pubkey."
pub_lines=()
while IFS= read -r line || [[ -n "$line" ]]; do
  pub_lines+=("${line%$'\r'}")
done <"$pubkey"
while [[ ${#pub_lines[@]} -gt 0 && -z "${pub_lines[-1]}" ]]; do
  unset 'pub_lines[-1]'
done
if [[ ${#pub_lines[@]} -ne 2 || "${pub_lines[0]}" != "untrusted comment: "* || ! "${pub_lines[1]}" =~ ^[A-Za-z0-9+/]{56}$ ]]; then
  die "${pubkey} is not a minisign public key (expected an 'untrusted comment:' line and one key line)"
fi
grep -qi 'placeholder' <<<"${pub_lines[0]}" && die "${pubkey} is a placeholder, not the release public key"
pub_hex="$(base64 -d <<<"${pub_lines[1]}" 2>/dev/null | od -An -v -tx1 | tr -d ' \n')" || pub_hex=""
[[ ${#pub_hex} -eq 84 && "${pub_hex:0:4}" == "4564" ]] \
  || die "${pubkey} is not a minisign Ed25519 public key"
[[ "${pub_hex:4:16}" != "0000000000000000" && "${pub_hex:20}" != "$(printf '0%.0s' {1..64})" ]] \
  || die "${pubkey} has an all-zero key ID or key; it is a placeholder"

if [[ "$verify_only" != "true" ]]; then
  if [[ -n "$signature" ]]; then
    [[ -f "$signature" ]] || die "signature file ${signature} not found"
  else
    [[ -n "$key" ]] || die "no secret key: pass --key <path>, set MINISIGN_KEY, or pass --signature <file>"
    [[ -f "$key" ]] || die "secret key ${key} not found"
  fi
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- Helpers ------------------------------------------------------------------------------------------------------
# retry <command...>: runs the command up to 5 times, waiting RETRY_SECONDS, then twice that, and so on, between tries.
retry() {
  local attempt
  for attempt in 1 2 3 4 5; do
    if "$@"; then
      return 0
    fi
    if [[ "$attempt" -lt 5 ]]; then
      sleep "$((RETRY_SECONDS * attempt))"
    fi
  done
  return 1
}

# load_assets <api path> [error message]: fills asset_* maps from a release object. A missing digest is left empty (see
# fill_digests).
declare -A asset_id=() asset_size=() asset_digest=() asset_uploader=() asset_url=()
load_assets() {
  asset_id=()
  asset_size=()
  asset_digest=()
  asset_uploader=()
  asset_url=()
  local listing id name size digest uploader url
  local error="${2:-could not read the assets of ${tag} (network or API error); nothing was changed, run the script again}"
  listing="$(retry gh api "$1" \
    --jq '.assets[] | [(.id | tostring), .name, (.size | tostring), (.digest // "-"), (.uploader.login // "-"), (.browser_download_url // "-")] | @tsv')" \
    || die "$error"
  while IFS=$'\t' read -r id name size digest uploader url; do
    [[ -n "$name" ]] || continue
    [[ -z "${asset_id[$name]+x}" ]] || die "release has two assets named ${name}"
    asset_id[$name]="$id"
    asset_size[$name]="$size"
    asset_uploader[$name]="$uploader"
    asset_url[$name]="$url"
    if [[ "$digest" == "-" ]]; then
      asset_digest[$name]=""
    else
      [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] || die "GitHub reports an unexpected digest for ${name}: ${digest}"
      asset_digest[$name]="${digest#sha256:}"
    fi
  done <<<"$listing"
}

# expect_assets <name>...: the release has exactly these assets.
expect_assets() {
  local want have
  want="$(printf '%s\n' "$@" | sort)"
  have="$(printf '%s\n' "${!asset_id[@]}" | sort)"
  [[ "$have" == "$want" ]] || die "unexpected release assets. Have: $(echo "$have" | paste -sd' ') Want: $(echo "$want" | paste -sd' ')"
}

# download <name> <dest>: downloads by asset ID (works for drafts), with retries. Fails only on network or API errors.
download_once() {
  gh api -H "Accept: application/octet-stream" "repos/${repo}/releases/assets/${asset_id[$1]}" >"$2"
}
download() {
  retry download_once "$1" "$2"
}

# check_download <name> <file>: the file has the digest (when GitHub reports one) and size GitHub reports.
check_download() {
  local name="$1" dest="$2" got
  got="$(sha256sum "$dest" | cut -d' ' -f1)"
  if [[ -n "${asset_digest[$name]}" ]]; then
    [[ "$got" == "${asset_digest[$name]}" ]] || die "downloaded ${name} does not match the digest GitHub reports"
  fi
  [[ "$(stat -c %s "$dest")" == "${asset_size[$name]}" ]] || die "downloaded ${name} does not have the size GitHub reports"
}

# fetch <name> <dest>: download and check_download.
fetch() {
  download "$1" "$2" || die "could not download $1 (network or API error)"
  check_download "$1" "$2"
}

# fill_digests <dir> <name>...: download and hash every listed asset that has no digest from GitHub (drafts may lack it).
fill_digests() {
  local dir="$1" name
  shift
  for name in "$@"; do
    if [[ -z "${asset_digest[$name]}" ]]; then
      say "GitHub reports no digest for ${name}; downloading it to hash it"
      fetch "$name" "${dir}/${name}"
      asset_digest[$name]="$(sha256sum "${dir}/${name}" | cut -d' ' -f1)"
    fi
  done
}

check_uploaders() {
  local name
  for name in "$@"; do
    [[ "${asset_uploader[$name]}" == "$CI_UPLOADER" ]] \
      || die "${name} was uploaded by '${asset_uploader[$name]}', not by the release workflow (${CI_UPLOADER}); refusing to sign"
  done
}

print_table() {
  local name
  printf '\n  %-44s %12s  %-64s  %s\n' "Asset" "Size" "SHA-256" "Uploaded by"
  for name in "$@"; do
    printf '  %-44s %12s  %-64s  %s\n' "$name" "${asset_size[$name]}" "${asset_digest[$name]}" "${asset_uploader[$name]}"
  done
  echo
}

# check_sums <file>: SHA256SUMS lists exactly the three packages, with the digests known for them.
check_sums() {
  local file="$1" line digest name
  local -A seen=()
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ "$line" =~ ^([0-9a-f]{64})\ \ ([^[:space:]]+)$ ]] || die "malformed SHA256SUMS line: ${line}"
    digest="${BASH_REMATCH[1]}"
    name="${BASH_REMATCH[2]}"
    [[ -z "${seen[$name]+x}" ]] || die "SHA256SUMS lists ${name} twice"
    seen[$name]=1
    [[ " ${binaries[*]} " == *" ${name} "* ]] || die "SHA256SUMS lists ${name}, which is not a release package"
    [[ "$digest" == "${asset_digest[$name]}" ]] || die "SHA256SUMS digest for ${name} does not match the uploaded asset"
  done <"$file"
  for name in "${binaries[@]}"; do
    [[ -n "${seen[$name]+x}" ]] || die "SHA256SUMS does not list ${name}"
  done
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

# --- Post-publish checks ------------------------------------------------------------------------------------------
# verify_published: checks a published release. Uses the pre-publish digests in expected_digest when they are set.
declare -A expected_digest=()
irreversible() {
  echo "error: $*" >&2
  echo "error: ${tag} is published and immutable, so it cannot be reverted. Do not reuse the tag. Edit the release notes to warn users and cut a new patch release." >&2
  echo "error: if this may be a network problem, check again first with: ${SELF} --verify-published ${tag}" >&2
  exit 1
}
verify_published() {
  local fields="" retry_hint="${tag} may already be published; check it again with: ${SELF} --verify-published ${tag}"
  fields="$(retry gh api "repos/${repo}/releases/tags/${tag}" --jq '[.draft, .prerelease, .immutable] | @tsv' 2>/dev/null)" \
    || fields=""
  [[ -n "$fields" ]] || die "could not read the published release ${tag} (network or API error); run '${SELF} --verify-published ${tag}' later"
  local p_draft p_prerelease p_immutable
  IFS=$'\t' read -r p_draft p_prerelease p_immutable <<<"$fields"
  [[ "$p_draft" == "false" ]] || die "${tag} is still a draft; it was not published"

  if [[ "$p_immutable" != "true" ]]; then
    if [[ "$verify_only" == "true" ]]; then
      die "${tag} is published but NOT immutable. Put it back to draft now with: gh release edit ${tag} --repo ${repo} --draft=true"
    fi
    # A release that is not immutable can still go back to draft, so do that rather than leave it public.
    echo "error: ${tag} was published but is NOT immutable. Reverting it to draft." >&2
    gh release edit "$tag" --repo "$repo" --draft=true \
      || echo "error: reverting to draft failed; run: gh release edit ${tag} --repo ${repo} --draft=true" >&2
    exit 1
  fi
  # From here on the release cannot go back to draft and its assets cannot change.
  [[ "$p_prerelease" == "$is_prerelease" ]] || irreversible "prerelease flag is ${p_prerelease}, expected ${is_prerelease}"

  load_assets "repos/${repo}/releases/tags/${tag}" \
    "could not read the assets of ${tag} (network or API error). ${retry_hint}"
  local want have name
  want="$(printf '%s\n' "${ci_assets[@]}" "$sig_name" | sort)"
  have="$(printf '%s\n' "${!asset_id[@]}" | sort)"
  [[ "$have" == "$want" ]] || irreversible "published assets are $(echo "$have" | paste -sd' ')"
  for name in "${ci_assets[@]}" "$sig_name"; do
    [[ "${asset_url[$name]}" == "https://github.com/${repo}/releases/download/${tag}/${name}" ]] \
      || irreversible "download URL for ${name} is ${asset_url[$name]}"
  done
  for name in "${ci_assets[@]}"; do
    [[ "${asset_uploader[$name]}" == "$CI_UPLOADER" ]] || irreversible "${name} was uploaded by '${asset_uploader[$name]}'"
  done

  # Download all five assets from the published release and hash them ourselves.
  mkdir -p "${tmp}/published"
  for name in "${ci_assets[@]}" "$sig_name"; do
    download "$name" "${tmp}/published/${name}" \
      || die "could not download published ${name} (network or API error). ${retry_hint}"
    ( check_download "$name" "${tmp}/published/${name}" ) || irreversible "published ${name} does not match what GitHub reports"
    asset_digest[$name]="$(sha256sum "${tmp}/published/${name}" | cut -d' ' -f1)"
    if [[ -n "${expected_digest[$name]:-}" && "${asset_digest[$name]}" != "${expected_digest[$name]}" ]]; then
      irreversible "published ${name} differs from what was checked before publishing"
    fi
  done
  ( verify_sig "${tmp}/published/SHA256SUMS" "${tmp}/published/${sig_name}" ) \
    || irreversible "the published signature does not verify"
  ( check_sums "${tmp}/published/SHA256SUMS" ) || irreversible "published packages do not match the signed SHA256SUMS"

  retry gh release verify "$tag" --repo "$repo" >/dev/null 2>&1 \
    || irreversible "gh release verify failed (no valid release attestation)"
  for name in "${ci_assets[@]}" "$sig_name"; do
    retry gh release verify-asset "$tag" "${tmp}/published/${name}" --repo "$repo" >/dev/null 2>&1 \
      || irreversible "gh release verify-asset failed for ${name}"
  done
  print_table "${ci_assets[@]}" "$sig_name"
}

# --- 1. Find the release ------------------------------------------------------------------------------------------
say "Looking up ${tag} in ${repo}"
ids_text="$(gh api --paginate "repos/${repo}/releases?per_page=100" --jq ".[] | select(.tag_name == \"${tag}\") | .id")" \
  || die "could not list the releases of ${repo} (network or API error); nothing was changed, run the script again"
mapfile -t ids < <(grep -v '^$' <<<"$ids_text" || true)
[[ ${#ids[@]} -gt 0 ]] || die "no release for ${tag}. Push the tag and wait for the release workflow to create the draft."
[[ ${#ids[@]} -eq 1 ]] || die "${#ids[@]} releases use tag ${tag}"
release_id="${ids[0]}"

meta="$(gh api "repos/${repo}/releases/${release_id}" --jq '[.draft, .prerelease] | @tsv')" \
  || die "could not read release ${tag} (network or API error); nothing was changed, run the script again"
IFS=$'\t' read -r is_draft draft_prerelease <<<"$meta"

if [[ "$verify_only" == "true" ]]; then
  [[ "$is_draft" == "false" ]] || die "${tag} is still a draft; sign and publish it with ${SELF} first"
  say "Checking the published release ${tag}"
  verify_published
  say "Verified ${tag}: immutable, attested, assets unchanged, SHA256SUMS signed (trusted comment: ${trusted_comment})"
  exit 0
fi

[[ "$is_draft" == "true" ]] \
  || die "${tag} is already published. To check it, run: ${SELF} --verify-published ${tag}"
[[ "$draft_prerelease" == "$is_prerelease" ]] \
  || die "draft prerelease flag is ${draft_prerelease}, expected ${is_prerelease} for ${tag}"

# --- 2. Assets: who uploaded them, and their digests --------------------------------------------------------------
load_assets "repos/${repo}/releases/${release_id}"
if [[ -n "${asset_id[$sig_name]+x}" ]]; then
  say "The draft already has ${sig_name} from an earlier run; it will be replaced"
  expect_assets "${ci_assets[@]}" "$sig_name"
else
  expect_assets "${ci_assets[@]}"
fi
mkdir -p "${tmp}/draft"
fill_digests "${tmp}/draft" "${ci_assets[@]}"
say "Draft assets (compare with the job summary of the release workflow run for ${tag}):"
print_table "${ci_assets[@]}"
check_uploaders "${ci_assets[@]}"
declare -A draft_digest=()
for name in "${ci_assets[@]}"; do
  draft_digest[$name]="${asset_digest[$name]}"
done

# --- 3. SHA256SUMS matches the uploaded assets --------------------------------------------------------------------
say "Checking SHA256SUMS against the uploaded assets"
fetch SHA256SUMS "${tmp}/SHA256SUMS"
check_sums "${tmp}/SHA256SUMS"

enabled="$(gh api "repos/${repo}/immutable-releases" --jq '.enabled')" \
  || die "could not read the immutable releases setting (network or API error)"
[[ "$enabled" == "true" ]] || die "immutable releases are not enabled for ${repo}; enable them before publishing"

# --- 4. Confirm, sign and verify ----------------------------------------------------------------------------------
if [[ "$assume_yes" != "true" && "$dry_run" != "true" ]]; then
  [[ -t 0 ]] || die "not a terminal; pass --yes to sign and publish without asking"
  kind="a stable (latest)"
  [[ "$is_prerelease" == "true" ]] && kind="a prerelease (not latest)"
  echo "If the SHA-256 values above match the workflow's job summary, ${tag} will be signed and published as ${kind}"
  echo "release. It is immutable afterwards."
  read -r -p "Type the tag to sign and publish it: " answer \
    || die "not confirmed (no answer); nothing was signed or uploaded and the release is still a draft"
  [[ "$answer" == "$tag" ]] || die "not confirmed; nothing was signed or uploaded and the release is still a draft"
fi

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

if [[ "$dry_run" == "true" ]]; then
  say "Dry run: all checks passed. Not uploading ${sig_name} and not publishing ${tag}."
  exit 0
fi

# --- 5. Upload and check what the draft now serves ----------------------------------------------------------------
say "Uploading ${sig_name} to the draft"
gh release upload "$tag" "${tmp}/${sig_name}" --repo "$repo" --clobber \
  || die "upload failed; the release is still a draft, run the script again"

load_assets "repos/${repo}/releases/${release_id}"
expect_assets "${ci_assets[@]}" "$sig_name"
check_uploaders "${ci_assets[@]}"
mkdir -p "${tmp}/check"
# The bytes that were signed must still be what the draft serves; the digest comparison below checks the same thing
# through GitHub's digests.
fetch SHA256SUMS "${tmp}/check/SHA256SUMS"
cmp -s "${tmp}/SHA256SUMS" "${tmp}/check/SHA256SUMS" || die "SHA256SUMS on the draft changed during signing"
fetch "$sig_name" "${tmp}/check/${sig_name}"
cmp -s "${tmp}/${sig_name}" "${tmp}/check/${sig_name}" || die "uploaded ${sig_name} differs from the local one"
fill_digests "${tmp}/check" "${ci_assets[@]}" "$sig_name"
for name in "${ci_assets[@]}"; do
  [[ "${asset_digest[$name]}" == "${draft_digest[$name]}" ]] || die "${name} changed on the draft while signing"
done
verify_sig "${tmp}/check/SHA256SUMS" "${tmp}/check/${sig_name}"
for name in "${ci_assets[@]}" "$sig_name"; do
  expected_digest[$name]="${asset_digest[$name]}"
done
say "Draft serves the signed SHA256SUMS"

# --- 6. Publish and check -----------------------------------------------------------------------------------------
say "Publishing ${tag}"
if [[ "$is_prerelease" == "true" ]]; then
  publish_flags=(--draft=false --prerelease --latest=false)
else
  publish_flags=(--draft=false --latest=true)
fi
gh release edit "$tag" --repo "$repo" "${publish_flags[@]}" \
  || die "publishing failed or its result is unknown. If ${tag} is now public, check it with: ${SELF} --verify-published ${tag}"

verify_published
say "Published ${tag}: immutable, attested, assets unchanged, SHA256SUMS signed (trusted comment: ${trusted_comment})"
