#!/usr/bin/env bash
# QA scenarios for the client-file engine (ClientFilesApplier) on SYNTHETIC clients, driven through tests/qa/ApplierProbe.
# The CLI does not call the applier yet (planned for F4), so this probe is the only way to exercise it outside unit tests.
# Usage: tests/qa/client_files_scenarios.sh   (QA_KEEP=1 keeps the temp dir)
set -u
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
build_all >/dev/null || { echo "build failed"; exit 99; }

RL_A='set realmlist logon-a.example.org\r\n'
RL_B='set realmlist logon-b.example.org\r\nset patchlist patch-b.example.org\r\n'
BK="$WORK/backups"

# wotlk_client <name>: 3.3.5a client with two locale folders, existing realmlist.wtf (CRLF) and Config.wtf (LF + BOM-less, extra lines).
wotlk_client() {
  local c; c="$(new_client "$1" 3.3.5.12340)"
  mkdir -p "$c/Data/enUS" "$c/Data/esES" "$c/WTF" "$c/Interface/AddOns"
  printf 'set realmlist old.example.org\r\n' > "$c/Data/enUS/realmlist.wtf"
  printf 'set realmlist old.example.org\nset patchlist old-patch.example.org\n' > "$c/Data/esES/realmlist.wtf"
  printf 'SET realmList "old.example.org"\nSET accountName "MYACCOUNT"\nSET locale "enUS"\nSET realmName "Old Realm"\n' > "$c/WTF/Config.wtf"
  echo "addon data" > "$c/Interface/AddOns/keep.txt"
  echo "$c"
}

echo "--- CF-01 byte-identical undo (3.3.5a, 2 locales, existing Config.wtf)"
C="$(wotlk_client c1)"; T0="$(tree_hash "$C")"; EXE0="$(sha "$C/Wow.exe")"
probe plan --client "$C" --backup "$BK" --realmlist "$RL_B" --realm "New Realm"; expect_rc CF-01a "plan succeeds" 0
[ "$(tree_hash "$C")" = "$T0" ] && [ ! -e "$BK" ]; check CF-01b "plan writes nothing (client untouched, no backup root)" $?
probe apply --client "$C" --backup "$BK" --realmlist "$RL_B" --realm "New Realm"; expect_rc CF-01c "apply succeeds" 0
grep -qx 'PLAN Replace Data/enUS/realmlist.wtf' <<<"$OUT" && grep -qx 'PLAN Replace Data/esES/realmlist.wtf' <<<"$OUT" \
  && grep -qx 'PLAN Replace WTF/Config.wtf' <<<"$OUT"; check CF-01d "apply edited both locale files + Config.wtf" $? "out=[$OUT]"
grep -q 'logon-b.example.org' "$C/Data/enUS/realmlist.wtf" && grep -q 'logon-b.example.org' "$C/Data/esES/realmlist.wtf"; check CF-01e "both realmlist.wtf now point at the new server" $?
grep -q 'SET accountName "MYACCOUNT"' "$C/WTF/Config.wtf" && grep -q 'SET locale "enUS"' "$C/WTF/Config.wtf"; check CF-01f "Config.wtf: accountName/locale lines preserved" $?
grep -q 'SET realmName "New Realm"' "$C/WTF/Config.wtf"; check CF-01g "Config.wtf: realmName updated" $?
[ "$(tree_hash "$C")" != "$T0" ]; check CF-01h "client really changed" $?
[ "$(sha "$C/Wow.exe")" = "$EXE0" ] && [ "$(cat "$C/Interface/AddOns/keep.txt")" = "addon data" ]; check CF-01i "unrelated files untouched" $?
probe undo --client "$C" --backup "$BK"; expect_rc CF-01j "undo succeeds" 0
[ "$(tree_hash "$C")" = "$T0" ]; check CF-01k "client tree byte-identical after undo (sha256 of every file)" $? "before=$T0 after=$(tree_hash "$C")"
probe undo --client "$C" --backup "$BK"; expect_out CF-01l "second undo -> NothingToUndo" 'NothingToUndo'

