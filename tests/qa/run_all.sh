#!/usr/bin/env bash
# Runs the whole QA suite: unit tests, CLI scenarios and client-file engine scenarios (all on synthetic clients in temp dirs).
set -u
cd "$(dirname "${BASH_SOURCE[0]}")"
rc=0
echo "=== unit tests ==="; ( cd ../.. && dotnet test RealmPortal.sln -c Release 2>&1 | grep -E "Passed!|Failed!|error" ) || rc=1
echo "=== CLI scenarios ===";    QA_RESULTS_FILE="${QA_OUT:-/tmp}/qa-cli-results.txt" ./cli_scenarios.sh | tail -n +1 | grep -vE '^PASS' ; [ "${PIPESTATUS[0]}" -eq 0 ] || rc=1
echo "=== engine scenarios ==="; QA_RESULTS_FILE="${QA_OUT:-/tmp}/qa-engine-results.txt" ./client_files_scenarios.sh | grep -vE '^PASS' ; [ "${PIPESTATUS[0]}" -eq 0 ] || rc=1
exit $rc
