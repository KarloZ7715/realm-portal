# Contributing to Realm Portal

Thanks for your interest! This project is MIT-licensed; the app UI is available in Spanish and English.

## Build and test

Requires the .NET SDK pinned in [`global.json`](global.json) (9.0.x).

```bash
dotnet restore RealmPortal.sln
dotnet build RealmPortal.sln -c Release --no-restore
dotnet test RealmPortal.sln -c Release --no-build
```

CI runs build + tests on Ubuntu 22.04 and Windows, plus a secret scan and a per-file size check.

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

## File size limit

Keep the repository small: no tracked file may exceed **5 MiB**. The `file-size` CI job
(`.github/workflows/file-size.yml`) checks every tracked file and fails the build otherwise.
Today the largest asset is under 1.5 MiB, so the limit leaves plenty of room for textures
and fonts.

If a file genuinely has to be larger (for example a texture or font that cannot be
reduced), list its exact repository-relative path in
[`.github/file-size-allowlist.txt`](.github/file-size-allowlist.txt) with a comment explaining
why, and mention it in the PR description. Prefer Git LFS or an external download for
anything bigger than a few MiB. Large raw design sources and build outputs do not belong
in the repository.

## Reporting vulnerabilities

Please do not open public issues for security problems. See [SECURITY.md](SECURITY.md).
