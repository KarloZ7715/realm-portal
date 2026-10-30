# Update notice and server status dot (1.1.0)

Design for [#105](https://github.com/KarloZ7715/realm-portal/issues/105) (update notice) and
[#104](https://github.com/KarloZ7715/realm-portal/issues/104) (online status dot). These two features are the only network
access the app makes. Both can be turned off in Settings, and neither sends telemetry or identifiers.

## Settings

Persisted in `network-settings.json` in `configDirectory` (`version: 1`, `0600` permissions, bounded reads and writes
under `ConfigDirectoryLock`, same pattern as `storage-settings.json`, see [app-service.md](app-service.md)):

| Key | Default | Meaning |
|---|---|---|
| `updates.auto_check` | `true` | Daily automatic check for a new release. `false` means **zero automatic network requests**. |
| `updates.last_attempt_utc` | none | Time of the last automatic check **attempt**, successful or failed. A value in the future is treated as "now". |
| `updates.etag` | none | ETag of the last `200` response from `releases/latest`, stored with that response body (see below). |
| `updates.skipped_version` | none | Version the user chose to skip. |
| `updates.later_version`, `updates.later_until_utc` | none | Version postponed with **Later** and the time before which it is not offered again (3 days). |
| `status.probe_enabled` | `true` | Server status dot. `false` hides the dot and schedules no probes. |

Both defaults are `true` by the maintainer's product decision: at most one automatic check a day, announced in the 1.1.0
release notes, with a manual button, and each feature can be switched off in Settings. Considered and rejected: making
`updates.auto_check` opt-in or asking on first run (recommended by the security review); the maintainer chose on by default
with the controls above. Settings shows next to each toggle:

- "Checks GitHub once a day for a new version (sends your IP address and the app version to GitHub)."
- "Shows whether each server's login port answers (your IP address is visible to that server every 5 minutes while the list
  is open)."

The **Privacy** section in `README.md` / `README.es.md` uses the same Settings toggle wording.

The **Check for updates** button works even when `updates.auto_check` is `false`, because it is an explicit user action. It
is debounced to one request per minute.

## 1. Update notice (#105)

### Threat model

The update check verifies downloads against the release's `SHA256SUMS`. This detects corrupted or truncated downloads only.
It does **not** protect against a compromised GitHub account, token or release workflow, because the attacker would control
both the asset and `SHA256SUMS`. Until [#116](https://github.com/KarloZ7715/realm-portal/issues/116) ships (checksums signed
with a key embedded in the app, the real authenticity control), the protections are:

- The user always confirms **Update**; nothing updates silently.
- Releases are immutable once GitHub "immutable releases" is enabled for the repository (pending, see the release gate
  below), so assets cannot be swapped after publication.
- The maintainer account uses 2FA.

**Release gate:** in-place AppImage replacement ships in 1.1.0 only if immutable releases are enabled for the repository.
This is decided **at packaging time**, not at runtime: the release workflow sets the build-time flag
`AppImageSelfReplace=true` only when the maintainer has enabled immutable releases; the app never queries the repository
settings. With the flag off, the `AppImage` kind downloads and verifies the new AppImage to the XDG Downloads folder (same rules as `Deb`: never
overwrite an existing file), with no replacement. The dialog then shows instructions with the absolute path, quoted as described under "Copyable commands":
`chmod u+x '<absolute path>'` (the file is saved `0600`), then start the new file and delete the old one. No `apt` command is shown.

### HTTP client

- One `HttpClient` with `UseCookies = false`, no credentials, `AllowAutoRedirect = false`. The app never reads `GITHUB_TOKEN`,
  `GH_TOKEN` or any other token from the environment, and never sends an `Authorization` header.
- Headers: `User-Agent: RealmPortal/<version>`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version`, and
  `If-None-Match` when allowed (below). Nothing else.
- Every URL, including the first, must be `https`, port 443 (or none), without user info, and its `Uri.IdnHost` (lower-case)
  must be exactly `github.com`, exactly `api.github.com`, or end with `.githubusercontent.com` (suffix match **with the
  leading dot**, so `evilgithubusercontent.com` and `github.com.evil.example` fail). Any other URL fails with
  `UntrustedRedirect`.

### Check

- Request: `GET https://api.github.com/repos/KarloZ7715/realm-portal/releases/latest`, built from constants, timeout 10 s.
  Redirects are not followed for this request. Only `200` and `304` are accepted.
- On `403` or `429`, honour `Retry-After` / `x-ratelimit-reset` and make no request before then (the unauthenticated limit
  is 60 requests per hour per IP).
- The JSON body is read through a bounded stream with a **1 MiB** cap on bytes actually read; `Content-Length` is not
  trusted.
- The automatic check runs in the background after startup, and only if `now - last_attempt_utc >= 24 h`.
  `last_attempt_utc` is written when the attempt starts, so a failed check also waits 24 h. It never blocks the UI.
  Failures are silent and retried on the next day.
- ETag: `If-None-Match` is sent only when the app has both the ETag and the cached body of that `200` response
  (`update-cache.json` in `configDirectory`, same storage rules as the settings). A `304` reuses the cached body. If the
  cache is missing or unreadable, the app drops the ETag and makes an unconditional request before any version comparison,
  notice or download.

### Version

- `draft` and `prerelease` must be `false`. The check uses `GET …/releases/latest`, which GitHub never points at a
  prerelease; the parser also rejects `prerelease: true`. Release tags such as `vX.Y.Z-rc.N` are published as GitHub
  prereleases (`--prerelease --latest=false`, see [linux-packaging.md](linux-packaging.md)), so they are never offered.
  There is no Settings opt-in to include prereleases in 1.1.0; if one is added later it must use a different API than
  `/releases/latest` and stay off by default.
- `tag_name` must match `^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$`, with no pre-release or build suffix. Components are
  parsed as integers; overflow means unparseable. An unparseable tag means no notice.
- The running version is the assembly `InformationalVersion` with any `+metadata` removed. If it is not a valid SemVer, no
  notice is shown.
- A notice is shown only when the release version is **strictly greater** than the running version and not equal to
  `skipped_version`. Equal or lower versions are never offered, whatever the cached or fresh response says.
- `{v}` in asset names and URLs is always built from the parsed version, never copied from `tag_name`.

### Dialog

The app shows **its own dialog** (never a desktop notification) with the version, the release notes as plain text (no HTML,
no remote images, no clickable links, truncated to 20 000 characters) and three buttons: **Update**, **Later** (wait for the
next check) and **Skip this version** (stores `skipped_version`). After **Later**, the same version is offered again no
sooner than **3 days** later (`updates.later_version` and `updates.later_until_utc`); a newer version is offered at the next
daily check as usual.

"Open release page" uses `html_url` only if it starts with `https://github.com/KarloZ7715/realm-portal/releases/tag/`;
otherwise it uses `https://github.com/KarloZ7715/realm-portal/releases/tag/v{v}`.

### Install kind

The install kind is a build-time constant set by packaging: `InstallKind` is one of `AppImage`, `Deb`, `WindowsZip` or
`Unknown`. Development builds are `Unknown`. No runtime heuristics on install paths or `dpkg`.

For `AppImage`, the `APPIMAGE` environment variable (set by the AppImage runtime) must also be present, and before
downloading the app checks it:

- Resolve `$APPIMAGE` to a real path. If it is a symlink, the resolved target is replaced, never the link.
- The target must be an absolute path to a regular file owned by the current user, starting with the ELF magic and the
  AppImage type 2 magic, in a directory the user can write.
- Record the target's `(device, inode)` and file mode.
- If any check fails, the kind is treated as `AppImage` without replacement (verified download to the XDG Downloads folder
  plus the instructions above).

### Assets and checksums

| Kind | Asset |
|---|---|
| `AppImage` | `RealmPortal-{v}-x86_64.AppImage` |
| `Deb` | `realm-portal_{v}_amd64.deb` |
| `WindowsZip` | `RealmPortal-{v}-win-x64.zip` |

- The download URL is built by the app as `https://github.com/KarloZ7715/realm-portal/releases/download/v{v}/{asset}`. The
  API's `browser_download_url` must equal it, otherwise `AssetMissing`. The asset's `size` from the API is pinned.
- `SHA256SUMS` is downloaded the same way and read with a **64 KiB** cap. Each line must match
  `^[0-9a-f]{64} [ *]?<name>$` (LF or CRLF). The asset name must match **exactly one** line (byte-exact, case-sensitive);
  zero or several matches mean `AssetMissing`. The digest is decoded from hex and compared with the computed SHA256 bytes
  using `CryptographicOperations.FixedTimeEquals`.
- Release process: the published `SHA256SUMS` must list every asset of the release (v1.0.0 and v1.0.1 do). The release workflow
  should check this automatically; otherwise the affected kinds fall back to the release page.

### Release signature (#116, in progress)

Core has a minisign verifier for `SHA256SUMS.minisig` (`RealmPortal.Core.Updates.Signing`), not yet called by
`UpdateDownloader`:

- Only prehashed `ED` signatures (Ed25519 over the BLAKE2b-512 hash of `SHA256SUMS`) are accepted; legacy `Ed` is
  rejected. The key ID in the signature picks the key from `ReleaseSigningKeys`, which is empty until the release key
  exists, so every check currently returns `NoTrustedKeys`.
- The trusted comment must be exactly `realm-portal <tag>` (e.g. `realm-portal v1.2.0`), which stops an older release's
  signed `SHA256SUMS` from being replayed. The release is signed with
  `minisign -S -m SHA256SUMS -t "realm-portal v1.2.0"`.
- The parser is strict: four lines (LF or CRLF), canonical base64, a 16 KiB cap on the signature file, and nothing after
  the global signature. Bad input gives a `SignatureStatus`, never an exception.
- Ed25519 and BLAKE2b come from BouncyCastle (MIT), kept in one adapter (`BouncyCastleEd25519`).

### Download rules (all kinds)

1. Redirects are followed manually, at most **5** hops, each checked with the URL rule above.
2. Each download has a 10 s connect timeout, a 30 s read timeout (no bytes received) and an overall deadline (1 min for
   `SHA256SUMS`, 10 min for the asset).
3. Size limit: refuse when `Content-Length` is above the pinned `size` or 300 MB, **and** count bytes while streaming; abort
   with `DownloadTooLarge` as soon as the count exceeds either.
4. The `.part` file is created with `FileMode.CreateNew`, a random suffix (`.RealmPortal-{v}.{random}.part`) and mode
   `0600`, in the destination directory (same filesystem). SHA256 is computed while streaming. The file is flushed to disk
   (`fsync`) before verification and rename. Stale `.RealmPortal-*.part` files from a crash are deleted on the next update
   attempt, only in the destination directory, only if `lstat` shows a regular file owned by the user; symlinks and
   anything else are left alone.
5. On mismatch, timeout, cancellation or any other failure, delete the `.part` file, show an error and offer to open the
   release page.
6. Nothing downloaded is executed automatically or before verification. The only time a downloaded file runs is when the
   user presses **Restart now** after a verified AppImage replacement. That starts the replaced target path directly with
   `Process.Start` (`UseShellExecute = false`, no shell, the current arguments only), then the running app exits. `.deb` and zip files are never opened or run by the
   app.

### What "Update" does

| Kind | Action |
|---|---|
| `AppImage` (release gate met) | Download into the target's directory. After verification, re-check that the target still has the recorded `(device, inode)` (otherwise abort without writing). This is a best-effort guard, not an atomic compare-and-replace: a concurrent writer could still replace the target between the check and the path-based `rename(2)`, which is accepted because the target is in a directory owned by the user. Then apply the target's **original file mode masked with `0755`** to the `.part` (setuid, setgid, sticky and group/other write are dropped), keep the old file as a hard link `.RealmPortal-{old}.AppImage.bak`, then `rename(2)` the `.part` over the target in the same directory. The dialog offers **Restart now**. **`.bak` rules:** if a `.bak` with that name already exists, it is removed first only if `lstat` shows a regular file owned by the user (never a symlink is followed); otherwise the update aborts. There is **no automatic rollback**. The new version deletes the `.bak` (same `lstat` checks) at its first successful start; if it never starts, the `.bak` stays and the error dialog shows its name so the user can restore it by hand. |
| `Deb` | **No in-app install**: it needs root, and the app never calls `pkexec` or `sudo` or writes to a root-owned path. Download the verified file to the XDG Downloads folder (mode `0600`), open that folder, and show the command `sudo apt install '<absolute path>'`, built from the real XDG Downloads path and the real saved file name, quoted, so the copied command works from any directory. The UI may show the path `~`-relative only if the copied command uses the absolute one. |
| `WindowsZip` | Download the verified zip to the user's Downloads folder, open it in Explorer, and show "extract over your current folder" instructions. No self-replacement in 1.1.0. Optional: write a `Zone.Identifier` (Mark of the Web) on the zip. |
| `Unknown` or any failure | Open the release page (validated as above) in the browser. |

In the Downloads folder an existing file is never overwritten: if the name is taken, a ` (1)`, ` (2)`… suffix is added.
The final name is set only after verification.

**Copyable commands** (`chmod u+x …`, `sudo apt install …`) quote the absolute path with POSIX single quotes, escaping each
`'` in the path as `'\''`, so any path works when pasted into a shell. If the path contains a control character (including
a newline), no command is shown, only the folder and the file name. Tests cover paths with spaces, quotes and `$`.

### Logging

Logs record the error code, the HTTP status and the **host only**. Full URLs are never logged, because the
`*.githubusercontent.com` redirect targets carry signed, short-lived tokens. Paths are logged as a file name or
`~`-relative, never the full `$APPIMAGE` or home directory path. There is no log file in the app today, so 1.1.0 adds one: `$XDG_STATE_HOME/realm-portal/logs/realm-portal.log`
(default `~/.local/state/...`; `%LOCALAPPDATA%\RealmPortal\logs\` on Windows), directory `0700`, file `0600`, capped at
1 MiB with a single rotated `.1` file.

## 2. Server status dot (#104)

### Probe

- Reuses the existing `TcpConnectionProbe` (`src/RealmPortal.App/Portal/ConnectionProbe.cs`): connect only, one DNS
  resolution per probe, every resolved address tried within one 3 s budget.
- Only the stored `set realmlist` host, already validated with `RealmlistAddress.ValidateRealmlistHost`, is probed. Port
  3724 is used when none is given. `patchlist`, launch-template hosts and anything from client files that has not been
  validated are never probed.
- **No data is sent**: the socket is closed right after the connection succeeds.
- States: `online` (connected), `offline` (refused, timed out, or any other connection error such as network or host
  unreachable; the tooltip gives the reason, for example "connection refused", "timed out" or "network error
  (`NetworkUnreachable`)"), `checking`, and `unknown`
  (not probed yet, or the host name could not be resolved). A DNS failure (`SocketError.HostNotFound` or `NoData`) is
  `unknown` with the reason "name not found (DNS)", so a typo in the realmlist host does not look like a server that is
  down. The add or edit server form shows the same warning after a probe. Today `TcpConnectionProbe` folds every
  non-timeout `SocketException` into `ProbeResult.Failed`; the implementation PR extends the result with the `SocketError`
  code so the scheduler can apply this mapping and show the reason.
- The tooltip also says when the result was taken ("checked 3 min ago"), because results are reused for up to 5 minutes.
- The tooltip says **"Login server reachable"**, not "server online": only the login port is checked, not the world server.
- Private, loopback and LAN addresses are allowed, because local servers are legitimate. A shared realmlist can therefore
  make the app connect to an internal `host:port` every 5 minutes; this is accepted because it is connect-only, the server
  was added by the user, and the rate is limited. DNS rebinding is irrelevant because no data is sent.

### Scheduling

- Each server is probed **at most once every 5 minutes**. When the list opens, servers whose last probe is older than 5 min
  are probed; the others keep their last result. While the window is open and not minimized, each server is probed again
  5 min after its previous probe plus a random 0 to 30 s delay. Jitter only adds delay; nothing bypasses the 5-minute
  minimum.
- At most **4 concurrent** probes, and never two probes at once for the same server. A probe counts against the limit until
  its underlying operation finishes, not only until its 3 s timeout (`Dns.GetHostAddressesAsync` does not really cancel on
  Linux), so a slow resolver cannot pile up threads.
- Probing stops when the app closes. Nothing is probed at startup unless the list is shown.
- Turning `status.probe_enabled` off cancels in-flight probes and hides the dot.

### Privacy

Probing reveals the user's IP address to servers the user added, at most once every 5 minutes per server. Resolving a host
name may also query the DNS resolver configured on the system (on a cache miss). Nothing else is contacted.

## 3. Placement

- Core: `IReleaseFeed`, `UpdateChecker`, `UpdateDownloader` (verify and place atomically), `InstallKind`, and
  `ProbeScheduler` on top of the existing `IConnectionProbe`. The UI only owns the dialogs, the toggles and the dot.
- New error codes: `UpdateCheckFailed`, `ChecksumMismatch`, `AssetMissing`, `DownloadTooLarge`, `UntrustedRedirect`,
  `RateLimited`.

## 4. Tests

- A fake `HttpMessageHandler` with release JSON fixtures: newer, same, lower, skipped, draft or pre-release, unparseable or
  suffixed tag, `304` with and without a cached body, `403`/`429` with `Retry-After`, oversized JSON, missing asset,
  mismatched `browser_download_url`, missing or duplicated `SHA256SUMS` line, checksum mismatch, oversized stream despite a
  small `Content-Length`, redirect chains (untrusted host, look-alike hosts, `http`, non-443 port, user info, more than 5
  hops).
- No automatic request is made when `updates.auto_check` is `false`, or within 24 h of the last attempt (including a
  failed one). The **Check for updates** button makes one request even when it is `false`, and is debounced.
- No `Authorization` header and no environment tokens are ever sent.
- AppImage replacement in a temporary directory: symlinked `$APPIMAGE`, non-regular file, wrong magic, changed inode before
  rename, mode masked to `0755` (setuid and group/other write dropped), `.bak` created, an existing `.bak` that is a
  symlink or not owned by the user aborts the update, stale `.part` cleanup skips symlinks and non-regular files;
  `AppImageSelfReplace=false` builds never replace and show no `apt` command; `.deb` and zip paths never execute anything,
  never overwrite an existing file, and the `.deb` command uses the absolute path.
- **Later** postpones the same version for 3 days; a newer version is still offered at the next daily check.
- Logs contain no full URLs or full home paths; the log file is `0600` and rotates at 1 MiB.
- DNS failure gives `unknown` with the DNS reason, not `offline`; the tooltip shows the age of the result.
- Probe against a loopback `TcpListener` (online), a closed port (offline), and an injected connect delegate for timeouts
  and slow DNS. No test touches the real internet.
- Concurrency cap counting unfinished operations, per-server single flight, the 5-minute minimum per server (including on
  list reopen); disabling the probe cancels in-flight probes and hides the dot.
