#!/usr/bin/env bash
# QA scenarios that run through the realm-portal CLI on synthetic clients (temp dirs only).
# Usage: tests/qa/cli_scenarios.sh        (set QA_KEEP=1 to keep the temp dir for inspection)
set -u
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"
build_all >/dev/null || { echo "build failed"; exit 99; }

C12340="$(new_client wotlk 3.3.5.12340)"
C8606="$(new_client tbc 2.4.3.8606)"

echo "--- QA-02/03: client folder + compatible servers"
rp client-version "$C12340";                                 expect_out QA-02a "client-version reads 12340 from synthetic Wow.exe" '12340'
rp client-version "$WORK/does-not-exist";                    expect_rc  QA-02b "missing client folder -> exit 2, no crash" 2
mkdir -p "$WORK/clients/empty"; rp client-version "$WORK/clients/empty"; expect_rc QA-02c "folder without Wow.exe -> exit 2" 2
mkdir -p "$WORK/clients/junk"; echo "not a pe" > "$WORK/clients/junk/Wow.exe"; rp client-version "$WORK/clients/junk"; expect_rc QA-02d "Wow.exe that is not a PE -> exit 2" 2
mkdir -p "$WORK/clients/lower"; python3 "$QA_ROOT/make_fake_wow_exe.py" "$WORK/clients/lower/wow.exe" 3.3.5.12340
rp client-version "$WORK/clients/lower";                     expect_rc  QA-02e "lowercase wow.exe is found (case-sensitive FS)" 0
rp list --client-dir "$C12340";                              expect_out QA-03a "empty store: 'No servers.'" 'No servers'
rp add Wotlk wotlk.example.org --build 12340;                expect_rc  QA-03b "add server requiring 12340" 0
rp add Tbc tbc.example.org --build 8606;                     expect_rc  QA-03c "add server requiring 8606" 0
rp add Anyver any.example.org;                               expect_rc  QA-03d "add server with no required build" 0
rp list --client-dir "$C12340"
[ "$(echo "$OUT" | grep -c 'Wotlk')" = 1 ] && ! echo "$OUT" | grep -q 'Tbc' && echo "$OUT" | grep -q 'Anyver'
check QA-03e "3.3.5a client lists Wotlk + Anyver, hides Tbc" $? "out=[$OUT]"
rp list --client-dir "$C8606"
echo "$OUT" | grep -q 'Tbc' && ! echo "$OUT" | grep -q 'Wotlk'; check QA-03f "2.4.3 client lists Tbc, hides Wotlk" $? "out=[$OUT]"
rp list --client-dir "$C12340" --all;                        expect_out QA-03g "--all shows incompatible ones, tagged" 'Tbc.*\[incompatible\]'
rp switch Tbc --client-dir "$C12340";                        expect_rc  QA-03h "switching to an incompatible server is refused (exit 2)" 2
[ ! -e "$PROJ_DIR/activo.json" ]; check QA-03i "refused switch wrote no activo.json" $?
rp switch Tbc --client-dir "$C12340" --force;                expect_rc  QA-03j "--force overrides the refusal" 0
rp switch Wotlk --client-dir "$C12340"; rp undo; rp switch Tbc --client-dir "$C12340" --force >/dev/null 2>&1

echo "--- QA-04/06: one-click switch + persistence across processes"
rp remove Tbc >/dev/null; rm -rf "$CONFIG_DIR/state.json" "$PROJ_DIR/activo.json"
rp switch Wotlk --client-dir "$C12340";                      expect_out QA-04a "switch prints 'Switched to'" "Switched to 'Wotlk'"
[ -f "$PROJ_DIR/activo.json" ]; check QA-04b "activo.json created" $?
rp status --client-dir "$C12340";                            expect_out QA-06a "new process: status still shows Wotlk active" "Active server: Wotlk"
expect_out QA-06b "new process: activo.json in sync" "activo.json: in sync"
expect_out QA-06c "client compatible" "compatible"
rp switch wotlk;                                             expect_out QA-04c "name is case-insensitive" "Switched to 'Wotlk'"
rp list; echo "$OUT" | grep -Eq '^\* .*Wotlk'; check QA-06d "list marks active server with *" $? "out=[$OUT]"

