#!/usr/bin/env bash
# Fails unless a built package's binary reports the expected install kind (#206).
# usage: check-install-kind.sh <binary> <Deb|AppImage|WindowsZip> <true|false>
# Runs the hidden `<binary> --print-install-kind`, which prints e.g. "AppImage self-replace=true" and exits
# before any window or single-instance socket is created.
set -euo pipefail

[[ $# -eq 3 ]] || { echo "usage: $(basename "$0") <binary> <Deb|AppImage|WindowsZip> <true|false>" >&2; exit 2; }
binary="$1"
want="$2 self-replace=$3"

[[ -x "$binary" || "$binary" == *.exe ]] || { echo "error: not an executable: ${binary}" >&2; exit 1; }
if ! got="$(timeout 60 "$binary" --print-install-kind)"; then
  echo "error: ${binary} --print-install-kind failed" >&2
  exit 1
fi
got="${got%$'\r'}"
if [[ "$got" != "$want" ]]; then
  echo "error: ${binary} reports '${got}', expected '${want}'" >&2
  exit 1
fi
echo "ok: ${binary}: ${got}"
