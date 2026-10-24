#!/usr/bin/env bash
# Check the author and committer emails of every commit in a git range.
#
# Usage: check-commit-emails.sh <base-sha> <head-sha>
#        check-commit-emails.sh --email <address>   (exit 0 if allowed, 1 if not)
#        check-commit-emails.sh --domain <address>  (print the reported domain; for tests)
#        check-commit-emails.sh --coauthor-email <trailer value>  (print the parsed email; for tests)
#        check-commit-emails.sh --reason <address>  (print why a rejected address fails; for tests)
#
# An email is allowed when, compared case-insensitively, it is either
#   - a GitHub noreply address matching the whole string
#     ^([0-9]+\+)?[a-z0-9]([a-z0-9]|-[a-z0-9]){0,38}(\[bot\])?@users\.noreply\.github\.com$
#     i.e. an optional numeric ID and a valid GitHub username (1-39 characters, letters,
#     digits and single inner hyphens; no leading, trailing or double hyphen; the length
#     is checked separately because the regex alone allows longer names), optionally
#     with the [bot] suffix (e.g. 188834590+KarloZ7715@..., KarloZ7715@...,
#     49699333+dependabot[bot]@...), or
#   - exactly one of EXACT_ALLOWED_EMAILS: noreply@github.com (committer of commits
#     made on github.com and by GitHub apps) and cursoragent@cursor.com (Cursor agents).
#     Addresses the regex cannot express (e.g. Enterprise Managed User accounts such as
#     <id>+<username>_<shortcode>@users.noreply.github.com, or legacy usernames that do
#     not follow the current rules) can be added there, as exact addresses.
# Author (%ae) or committer (%ce) outside that set fails the check.
# Co-authored-by: trailers are checked with the same rule but only produce warnings.
# Only the commit SHA and the email domain are printed, never the full address.
#
# Scope: this check protects privacy, it does NOT verify identity. Author, committer
# and trailer emails are free text that anyone can set to any allowed value, including
# someone else's noreply address or cursoragent@cursor.com. It only prevents personal
# or institutional addresses from being published by mistake. To prove who made a
# commit, sign commits (GPG, SSH or S/MIME) and enable GitHub vigilant mode.
set -euo pipefail

# Matched against the lowercased address, so it is case-insensitive.
NOREPLY_REGEX='^([0-9]+\+)?[a-z0-9]([a-z0-9]|-[a-z0-9]){0,38}(\[bot\])?@users\.noreply\.github\.com$'
EXACT_ALLOWED_EMAILS="noreply@github.com cursoragent@cursor.com"

is_allowed_email() {
  local lower="${1,,}"
  [[ -n "$lower" ]] || return 1
  if [[ "$lower" =~ $NOREPLY_REGEX ]]; then
    # Each "-x" repetition consumes two characters, so the regex alone allows longer
    # usernames; enforce GitHub's 39-character maximum on the username itself.
    local user="${lower%@users.noreply.github.com}"
    user="${user#*+}"
    user="${user%\[bot\]}"
    [[ "${#user}" -le 39 ]] && return 0
    return 1
  fi
  local exact
  for exact in $EXACT_ALLOWED_EMAILS; do
    # Entries are compared case-insensitively too.
    [[ "$lower" == "${exact,,}" ]] && return 0
  done
  return 1
}

# Prints why an address that is_allowed_email rejected fails (completes "which is ...").
# For a single-@ address on the GitHub noreply domain it names the part that is wrong:
# the optional "<id>+" prefix (digits only) or the username; anything else gets the
# generic reason.
rejection_reason() {
  local lower="${1,,}"
  local rest="${lower#*@}"
  if [[ "$lower" == *@* && "$rest" != *@* && "$rest" == "users.noreply.github.com" ]]; then
    local local_part="${lower%@*}"
    if [[ "$local_part" == *+* && ! "${local_part%%+*}" =~ ^[0-9]+$ ]]; then
      printf 'a noreply address with an invalid numeric ID prefix (only digits before the +)'
    else
      printf 'a noreply address with an invalid GitHub username (1-39 letters, digits or single inner hyphens)'
    fi
  else
    printf 'not an allowed GitHub noreply address'
  fi
}

# Prints "@domain" for reporting, "(no domain)" without an @, and "(malformed address)"
# when there is more than one @ (the domain would be ambiguous and could hide the real one).
domain_of() {
  local lower="${1,,}"
  local rest="${lower#*@}"
  if [[ "$lower" != *@* ]]; then
    printf '(no domain)'
  elif [[ "$rest" == *@* ]]; then
    printf '(malformed address)'
  else
    printf '@%s' "$rest"
  fi
}

# Extracts the email of a Co-authored-by value: the text inside <...> or, when there are
# no angle brackets, the only whitespace-separated token that contains an @. Prints
# nothing (malformed trailer) when no single candidate exists.
coauthor_email() {
  local value="$1" token candidate="" found=0
  local -a tokens=()
  if [[ "$value" == *[\<\>]* ]]; then
    # Exactly one <...> pair and no other address (@) outside it; anything else is malformed.
    # The bracketed text must itself contain an @.
    if [[ "$value" =~ ^[^\<\>@]*\<([^\<\>]*@[^\<\>]*)\>[^\<\>@]*$ ]]; then
      printf '%s' "${BASH_REMATCH[1]}"
    fi
    return 0
  fi
  # read -a splits on whitespace without glob expansion.
  read -r -a tokens <<< "$value" || true
  for token in "${tokens[@]}"; do
    if [[ "$token" == *@* ]]; then
      candidate="$token"
      found=$((found + 1))
    fi
  done
  [[ "$found" -eq 1 ]] && printf '%s' "$candidate"
  return 0
}

if [[ "${1:-}" == "--domain" ]]; then
  [[ $# -eq 2 ]] || { echo "usage: $0 --domain <address>" >&2; exit 2; }
  domain_of "$2"
  echo
  exit
fi

if [[ "${1:-}" == "--coauthor-email" ]]; then
  [[ $# -eq 2 ]] || { echo "usage: $0 --coauthor-email <trailer value>" >&2; exit 2; }
  coauthor_email "$2"
  echo
  exit
fi

if [[ "${1:-}" == "--reason" ]]; then
  [[ $# -eq 2 ]] || { echo "usage: $0 --reason <address>" >&2; exit 2; }
  rejection_reason "$2"
  echo
  exit
fi

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
    echo "::error::Commit ${sha:0:12} has an author email at $(domain_of "$author"), which is $(rejection_reason "$author")."
    failed=1
  fi
  if ! is_allowed_email "$committer"; then
    echo "::error::Commit ${sha:0:12} has a committer email at $(domain_of "$committer"), which is $(rejection_reason "$committer")."
    failed=1
  fi
  # Co-authors: same rule, warning only.
  coauthors=$(git log -1 --format='%(trailers:key=Co-authored-by,valueonly)' "$sha")
  while IFS= read -r line; do
    [[ -n "$line" ]] || continue
    coemail="$(coauthor_email "$line")"
    if [[ -z "$coemail" ]]; then
      echo "::warning::Commit ${sha:0:12} has a malformed Co-authored-by trailer (expected 'Name <email>')."
    elif ! is_allowed_email "$coemail"; then
      echo "::warning::Commit ${sha:0:12} has a Co-authored-by email at $(domain_of "$coemail"), which is $(rejection_reason "$coemail")."
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
