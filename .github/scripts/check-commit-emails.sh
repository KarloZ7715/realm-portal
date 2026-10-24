#!/usr/bin/env bash
# Check the author and committer emails of every commit in a git range.
#
# Usage: check-commit-emails.sh <base-sha> <head-sha>
#        check-commit-emails.sh --email <address>   (exit 0 if allowed, 1 if not)
#
# An email is allowed when, compared case-insensitively, it is either
#   - a GitHub noreply address matching the whole string
#     ^([0-9]+\+)?[A-Za-z0-9-]+(\[bot\])?@users\.noreply\.github\.com$
#     (e.g. 188834590+KarloZ7715@..., KarloZ7715@..., 49699333+dependabot[bot]@...), or
#   - exactly one of EXACT_ALLOWED_EMAILS: noreply@github.com (committer of commits
#     made on github.com and by GitHub apps) and cursoragent@cursor.com (Cursor agents).
# Author (%ae) or committer (%ce) outside that set fails the check.
# Co-authored-by: trailers are checked with the same rule but only produce warnings.
# Only the commit SHA and the email domain are printed, never the full address.
set -euo pipefail

NOREPLY_REGEX='^([0-9]+\+)?[a-z0-9-]+(\[bot\])?@users\.noreply\.github\.com$'
EXACT_ALLOWED_EMAILS="noreply@github.com cursoragent@cursor.com"

is_allowed_email() {
  local lower="${1,,}"
  [[ -n "$lower" ]] || return 1
  [[ "$lower" =~ $NOREPLY_REGEX ]] && return 0
  local exact
  for exact in $EXACT_ALLOWED_EMAILS; do
    [[ "$lower" == "$exact" ]] && return 0
  done
  return 1
}

domain_of() {
  local lower="${1,,}"
  if [[ "$lower" == *@* ]]; then
    printf '@%s' "${lower##*@}"
  else
    printf '(no domain)'
  fi
}

if [[ "${1:-}" == "--email" ]]; then
  [[ $# -eq 2 ]] || { echo "usage: $0 --email <address>" >&2; exit 2; }
  is_allowed_email "$2"
  exit
fi

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <base-sha> <head-sha>" >&2
  exit 2
fi
base="$1"
head="$2"

# Fail closed if either SHA is missing or malformed.
for sha in "$base" "$head"; do
  if ! [[ "$sha" =~ ^[0-9a-f]{40}$ ]]; then
    echo "::error::Unexpected base/head SHA: expected 40 lowercase hex characters."
    exit 1
  fi
done

# Captured first so that a git failure (e.g. a missing SHA) stops the script.
# Fields are separated by a unit separator (0x1f), which cannot appear in an email.
commits=$(git log --format='%H%x1f%ae%x1f%ce' "$base..$head")

failed=0
count=0
while IFS=$'\x1f' read -r sha author committer; do
  [[ -n "$sha" ]] || continue
  count=$((count + 1))
  if ! is_allowed_email "$author"; then
    echo "::error::Commit ${sha:0:12} has an author email at $(domain_of "$author"), which is not an allowed GitHub noreply address."
    failed=1
  fi
  if ! is_allowed_email "$committer"; then
    echo "::error::Commit ${sha:0:12} has a committer email at $(domain_of "$committer"), which is not an allowed GitHub noreply address."
    failed=1
  fi
  # Co-authors: same rule, warning only.
  coauthors=$(git log -1 --format='%(trailers:key=Co-authored-by,valueonly)' "$sha")
  while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    if [[ "$line" =~ \<([^\<\>]*)\> ]]; then
      coemail="${BASH_REMATCH[1]}"
    else
      coemail=""
    fi
    if ! is_allowed_email "$coemail"; then
      echo "::warning::Commit ${sha:0:12} has a Co-authored-by email at $(domain_of "$coemail"), which is not an allowed GitHub noreply address."
    fi
  done <<< "$coauthors"
done <<< "$commits"

if [[ "$failed" -ne 0 ]]; then
  cat <<'MSG'
Every commit in a pull request must have an author and a committer email that is a
GitHub noreply address: <id>+<username>@users.noreply.github.com (preferred, see
https://github.com/settings/emails) or <username>@users.noreply.github.com.
noreply@github.com (commits made on github.com) and cursoragent@cursor.com (Cursor
agents) are also accepted. To fix it on your PR branch:

  git config user.email "<id>+<username>@users.noreply.github.com"
  # last commit only:
  git commit --amend --no-edit --reset-author
  # several commits (replace <base> with the PR base branch, e.g. origin/main):
  git rebase -r <base> --exec 'git commit --amend --no-edit --reset-author'
  git push --force-with-lease

When merging or editing on github.com, pick your noreply address in the commit dialog.
MSG
  exit 1
fi
echo "All $count commit(s) in this range use allowed author and committer emails."