echo "--- QA-05: open the game (dry run + wrapper, never the real game)"
rp launch --client-dir "$C12340" --dry-run;                  expect_out QA-05a "dry-run names Wow.exe + server" "Dry run: would launch .*Wow.exe.*Wotlk"
cat > "$WORK/wrapper.sh" <<WEOF
#!/bin/sh
echo "\$@" > "$WORK/wrapper-args.txt"
WEOF
chmod +x "$WORK/wrapper.sh"
rp launch --client-dir "$C12340" --wrapper "$WORK/wrapper.sh"; expect_rc QA-05b "launch through a recording wrapper exits 0" 0
sleep 1; grep -q "Wow.exe" "$WORK/wrapper-args.txt" 2>/dev/null; check QA-05c "wrapper received the Wow.exe path" $? "$(cat "$WORK/wrapper-args.txt" 2>&1)"
rp launch --client-dir "$WORK/clients/empty" --dry-run;      expect_rc QA-05d "launch with no Wow.exe -> exit 2" 2
rp launch --dry-run;                                         expect_rc QA-05e "launch without --client-dir -> usage error exit 1" 1
rp launch Tbc --client-dir "$C12340" --dry-run;              expect_rc QA-05f "launch of unknown server -> exit 2" 2

echo "--- QA-07a: undo of activo.json is byte-identical"
rp switch Wotlk >/dev/null
rp add Second second.example.org --build 12340 --realm "Realm Two" --line "set patchlist patch.example.org" >/dev/null
H1="$(sha "$PROJ_DIR/activo.json")"
rp switch Second;                                            expect_rc QA-07a1 "switch to second server" 0
H2="$(sha "$PROJ_DIR/activo.json")"; [ "$H1" != "$H2" ]; check QA-07a2 "activo.json changed after switch" $?
rp undo;                                                     expect_out QA-07a3 "undo restores Wotlk" "restored 'Wotlk'"
[ "$(sha "$PROJ_DIR/activo.json")" = "$H1" ]; check QA-07a4 "activo.json byte-identical to before switch (sha256)" $?
rp undo;                                                     expect_rc QA-07a5 "second undo fails cleanly (one level only) -> exit 2" 2
rm -f "$CONFIG_DIR/state.json" "$PROJ_DIR/activo.json"
rp switch Wotlk >/dev/null; rp undo >/dev/null
[ ! -e "$PROJ_DIR/activo.json" ]; check QA-07a6 "undo of first-ever switch removes activo.json" $?

echo "--- QA-08: add by hand, invalid realmlist"
rp add Bad "bad host.example.org";                           expect_rc QA-08a "host with a space -> rejected (exit 2)" 2
rp add Bad "caf$(printf '\xc3\xa9').example.org";            expect_rc QA-08b "non-ASCII host -> rejected" 2
rp add Bad 'x"y.example.org';                                expect_rc QA-08c "host with a quote -> rejected" 2
rp add Bad "a.example.org" --line "set realmlist b.example.org"; expect_rc QA-08d "duplicate realmlist line -> rejected" 2
rp add Bad "a.example.org" --line "set portal b.example.org";    expect_rc QA-08e "portal key -> rejected (unsupported)" 2
rp add Bad "a.example.org" --line "SET patchlist p.example.org"; expect_rc QA-08f "uppercase SET line -> rejected" 2
rp add Bad "a.example.org" --line "rm -rf /";                expect_rc QA-08g "arbitrary line -> rejected" 2
rp add Bad "a.example.org" --line "set patchlist ";          expect_rc QA-08h "empty patchlist value -> rejected" 2
rp add Bad "";                                               expect_rc QA-08i "empty host -> rejected" 2
rp add "" a.example.org;                                     expect_rc QA-08j "empty name -> rejected" 2
rp add Wotlk dup.example.org;                                expect_rc QA-08k "duplicate name (case-insens.) -> rejected" 2
rp add WOTLK dup.example.org;                                expect_rc QA-08l "duplicate name different case -> rejected" 2
rp add Bad a.example.org --build abc;                        expect_rc QA-08m "--build abc -> usage error (exit 1)" 1
rp add Bad a.example.org --build 0; [ "$RC" -eq 1 ]; known QA-08n "PR #33 (open)" "--build 0 -> usage error (exit 1)" $? "rc=$RC err=[$ERR]"
rp add Bad a.example.org --build -5;                         expect_rc QA-08o "--build -5 -> usage error (exit 1)" 1
rp add Ip 10.0.0.5:3724;                                     expect_rc QA-08p "host:port accepted" 0
rp list --all; ! echo "$OUT" | grep -q "Bad"; check QA-08q "no rejected server leaked into the store" $? "out=[$OUT]"
rp add LongName "$(printf 'a%.0s' $(seq 1 100)).example.org"
[ "$RC" -le 2 ]; check QA-08r "long host does not crash (rc<=2)" $?