echo "--- CF-02 files that did not exist are removed by undo (Config.wtf missing, WTF/ missing)"
C="$(new_client c2 3.3.5.12340)"; mkdir -p "$C/Data/enUS"; T0="$(tree_hash "$C")"; D0="$(cd "$C" && find . | sort | sha256sum)"
probe apply --client "$C" --backup "$BK" --realmlist "$RL_A"; expect_rc CF-02a "apply on a client with only an empty Data/enUS" 0
[ -f "$C/WTF/Config.wtf" ] && [ -f "$C/Data/enUS/realmlist.wtf" ]; check CF-02b "created Config.wtf and realmlist.wtf" $?
probe undo --client "$C" --backup "$BK"; expect_rc CF-02c "undo succeeds" 0
[ "$(tree_hash "$C")" = "$T0" ]; check CF-02d "files identical to before (created files deleted)" $?
# Documented behaviour (client-files-backup.md: "folders are never removed"): an empty WTF/ stays. Reported as INFO, not a failure.
[ "$(cd "$C" && find . | sort | sha256sum)" = "$D0" ] && echo "INFO  CF-02e directory tree identical" || echo "INFO  CF-02e (documented) empty folders created by apply stay after undo: $(cd "$C" && find . -type d | sort | tr '\n' ' ')"

echo "--- CF-03 invalid realmlist: nothing written"
C="$(wotlk_client c3)"; T0="$(tree_hash "$C")"
for bad in 'set realmlist caf\xc3\xa9.example.org\r\n' 'set realmlist a b.example.org\r\n' 'SET REALMLIST a.example.org\r\n' 'set realmlist a.example.org\nset foo bar\n' ''; do
  probe apply --client "$C" --backup "$BK" --realmlist "$(printf "$bad")"
  [ "$RC" -ne 0 ] && [ "$(tree_hash "$C")" = "$T0" ]; ok=$?
  check CF-03 "rejected and client untouched: $(printf '%q' "$bad")" $ok "rc=$RC out=[$OUT] changed=$(cd "$C" && find . -type f | xargs ls -l | md5sum)"
done
# an LF-only realmlist without the final terminator is accepted by design (the applier normalises it); check it is written terminated
probe apply --client "$C" --backup "$BK" --install lf --realmlist 'set realmlist lf.example.org' >/dev/null; [ "$(tail -c2 "$C/Data/enUS/realmlist.wtf" | od -An -c | tr -d ' ')" = '\r\n' ] || [ "$(tail -c1 "$C/Data/enUS/realmlist.wtf" | od -An -c | tr -d ' ')" = '\n' ]; ok=$?
check CF-03s "last line without terminator is accepted and written terminated" $ok "tail=$(tail -c 20 "$C/Data/enUS/realmlist.wtf" | od -An -c)"
probe undo --client "$C" --backup "$BK" --install lf >/dev/null; T0="$(tree_hash "$C")"
probe apply --client "$C" --backup "$BK" --realmlist "$RL_A" --realm 'Bad"Realm'; [ "$RC" -ne 0 ] && [ "$(tree_hash "$C")" = "$T0" ]; check CF-03r "realm name with a quote rejected, client untouched" $? "rc=$RC out=[$OUT]"

