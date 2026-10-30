# Security Policy

## Reporting a vulnerability

Please report vulnerabilities **privately** using GitHub's
[private vulnerability reporting](https://github.com/KarloZ7715/realm-portal/security/advisories/new)
(Security tab → "Report a vulnerability"). Do not open a public issue or pull request
for security problems.

Include the affected version/commit, steps to reproduce and the potential impact.
You can expect an initial response within a few days. Fixes are released as soon as
reasonably possible and credited to the reporter unless you prefer otherwise.

## Release signing

From 1.2.0 on, each release's `SHA256SUMS` is signed with [minisign](https://jedisct1.github.io/minisign/). The release
workflow builds the packages and leaves a draft release. The maintainer then signs `SHA256SUMS` offline with
`packaging/sign-and-publish.sh` and publishes, which uploads the signature as `SHA256SUMS.minisig`. Releases are
immutable once published, and GitHub attests them. The process is described in
[docs/linux-packaging.md](docs/linux-packaging.md#signing-and-publishing-a-release).

The release key has the ID `A71D9203F538E296`. Its fingerprint is the base64 line of the public key:

```text
RWSW4jj1A5Idp08WKXT16AwD21ICuzdEz0CoIx39Wji6Zsffoqkq96Sl
```

The public key is in [`minisign.pub`](minisign.pub) at the repository root, and the app has the same key built in.

The trusted comment of each signature is `realm-portal <tag>`, for example `realm-portal v1.2.0`.

The app does not check the signature yet; see
[docs/updates-and-status.md](docs/updates-and-status.md#release-signature-116-not-enforced-yet).

### Verifying a release by hand

```sh
gh release download v1.2.0 --repo KarloZ7715/realm-portal \
  -p SHA256SUMS -p SHA256SUMS.minisig -p RealmPortal-1.2.0-x86_64.AppImage
minisign -V -p minisign.pub -m SHA256SUMS      # must print: Trusted comment: realm-portal v1.2.0
sha256sum --check --ignore-missing SHA256SUMS  # checks the packages you downloaded
gh release verify v1.2.0 --repo KarloZ7715/realm-portal
```

Use a `minisign.pub` you trust, such as the one from this repository or the key line above, and check that the trusted
comment names the tag you downloaded. A signature by another key, or one with another tag in its trusted comment, means
the release must not be trusted.

### Key rotation

1. Generate a new key pair and add the new public key to `ReleaseSigningKeys` next to the old one.
2. Ship that in a release signed with the old key, so existing installs learn the new key.
3. Sign later releases with the new key and replace `minisign.pub` with it.
4. Drop the old key from `ReleaseSigningKeys` once users have had time to update.

### If the key is lost or leaked

- **Lost** (secret key or password): nothing already published is affected, but no new release can be signed with it.
  Generate a new key and rotate as above. Until the app enforces signatures, the new key is enough for users. Once it
  does, installs that only know the old key cannot verify releases signed with the new key, so they must update by
  hand from the release page once.
- **Leaked or suspected leaked:** treat every signature by that key made after the leak as untrusted. Remove the key
  from `ReleaseSigningKeys` and `minisign.pub`, publish a new key and a release signed with it, and announce the key ID
  of the revoked key in a security advisory and the release notes. Users have to install that release by hand from
  the release page and check it with the new key.

If you find a release whose signature does not verify with `minisign.pub`, or a signing key in the wrong place, report
it privately as described above.

## Supported versions

The project is pre-release; only the latest commit on `main` is supported.