echo "--- QA-12: activo.json format"
rp switch Second >/dev/null
F="$PROJ_DIR/activo.json"
[ "$(head -c 3 "$F" | od -An -tx1 | tr -d " \n")" != "efbbbf" ] && [ -s "$F" ]; check QA-12a "no UTF-8 BOM" $?
LC_ALL=C grep -qP '[^\x00-\x7F]' "$F"; [ $? -ne 0 ]; check QA-12b "pure ASCII" $?
jq -e . "$F" >/dev/null; check QA-12c "valid JSON" $?
[ "$(jq -r '.realmlist | startswith("set realmlist ")' "$F")" = true ]; check QA-12d "realmlist starts with lowercase 'set realmlist '" $?
[ "$(jq -r 'keys_unsorted | join(",")' "$F")" = "server_id,name,host,realm,realmlist" ]; check QA-12e "keys in the documented order" $?
[ "$(jq -r '.realmlist' "$F" | od -An -c | tr -d ' \n' | grep -c '\\r\\n')" -ge 1 ]; check QA-12f "realmlist uses CR LF inside the JSON string" $?
grep -q $'\r' "$F"; [ $? -ne 0 ]; check QA-12g "JSON file itself uses LF only (no raw CR)" $?
[ "$(tail -c1 "$F" | od -An -tx1 | tr -d " \n")" = "0a" ] && [ "$(tail -c2 "$F" | od -An -tx1 | tr -d " \n")" != "0a0a" ]; check QA-12h "ends with exactly one trailing newline" $?
jq -e '.realmlist | endswith("\r\n")' "$F" >/dev/null; check QA-12i "every realmlist line terminated (ends with CR LF)" $?
[ "$(jq -r '.realm' "$F")" = "Realm Two" ]; check QA-12j "realm carried through" $?
ls "$PROJ_DIR" | grep -q '\.tmp$'; [ $? -ne 0 ]; check QA-12k "no leftover temp files in projection dir" $? "$(ls -a "$PROJ_DIR")"
rp status; expect_out QA-12l "status says in sync" "in sync"
echo '{"tampered":1}' > "$F"; rp status; expect_out QA-12m "status detects tampered activo.json" "OUT OF SYNC"
rp switch Second >/dev/null; rp status; expect_out QA-12n "switch repairs activo.json" "in sync"