echo "--- CF-04 game running"
C="$(wotlk_client c4)"; T0="$(tree_hash "$C")"
mkdir -p "$WORK/bin"; cp "$(command -v sleep)" "$WORK/bin/Wow"; "$WORK/bin/Wow" 120 & GAME=$!
sleep 0.5
probe apply --client "$C" --backup "$BK" --realmlist "$RL_A"; expect_out CF-04a "apply refused with GameRunning" 'GameRunning'
[ "$(tree_hash "$C")" = "$T0" ]; check CF-04b "client untouched while the game runs" $?
kill $GAME 2>/dev/null; wait $GAME 2>/dev/null; sleep 0.3
probe apply --client "$C" --backup "$BK" --realmlist "$RL_A"; expect_rc CF-04c "apply works once the game is closed" 0
cp "$(command -v sleep)" "$WORK/bin/Wow.exe"; "$WORK/bin/Wow.exe" 120 & GAME=$!; sleep 0.5
probe undo --client "$C" --backup "$BK"; expect_out CF-04d "undo refused with GameRunning too (process named Wow.exe, e.g. Wine)" 'GameRunning'
kill $GAME 2>/dev/null; wait $GAME 2>/dev/null; sleep 0.3
probe undo --client "$C" --backup "$BK"; expect_rc CF-04e "undo works after closing the game" 0
[ "$(tree_hash "$C")" = "$T0" ]; check CF-04f "byte-identical after the whole sequence" $?

echo "--- CF-05 backup conflict (file edited after apply)"
C="$(wotlk_client c5)"; probe apply --client "$C" --backup "$BK" --install conf --realmlist "$RL_A" >/dev/null
echo "# user edit" >> "$C/Data/enUS/realmlist.wtf"; EDITED="$(sha "$C/Data/enUS/realmlist.wtf")"
probe undo --client "$C" --backup "$BK" --install conf; expect_rc CF-05a "undo reports conflicts (exit 3)" 3
expect_out CF-05b "conflicting file reported" 'Conflict Data/enUS/realmlist.wtf'
[ "$(sha "$C/Data/enUS/realmlist.wtf")" = "$EDITED" ]; check CF-05c "conflicting file is NOT overwritten" $?
grep -q 'old.example.org' "$C/Data/esES/realmlist.wtf"; check CF-05d "non-conflicting files are restored" $?
probe undo --client "$C" --backup "$BK" --install conf; expect_rc CF-05e "second undo reports the same conflict (does not undo something older)" 3
# pre-existing / hostile backup storage
C="$(wotlk_client c5b)"; T0="$(tree_hash "$C")"; rm -rf "$WORK/bk2"; mkdir -p "$WORK/bk2/inst/99991231T235959999Z-000000"
probe apply --client "$C" --backup "$WORK/bk2" --install inst --realmlist "$RL_A"; expect_rc CF-05f "pre-existing far-future backup folder without manifest: apply still works" 0
probe undo --client "$C" --backup "$WORK/bk2" --install inst; [ "$(tree_hash "$C")" = "$T0" ]; check CF-05g "...and undo restores byte-identical" $? "rc=$RC out=[$OUT]"
C="$(wotlk_client c5c)"; T0="$(tree_hash "$C")"; echo "i am a file" > "$WORK/bk-is-file"
probe apply --client "$C" --backup "$WORK/bk-is-file" --realmlist "$RL_A"; [ "$RC" -ne 0 ] && [ "$(tree_hash "$C")" = "$T0" ]; check CF-05h "backup root is a file: apply fails, client untouched" $? "rc=$RC out=[$OUT]"
C="$(wotlk_client c5d)"; T0="$(tree_hash "$C")"; mkdir -p "$WORK/bk-ro"; chmod a-w "$WORK/bk-ro"
probe apply --client "$C" --backup "$WORK/bk-ro" --realmlist "$RL_A"; [ "$RC" -ne 0 ] && [ "$(tree_hash "$C")" = "$T0" ]; check CF-05i "backup root read-only: apply fails, client untouched" $? "rc=$RC out=[$OUT]"
expect_out CF-05j "...with a PermissionDenied code" 'PermissionDenied'; chmod u+w "$WORK/bk-ro"
# tampered backup
C="$(wotlk_client c5e)"; probe apply --client "$C" --backup "$BK" --install tamp --realmlist "$RL_A" >/dev/null
BF="$(find "$BK/tamp" -path '*files/0' | head -1)"; echo "tampered" > "$BF"
probe undo --client "$C" --backup "$BK" --install tamp; [ "$RC" -ne 0 ]; check CF-05k "tampered backup copy is refused, not restored" $? "rc=$RC out=[$OUT]"
grep -q tampered "$C/Data/enUS/realmlist.wtf"; [ $? -ne 0 ]; check CF-05l "tampered bytes never written into the client" $?

