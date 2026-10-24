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
expect allowed '335604065+openbotgit[bot]@users.noreply.github.com'
expect allowed 'openbotgit@users.noreply.github.com'
expect allowed '31301654+imgbot[bot]@users.noreply.github.com'
expect allowed 'a-b-c@users.noreply.github.com'
expect allowed 'a@users.noreply.github.com'
expect allowed "$(printf 'a%.0s' {1..39})@users.noreply.github.com"            # 39 chars: GitHub maximum
expect allowed "1+$(printf 'a%.0s' {1..39})[bot]@users.noreply.github.com"
expect allowed "1+a$(printf -- '-b%.0s' {1..19})@users.noreply.github.com"         # 39 chars with hyphens
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

# Rejected: not a valid GitHub username (hyphen rules, 1-39 characters)
expect rejected 'karloz-@users.noreply.github.com'
expect rejected '1+karloz-@users.noreply.github.com'
expect rejected 'a--b@users.noreply.github.com'
expect rejected '-ab@users.noreply.github.com'
expect rejected '1+-ab@users.noreply.github.com'
expect rejected 'bot-[bot]@users.noreply.github.com'
expect rejected "$(printf 'a%.0s' {1..40})@users.noreply.github.com"           # 40 chars
expect rejected "1+$(printf 'a%.0s' {1..45})@users.noreply.github.com"
expect rejected "a$(printf -- '-b%.0s' {1..20})@users.noreply.github.com"            # 41 chars with hyphens
expect rejected "1+a$(printf -- '-b%.0s' {1..20})[bot]@users.noreply.github.com"

# Reported domain (never the full address)
expect_out() { # expect_out <want> <flag> <input>
  local got
  got="$("$SCRIPT" "$2" "$3")"
  if [[ "$got" != "$1" ]]; then
    echo "FAIL: $2 '$3' expected '$1', got '$got'"
    fail=1
  fi
}
expect_out '@gmail.com' --domain 'Me@Gmail.com'
expect_out '(no domain)' --domain 'nobody'
expect_out '(no domain)' --domain ''
expect_out '(malformed address)' --domain 'me@gmail.com@users.noreply.github.com'
expect_out '(malformed address)' --domain 'a@b@c'

# Reason reported for a rejected address
invalid_user='a noreply address with an invalid GitHub username (1-39 letters, digits or single inner hyphens)'
not_allowed='not an allowed GitHub noreply address'
expect_out "$invalid_user" --reason 'karloz-@users.noreply.github.com'
expect_out "$invalid_user" --reason '1+KarloZ-@Users.Noreply.GitHub.com'
expect_out "$invalid_user" --reason "$(printf 'a%.0s' {1..40})@users.noreply.github.com"
expect_out "$invalid_user" --reason '123+@users.noreply.github.com'
expect_out "$invalid_user" --reason 'a.b@users.noreply.github.com'
expect_out "$not_allowed" --reason 'me@gmail.com'
expect_out "$not_allowed" --reason 'me@gmail.com@users.noreply.github.com'
expect_out "$not_allowed" --reason 'x@users.noreply.github.com.evil.com'
expect_out "$not_allowed" --reason 'nobody'
expect_out "$not_allowed" --reason ''

# Co-authored-by value parsing
expect_out 'me@gmail.com' --coauthor-email 'Me <me@gmail.com>'
expect_out 'me@gmail.com' --coauthor-email 'me@gmail.com'
expect_out 'me@gmail.com' --coauthor-email 'Some Name me@gmail.com'
expect_out '' --coauthor-email 'Some Name'
expect_out '' --coauthor-email 'a@x.com b@y.com'
expect_out '' --coauthor-email 'Me <me@gmail.com'
expect_out '' --coauthor-email '*'
expect_out '' --coauthor-email 'Me <me@gmail.com> <other@x.com>'
expect_out '' --coauthor-email 'Me <me@gmail.com> trailing>'
expect_out '' --coauthor-email 'Me <<me@gmail.com>'
expect_out '' --coauthor-email 'Me <1+me@users.noreply.github.com> me@gmail.com'
expect_out '' --coauthor-email 'me@gmail.com <1+me@users.noreply.github.com>'
expect_out '' --coauthor-email 'Me <not-an-address>'
expect_out '' --coauthor-email 'Me <>'