echo "--- QA-10: corrupt / hostile config"
cp "$CONFIG_DIR/servers.json" "$WORK/servers.good"
echo '{ not json' > "$CONFIG_DIR/servers.json"
rp list;                                                     expect_rc QA-10a "corrupt servers.json -> exit 2, message names file" 2
expect_out QA-10b "error message mentions servers.json" "servers.json"
rp add X x.example.org;                                      expect_rc QA-10c "add refuses to overwrite a corrupt servers.json" 2
[ "$(cat "$CONFIG_DIR/servers.json")" = "{ not json" ]; check QA-10d "corrupt servers.json left untouched" $?
: > "$CONFIG_DIR/servers.json"; rp list;                      expect_rc QA-10e "empty (0 byte) servers.json -> exit 2, no crash" 2
printf '\xef\xbb\xbf' > "$CONFIG_DIR/servers.json"; cat "$WORK/servers.good" >> "$CONFIG_DIR/servers.json"; rp list
[ "$RC" -le 2 ]; check QA-10f "BOM servers.json: no crash (rc<=2)" $? "rc=$RC err=[$ERR]"
sed 's/"version": 1/"version": 99/' "$WORK/servers.good" > "$CONFIG_DIR/servers.json"; rp list; expect_rc QA-10g "unsupported version -> exit 2" 2
printf 'null' > "$CONFIG_DIR/servers.json"; rp list;         expect_rc QA-10h "'null' servers.json -> exit 2, no crash" 2
printf '{"version":1,"servers":[{"id":"x"}]}' > "$CONFIG_DIR/servers.json"; rp list; expect_rc QA-10i "server missing fields -> exit 2" 2
jq '.servers[0].realmlist = "set realmlist evil.example.org\nset realmlist x\n"' "$WORK/servers.good" > "$CONFIG_DIR/servers.json"
rp switch "$(jq -r '.servers[0].name' "$WORK/servers.good")"; expect_rc QA-10j "hand-edited invalid realmlist in servers.json: switch refused (exit 2)" 2
jq '.servers[0].realmlist = "set realmlist caf\u00e9.example.org\r\n"' "$WORK/servers.good" > "$CONFIG_DIR/servers.json"
rp switch "$(jq -r '.servers[0].name' "$WORK/servers.good")"; expect_rc QA-10k "hand-edited non-ASCII realmlist: switch refused, no activo.json written" 2
cp "$WORK/servers.good" "$CONFIG_DIR/servers.json"
cp "$CONFIG_DIR/state.json" "$WORK/state.good"
echo 'garbage' > "$CONFIG_DIR/state.json"; rp status;      expect_rc QA-10l "corrupt state.json -> exit 2, no crash" 2
rp switch Second;                                            expect_rc QA-10m "switch with corrupt state.json refuses (does not silently reset)" 2
cp "$WORK/state.good" "$CONFIG_DIR/state.json"
printf '{"version":1,"active_id":"deadbeef","undo":null}' > "$CONFIG_DIR/state.json"; rp status; expect_out QA-10n "state pointing to a missing server -> 'none', no crash" "Active server: none"

echo "--- QA-11: permissions"
cp "$WORK/state.good" "$CONFIG_DIR/state.json"
chmod a-w "$PROJ_DIR"; rp switch Wotlk;                      expect_rc QA-11a "read-only projection dir -> exit 2 (no stack trace)" 2
echo "$ERR" | grep -q "   at "; [ $? -ne 0 ]; check QA-11b "no stack trace leaked" $? "$ERR"
chmod u+w "$PROJ_DIR"
[ "$(jq -r .active_id "$CONFIG_DIR/state.json")" = "$(jq -r .active_id "$WORK/state.good")" ]; check QA-11c "failed projection left state.json unchanged" $?
chmod a-w "$CONFIG_DIR"; rp add Perm perm.example.org;       expect_rc QA-11d "read-only config dir: add -> exit 2" 2
chmod u+w "$CONFIG_DIR"
chmod 000 "$CONFIG_DIR/servers.json"; rp list;               expect_rc QA-11e "unreadable servers.json -> exit 2" 2
chmod 600 "$CONFIG_DIR/servers.json"
chmod 000 "$C12340/Wow.exe"; rp list --client-dir "$C12340"; expect_rc QA-11f "unreadable Wow.exe -> exit 2" 2
chmod 644 "$C12340/Wow.exe"
chmod a-r "$C12340"; rp client-version "$C12340";            expect_rc QA-11g "unlistable client dir -> exit 2" 2
chmod u+rwx "$C12340"
chmod 444 "$PROJ_DIR/activo.json"; rp switch Second; RC_RO=$RC; chmod 644 "$PROJ_DIR/activo.json"
[ "$RC_RO" -le 2 ]; check QA-11h "read-only activo.json (dir writable): no crash (rc<=2)" $? "rc=$RC_RO"
mkdir -p "$WORK/ro"; chmod a-w "$WORK/ro"
rp add Z z.example.org --config-dir "$WORK/ro/new";          expect_rc QA-11i "uncreatable config dir -> exit 2" 2
chmod u+w "$WORK/ro"