echo "--- CF-06 corrupt / odd config files"
C="$(wotlk_client c6)"; head -c 4096 /dev/urandom > "$C/WTF/Config.wtf"; T0="$(tree_hash "$C")"
probe apply --client "$C" --backup "$BK" --install rnd --realmlist "$RL_A"; check CF-06a "random-binary Config.wtf: apply succeeds or fails cleanly (rc<=2)" $([ "$RC" -le 2 ]; echo $?) "rc=$RC out=[$OUT]"
probe undo --client "$C" --backup "$BK" --install rnd; [ "$(tree_hash "$C")" = "$T0" ]; check CF-06b "...and undo restores the random bytes exactly" $? "rc=$RC out=[$OUT]"
C="$(wotlk_client c6b)"; : > "$C/WTF/Config.wtf"; : > "$C/Data/enUS/realmlist.wtf"; T0="$(tree_hash "$C")"
probe apply --client "$C" --backup "$BK" --install empt --realmlist "$RL_A"; expect_rc CF-06c "empty Config.wtf / realmlist.wtf: apply ok" 0
probe undo --client "$C" --backup "$BK" --install empt; [ "$(tree_hash "$C")" = "$T0" ]; check CF-06d "...undo restores the empty files" $?
C="$(wotlk_client c6c)"; printf 'SET realmList "a"\nSET realmList "b"\nSET realmName "x"\nSET realmName "y"' > "$C/WTF/Config.wtf"; T0="$(tree_hash "$C")"
probe apply --client "$C" --backup "$BK" --install dup --realmlist "$RL_A" --realm "R"; expect_rc CF-06e "duplicate keys + no trailing newline: apply ok" 0
[ "$(grep -ci '^SET realmList' "$C/WTF/Config.wtf")" = 1 ]; check CF-06f "duplicates collapsed to one realmList line" $? "$(cat "$C/WTF/Config.wtf")"
probe undo --client "$C" --backup "$BK" --install dup; [ "$(tree_hash "$C")" = "$T0" ]; check CF-06g "undo restores original duplicates byte-for-byte" $?
C="$(wotlk_client c6d)"; printf '\xef\xbb\xbfset realmlist old.example.org\r\n' > "$C/Data/enUS/realmlist.wtf"
probe apply --client "$C" --backup "$BK" --install bom --realmlist "$RL_A" >/dev/null; [ "$(head -c3 "$C/Data/enUS/realmlist.wtf" | od -An -tx1 | tr -d ' \n')" = efbbbf ]; check CF-06h "existing BOM in realmlist.wtf is preserved (documented)" $?
C="$(wotlk_client c6e)"; mkdir -p "$WORK/bk3/mf"; probe apply --client "$C" --backup "$WORK/bk3" --install mf --realmlist "$RL_A" >/dev/null
MF="$(find "$WORK/bk3/mf" -name manifest.json | head -1)"; head -c 40 "$MF" > "$MF.t"; mv "$MF.t" "$MF"
probe undo --client "$C" --backup "$WORK/bk3" --install mf; [ "$RC" -ne 0 ] && ! echo "$OUT$ERR" | grep -q '   at '; check CF-06i "truncated manifest.json: undo fails cleanly (no crash/stack)" $? "rc=$RC out=[$OUT] err=[$ERR]"
C="$(wotlk_client c6f)"; probe apply --client "$C" --backup "$BK" --install same --realmlist "$RL_A" >/dev/null
probe apply --client "$C" --backup "$BK" --install same --realmlist "$RL_A"; expect_out CF-06j "re-applying the same server: empty plan, no new backup" 'APPLIED backup=none edits=0'

