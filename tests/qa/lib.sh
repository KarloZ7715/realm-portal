#!/usr/bin/env bash
# Shared helpers for the Realm Portal QA scripts. Source this file; do not run it.
# SAFETY: everything happens under a fresh mktemp -d directory. HOME is redirected into it for every CLI call,
# so even a default path (~/.config/wow-servidores, ~/.config/realm-portal) cannot point at real user data.

QA_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$QA_ROOT/../.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[ -x "$HOME/.dotnet/dotnet" ] && export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

CLI_DLL="$REPO_ROOT/src/RealmPortal.Cli/bin/Release/net9.0/realm-portal.dll"
PROBE_DLL="${QA_PROBE_BIN:-/tmp/qa-probe-bin}/applier-probe.dll"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/rp-qa.XXXXXX")"
case "$WORK" in /tmp/*|"${TMPDIR:-/tmp}"/*) ;; *) echo "refusing: bad work dir $WORK"; exit 99;; esac
FAKE_HOME="$WORK/home"; mkdir -p "$FAKE_HOME"
CONFIG_DIR="$WORK/config"; PROJ_DIR="$WORK/projection"; mkdir -p "$CONFIG_DIR" "$PROJ_DIR"
trap 'chmod -R u+rwX "$WORK" 2>/dev/null; [ -n "${QA_KEEP:-}" ] || rm -rf "$WORK"' EXIT

PASS=0; FAIL=0; RESULTS=()

build_all() {
  ( cd "$REPO_ROOT" && dotnet build src/RealmPortal.Cli -c Release -v q 2>&1 | tail -3 \
    && dotnet build tests/qa/ApplierProbe -c Release -o "${QA_PROBE_BIN:-/tmp/qa-probe-bin}" -v q 2>&1 | tail -3 )
}

# rp <args...>: runs the CLI with all config redirected into $WORK. Captures stdout in $OUT, stderr in $ERR, status in $RC.
rp() {
  OUT="$(HOME="$FAKE_HOME" REALM_PORTAL_CONFIG_DIR="$CONFIG_DIR" REALM_PORTAL_PROJECTION_DIR="$PROJ_DIR" \
         REALM_PORTAL_BACKUP_DIR="$WORK/backups" dotnet "$CLI_DLL" "$@" 2>"$WORK/stderr")"; RC=$?
  ERR="$(cat "$WORK/stderr")"
}

# probe <args...>: runs applier-probe (the real ClientFilesApplier) on synthetic dirs.
probe() {
  OUT="$(HOME="$FAKE_HOME" dotnet "$PROBE_DLL" "$@" 2>"$WORK/stderr")"; RC=$?
  ERR="$(cat "$WORK/stderr")"
}

# new_client <name> <version> [layout]: creates a synthetic client dir with a fake Wow.exe; prints the path.
new_client() {
  local dir="$WORK/clients/$1"; mkdir -p "$dir"
  python3 "$QA_ROOT/make_fake_wow_exe.py" "$dir/Wow.exe" "$2"
  echo "$dir"
}

sha() { sha256sum "$1" | cut -d' ' -f1; }
# tree_hash <dir>: one hash over relative paths + file contents (detects any added/removed/changed file).
tree_hash() { ( cd "$1" && find . -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum | cut -d' ' -f1 ); }

# check <ID> <description> <condition-exit-status 0=ok> [evidence]
check() {
  local id="$1" desc="$2" ok="$3" ev="${4:-}"
  if [ "$ok" -eq 0 ]; then PASS=$((PASS+1)); RESULTS+=("PASS | $id | $desc"); echo "PASS  $id  $desc"
  else FAIL=$((FAIL+1)); RESULTS+=("FAIL | $id | $desc"); echo "FAIL  $id  $desc"; [ -n "$ev" ] && echo "      evidence: $ev"; fi
}
# known <ID> <issue-ref> <desc> <ok-status> [evidence]: like check, but a failure is a KNOWN defect already tracked in <issue-ref>
# (counted separately, does not fail the run). It turns into a normal PASS as soon as the defect is fixed.
KNOWN=0
known() {
  local id="$1" ref="$2" desc="$3" ok="$4" ev="${5:-}"
  if [ "$ok" -eq 0 ]; then PASS=$((PASS+1)); RESULTS+=("PASS | $id | $desc (was known defect $ref: fixed?)"); echo "PASS  $id  $desc  [$ref now passes]"
  else KNOWN=$((KNOWN+1)); RESULTS+=("KNOWN-FAIL | $id | $desc | $ref"); echo "KNOWN $id  $desc  [$ref]"; [ -n "$ev" ] && echo "      evidence: $ev"; fi
}
expect_rc() { # <ID> <desc> <expected rc>
  [ "$RC" -eq "$3" ]; check "$1" "$2" $? "rc=$RC out=[$OUT] err=[$ERR]"
}
expect_out() { # <ID> <desc> <grep -E pattern> (searches stdout+stderr)
  printf '%s\n%s\n' "$OUT" "$ERR" | grep -Eq -- "$3"; check "$1" "$2" $? "pattern /$3/ not in out=[$OUT] err=[$ERR]"
}
summary() {
  echo; echo "== $PASS passed, $FAIL failed, $KNOWN known defects =="; printf '%s\n' "${RESULTS[@]}" > "${QA_RESULTS_FILE:-/dev/null}"
  [ "$FAIL" -eq 0 ]
}