echo "--- QA-08 (cont.): host validation (known defect: ISSUE_HOST)"
for h in "http://x.example.org" "a.example.org/path" 'a;b' '$(id)' "a.example.org:99999" "-bad.example.org"; do
  rp add "H$RANDOM" "$h"; [ "$RC" -ne 0 ]; ok=$?
  known QA-08s "ISSUE_HOST" "add rejects malformed host [$h]" $ok "rc=$RC out=[$OUT]"
done

echo "--- QA-13: remove, odd names, activo.json with non-ASCII display name"
rm -rf "$CONFIG_DIR"/* "$PROJ_DIR"/*
rp add Alpha alpha.example.org >/dev/null; rp add Beta beta.example.org >/dev/null
rp switch Alpha >/dev/null; rp remove Alpha;                 expect_rc QA-13a "remove the active server" 0
[ ! -e "$PROJ_DIR/activo.json" ]; check QA-13b "removing the active server deletes activo.json" $?
rp status;                                                   expect_out QA-13c "status: no active server, in sync" "Active server: none"
rp switch Beta >/dev/null; rp switch Beta >/dev/null; rp undo; expect_out QA-13d "switch same server twice keeps one undo level (back to none)" "no active server"
rp add "$(printf '\xc3\x91and\xc3\xba')" nandu.example.org --realm "Reino"; rp switch "$(printf '\xc3\x91and\xc3\xba')" >/dev/null
LC_ALL=C grep -qP '[^\x00-\x7F]' "$PROJ_DIR/activo.json"; ok=$?
echo "INFO  QA-13e activo.json with a non-ASCII server NAME contains non-ASCII bytes: $([ $ok -eq 0 ] && echo yes || echo no) (documented in ActiveProjection; only the 'realmlist' field is guaranteed ASCII)"
jq -e '.realmlist|test("^[\\x00-\\x7f]*$")' "$PROJ_DIR/activo.json" >/dev/null; check QA-13f "realmlist field itself stays ASCII even then" $?

echo "--- QA-14: concurrency across processes (known defect: ISSUE_RACE)"
par() { HOME="$FAKE_HOME" REALM_PORTAL_CONFIG_DIR="$CONFIG_DIR" REALM_PORTAL_PROJECTION_DIR="$PROJ_DIR" dotnet "$CLI_DLL" "$@" >/dev/null 2>&1; }
rm -rf "$CONFIG_DIR"/* "$PROJ_DIR"/*
for i in 1 2 3 4 5 6 7 8; do par add "Q$i" "q$i.example.org" & done; wait
rp list --all; N="$(echo "$OUT" | grep -c 'example.org')"
[ "$N" = 8 ]; known QA-14a "ISSUE_RACE" "8 parallel 'add' (all exit 0) -> 8 servers stored" $? "stored=$N of 8"
BAD=0
for round in 1 2 3 4 5 6 7 8; do
  rp list --all >/dev/null; for i in 1 2 3 4 5 6; do rp add "S$round$i" "s$round$i.example.org" >/dev/null; done
done
for round in 1 2 3 4 5 6 7 8; do
  for i in 1 2 3 4 5 6; do par switch "S1$i" & done; wait
  rp status; echo "$OUT" | grep -q 'in sync' || BAD=$((BAD+1))
done
[ "$BAD" = 0 ]; known QA-14b "ISSUE_RACE" "6 parallel 'switch' x8 rounds -> activo.json always in sync with state.json" $? "out-of-sync rounds: $BAD/8 (status flags it; 'switch' repairs it)"

summary