echo "--- CF-07 permissions"
C="$(wotlk_client c7)"; T0="$(tree_hash "$C")"; chmod a-w "$C/Data/esES"
probe apply --client "$C" --backup "$BK" --install p1 --realmlist "$RL_A"; chmod u+w "$C/Data/esES"
[ "$RC" -ne 0 ] && [ "$(tree_hash "$C")" = "$T0" ]; check CF-07a "one locale folder read-only: apply fails and EVERYTHING is reverted" $? "rc=$RC out=[$OUT]"
expect_out CF-07b "error code is PermissionDenied" 'PermissionDenied'
C="$(wotlk_client c7b)"; chmod 444 "$C/Data/enUS/realmlist.wtf"; M0="$(stat -c %a "$C/Data/enUS/realmlist.wtf")"
probe apply --client "$C" --backup "$BK" --install p2 --realmlist "$RL_A"; RC_APPLY=$RC
M1="$(stat -c %a "$C/Data/enUS/realmlist.wtf")"
echo "apply rc=$RC; realmlist.wtf mode before=$M0 after=$M1; out=[$OUT]"
if [ "$RC_APPLY" -eq 0 ]; then
  [ "$M1" = "$M0" ]; check CF-07c "read-only (0444) realmlist.wtf: mode preserved after apply (or apply refused)" $? "mode $M0 -> $M1; apply replaced a read-only file silently"
  probe undo --client "$C" --backup "$BK" --install p2; [ "$(stat -c %a "$C/Data/enUS/realmlist.wtf")" = "$M0" ]; check CF-07d "mode preserved after undo" $? "mode now $(stat -c %a "$C/Data/enUS/realmlist.wtf"), was $M0"
else
  check CF-07c "read-only realmlist.wtf: apply refused with a code" 0
fi
C="$(wotlk_client c7c)"; chmod a-rx "$C/Data"; probe plan --client "$C" --backup "$BK" --realmlist "$RL_A"; chmod u+rwx "$C/Data"
[ "$RC" -ne 0 ] && ! echo "$OUT$ERR" | grep -q '   at '; check CF-07e "unreadable Data/ folder: plan fails cleanly" $? "rc=$RC out=[$OUT] err=[$ERR]"
C="$(wotlk_client c7d)"; chmod a-w "$C/WTF"; T0="$(tree_hash "$C")"; probe apply --client "$C" --backup "$BK" --install p3 --realmlist "$RL_A"; chmod u+w "$C/WTF"
[ "$RC" -ne 0 ] && [ "$(tree_hash "$C")" = "$T0" ]; check CF-07f "WTF/ read-only: apply fails, all reverted" $? "rc=$RC out=[$OUT]"
C="$(wotlk_client c7e)"; ln -s "$WORK/outside-realmlist.wtf" "$C/Data/enUS/realmlist.wtf.link" 2>/dev/null; rm -f "$C/Data/enUS/realmlist.wtf.link"
echo 'set realmlist shared.example.org' > "$WORK/outside-realmlist.wtf"; OUTSIDE="$(sha "$WORK/outside-realmlist.wtf")"
rm "$C/Data/enUS/realmlist.wtf"; ln -s "$WORK/outside-realmlist.wtf" "$C/Data/enUS/realmlist.wtf"
probe apply --client "$C" --backup "$BK" --install sym --realmlist "$RL_A"
[ "$(sha "$WORK/outside-realmlist.wtf")" = "$OUTSIDE" ]; check CF-07g "symlinked realmlist.wtf: file outside the client is never written through" $? "rc=$RC out=[$OUT]"
probe undo --client "$C" --backup "$BK" --install sym
[ -L "$C/Data/enUS/realmlist.wtf" ]; check CF-07h "symlinked realmlist.wtf: undo restores the symlink (not just a regular copy)" $? "after undo: $(ls -l "$C/Data/enUS/realmlist.wtf" | cut -c1-60)"