# Range checks on a throwaway repository
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git -C "$tmp" init -q
commit() { # commit <author-email> <committer-email> [message]
  GIT_AUTHOR_NAME=a GIT_AUTHOR_EMAIL="$1" GIT_COMMITTER_NAME=c GIT_COMMITTER_EMAIL="$2" \
    git -C "$tmp" commit -q --no-gpg-sign --allow-empty -m "${3:-test}"
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
[[ "$out" == *"author email at (malformed address)"* ]] || { echo "FAIL: missing author error"; fail=1; }
[[ "$out" != *"me@gmail.com"* ]] || { echo "FAIL: full address leaked in output"; fail=1; }

bad_committer=$(commit "$ok" 'someone@university.edu.co')
out=$(run_range 1 "$bad_author" "$bad_committer" 'bad committer')
[[ "$out" == *"committer email at @university.edu.co"* ]] || { echo "FAIL: missing committer error"; fail=1; }

co_bad=$(commit "$ok" "$ok" $'subject\n\nCo-authored-by: Someone <someone@gmail.com>\nCo-authored-by: Bot <1+bot[bot]@users.noreply.github.com>')
out=$(run_range 0 "$bad_committer" "$co_bad" 'bad co-author is only a warning')
[[ "$out" == *"::warning::"*"Co-authored-by email at @gmail.com"* ]] || { echo "FAIL: missing co-author warning"; fail=1; }
[[ "$(grep -c '::warning::' <<< "$out")" -eq 1 ]] || { echo "FAIL: the noreply co-author must not warn"; fail=1; }

co_odd=$(commit "$ok" "$ok" $'subject\n\nCo-authored-by: someone@university.edu.co\nCo-authored-by: Just A Name\nCo-authored-by: X <a@b.com@users.noreply.github.com>')
out=$(run_range 0 "$co_bad" "$co_odd" 'co-author without brackets or malformed')
[[ "$out" == *"Co-authored-by email at @university.edu.co"* ]] || { echo "FAIL: bare co-author email not parsed"; fail=1; }
[[ "$out" == *"malformed Co-authored-by trailer"* ]] || { echo "FAIL: missing malformed trailer warning"; fail=1; }
[[ "$out" == *"Co-authored-by email at (malformed address)"* ]] || { echo "FAIL: missing malformed address warning"; fail=1; }
[[ "$out" != *"someone@"* && "$out" != *"a@b.com"* ]] || { echo "FAIL: full address leaked in output"; fail=1; }

bad_user=$(commit 'karloz-@users.noreply.github.com' "$(printf 'a%.0s' {1..40})@users.noreply.github.com")
out=$(run_range 1 "$co_odd" "$bad_user" 'noreply domain with an invalid username')
[[ "$out" == *"author email at @users.noreply.github.com, which is $invalid_user."* ]] || { echo "FAIL: missing invalid-username author error"; fail=1; }
[[ "$out" == *"committer email at @users.noreply.github.com, which is $invalid_user."* ]] || { echo "FAIL: missing invalid-username committer error"; fail=1; }
[[ "$out" != *"$not_allowed"* ]] || { echo "FAIL: invalid username reported as not allowed"; fail=1; }
[[ "$out" != *"karloz-@"* ]] || { echo "FAIL: full address leaked in output"; fail=1; }

run_range 1 "$base" "deadbeef" 'malformed SHA' >/dev/null

if [[ "$fail" -ne 0 ]]; then
  echo "check-commit-emails tests FAILED"
  exit 1
fi
echo "check-commit-emails tests passed"
