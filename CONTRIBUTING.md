# Contributing to Realm Portal

Thanks for your interest! This project is MIT-licensed; the app UI is available in Spanish and English.

## Build and test

Requires the .NET SDK pinned in [`global.json`](global.json) (9.0.x).

```bash
dotnet restore RealmPortal.sln
dotnet build RealmPortal.sln -c Release --no-restore
dotnet test RealmPortal.sln -c Release --no-build
```

CI runs build + tests on Ubuntu 22.04 and Windows, plus a secret scan.

## Secret scanning (gitleaks)

Never commit credentials, tokens, private keys or `.env` files. CI scans the full git
history with [gitleaks](https://github.com/gitleaks/gitleaks); you can catch leaks earlier
by enabling the pre-commit hook:

```bash
pip install pre-commit      # or: brew install pre-commit
pre-commit install          # scans staged changes on every commit
pre-commit run --all-files  # optional: scan on demand
```

If a secret was committed, treat it as compromised: revoke/rotate it first, then remove it
from history. A confirmed false positive can be allow-listed in a `.gitleaks.toml`
(explain why in the PR).

## Reporting vulnerabilities

Please do not open public issues for security problems. See [SECURITY.md](SECURITY.md).