echo "--- CF-08 other client versions / layouts"
C="$(new_client c8a 1.12.1.5875)"; T0="$(tree_hash "$C")"; probe apply --client "$C" --backup "$BK" --install v1 --realmlist "$RL_A"; expect_rc CF-08a "1.12.1: apply creates root realmlist.wtf" 0
[ -f "$C/realmlist.wtf" ] && [ ! -e "$C/WTF/Config.wtf" ]; check CF-08b "1.12.1: realmlist.wtf in root, Config.wtf not created" $?
probe undo --client "$C" --backup "$BK" --install v1; [ "$(tree_hash "$C")" = "$T0" ]; check CF-08c "1.12.1: undo byte-identical" $?
C="$(new_client c8b 5.4.8.18414)"; T0="$(tree_hash "$C")"; probe apply --client "$C" --backup "$BK" --install v5 --realmlist "$RL_A"; expect_rc CF-08d "5.4.8: apply creates WTF/Config.wtf" 0
grep -q 'SET realmList "logon-a.example.org"' "$C/WTF/Config.wtf" && [ ! -e "$C/realmlist.wtf" ]; check CF-08e "5.4.8: only Config.wtf written" $?
probe undo --client "$C" --backup "$BK" --install v5; [ "$(tree_hash "$C")" = "$T0" ]; check CF-08f "5.4.8: undo byte-identical" $?
C="$(new_client c8c 9.0.1.36500)"; T0="$(tree_hash "$C")"; probe apply --client "$C" --backup "$BK" --install v9 --realmlist "$RL_A"
expect_out CF-08g "9.x client -> UnsupportedClient, nothing written" 'UnsupportedClient'; [ "$(tree_hash "$C")" = "$T0" ]; check CF-08h "9.x: client untouched" $?
C="$(new_client c8d 3.3.5.12340)"; T0="$(tree_hash "$C")"; probe apply --client "$C" --backup "$BK" --install nl --realmlist "$RL_A"
check CF-08i "3.3.5a with NO locale folder: no crash (rc<=2); never invents Data/<locale>" $([ "$RC" -le 2 ] && [ ! -d "$C/Data" ]; echo $?) "rc=$RC out=[$OUT] tree=$(cd "$C" && find . | tr '\n' ' ')"
C="$(wotlk_client c8e)"; mv "$C/Data" "$C/DATA"; mv "$C/DATA/enUS" "$C/DATA/enus_x"; mkdir -p "$C/DATA/enUS"; mv "$C/DATA/enus_x/realmlist.wtf" "$C/DATA/enUS/REALMLIST.WTF"; rmdir "$C/DATA/enus_x"
probe apply --client "$C" --backup "$BK" --install cs --realmlist "$RL_A"; [ -f "$C/DATA/enUS/REALMLIST.WTF" ] && [ ! -e "$C/Data" ] && grep -q logon-a "$C/DATA/enUS/REALMLIST.WTF"; check CF-08j "case-sensitive FS: DATA/enUS/REALMLIST.WTF is found and edited, no duplicate Data/ created" $? "rc=$RC out=[$OUT] tree=$(cd "$C" && find . -name '*ealmlist*' -o -iname data | tr '\n' ' ')"

echo "--- CF-09 file changed between plan and apply"
C="$(wotlk_client c9)"; summary_hash="$(tree_hash "$C")"
# probe plans and applies in one process, so this is covered by the unit test Apply_FailsWithHashMismatch_WhenAFileChangedSincePlanning.
echo "(covered by unit test ClientFilesApplierTests.Apply_FailsWithHashMismatch_WhenAFileChangedSincePlanning)"

summary
