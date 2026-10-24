#!/usr/bin/env bash
# Tests for check-commit-emails.sh. Runs in CI before the real check; no network access needed.
set -euo pipefail

SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/check-commit-emails.sh"
fail=0

expect() { # expect <allowed|rejected> <email>
  local want="$1" email="$2" got
  if "$SCRIPT" --email "$email"; then got=allowed; else got=rejected; fi
  if [[ "$got" != "$want" ]]; then
    echo "FAIL: '$email' expected $want, got $got"
    fail=1
  fi
}

# Allowed
expect allowed '188834590+KarloZ7715@users.noreply.github.com'
expect allowed '188834590+karloz7715@USERS.NOREPLY.GITHUB.COM'
expect allowed 'KarloZ7715@users.noreply.github.com'
expect allowed 'openbot@users.noreply.github.com'
expect allowed '49699333+dependabot[bot]@users.noreply.github.com'
expect allowed '136622811+coderabbitai[bot]@users.noreply.github.com'
expect allowed 'noreply@github.com'
expect allowed 'NoReply@GitHub.com'
expect allowed 'cursoragent@cursor.com'

# Rejected: the whole address must match, not only its suffix
expect rejected 'me@gmail.com@users.noreply.github.com'
expect rejected 'me@gmail.com'
expect rejected 'someone@university.edu.co'
expect rejected 'a.b@users.noreply.github.com'
expect rejected '+KarloZ7715@users.noreply.github.com'
expect rejected '123+@users.noreply.github.com'
expect rejected 'x@users.noreply.github.com.evil.com'
expect rejected 'x@users-noreply-github-com'
expect rejected ' 1+x@users.noreply.github.com'
expect rejected '1+x@users.noreply.github.com '
expect rejected $'1+x@users.noreply.github.com\nme@gmail.com'
expect rejected 'bot[bot]x@users.noreply.github.com'
expect rejected 'xnoreply@github.com'
expect rejected 'noreply@github.com.evil.com'
expect rejected 'agent@cursor.com'
expect rejected 'cursoragent@cursor.com.evil.com'
expect rejected ''

# Range checks on a throwaway repository
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git -C "$tmp" init -q
commit() { # commit <author-email> <committer-email> [message]
  GIT_AUTHOR_NAME=a GIT_AUTHOR_EMAIL="$1" GIT_COMMITTER_NAME=c GIT_COMMITTER_EMAIL="$2" \
    git -C "$tmp" commit -q --allow-empty -m "${3:-test}"
  git -C "$tmp" rev-parse HEAD
}
run_range() { # run_range <want-exit> <base> <head> <label>
  local want="$1" got=0 out
  out="$(cd "$tmp" && "$SCRIPT" "$2" "$3" 2>&1)" || got=$?
  if [[ "$got" -ne "$want" ]]; then
    echo "FAIL: range '$4' expected exit $want, got $got"; echo "$out"; fail=1
  fi
  printf '%s' "$out"
}

ok='188834590+KarloZ7715@users.noreply.github.com'
base=$(commit "$ok" "$ok")
commit "$ok" 'noreply@github.com' >/dev/null
good2=$(commit 'cursoragent@cursor.com' 'cursoragent@cursor.com')
run_range 0 "$base" "$good2" 'allowed author/committer pairs' >/dev/null

bad_author=$(commit 'me@gmail.com@users.noreply.github.com' "$ok")
out=$(run_range 1 "$good2" "$bad_author" 'bad author')
[[ "$out" == *"author email at @users.noreply.github.com"* ]] || { echo "FAIL: missing author error"; fail=1; }
[[ "$out" != *"me@gmail.com"* ]] || { echo "FAIL: full address leaked in output"; fail=1; }

bad_committer=$(commit "$ok" 'someone@university.edu.co')
out=$(run_range 1 "$bad_author" "$bad_committer" 'bad committer')
[[ "$out" == *"committer email at @university.edu.co"* ]] || { echo "FAIL: missing committer error"; fail=1; }

co_bad=$(commit "$ok" "$ok" $'subject\n\nCo-authored-by: Someone <someone@gmail.com>\nCo-authored-by: Bot <1+bot[bot]@users.noreply.github.com>')
out=$(run_range 0 "$bad_committer" "$co_bad" 'bad co-author is only a warning')
[[ "$out" == *"::warning::"*"Co-authored-by email at @gmail.com"* ]] || { echo "FAIL: missing co-author warning"; fail=1; }
[[ "$(grep -c '::warning::' <<< "$out")" -eq 1 ]] || { echo "FAIL: the noreply co-author must not warn"; fail=1; }

run_range 1 "$base" "deadbeef" 'malformed SHA' >/dev/null

if [[ "$fail" -ne 0 ]]; then
  echo "check-commit-emails tests FAILED"
  exit 1
fi
echo "check-commit-emails tests passed"
