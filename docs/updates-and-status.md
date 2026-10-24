# Update notice and server status dot (1.1.0)

Design for [#105](https://github.com/KarloZ7715/realm-portal/issues/105) (update notice) and
[#104](https://github.com/KarloZ7715/realm-portal/issues/104) (online status dot). Both features are the only network access
the app makes, both can be turned off in Settings, and neither sends telemetry or identifiers.

## Settings

Persisted in `network-settings.json` in `configDirectory` (`version: 1`, written with `0600` permissions, same pattern as
`storage-settings.json`, see [app-service.md](app-service.md)):

| Key | Default | Meaning |
|---|---|---|
| `updates.auto_check` | `true` | Daily automatic check for a new release. `false` means **zero automatic network requests**. |
| `updates.last_check_utc` | none | Time of the last completed automatic check. |
| `updates.etag` | none | ETag of the last `releases/latest` response. |
| `updates.skipped_version` | none | Version the user chose to skip. |
| `status.probe_enabled` | `true` | Server status dot. `false` hides the dot and schedules no probes. |

The **Check for updates** button in Settings works even when `updates.auto_check` is `false`, because it is an explicit
user action.

## 1. Update notice (#105)

### Check

- Request: `GET https://api.github.com/repos/KarloZ7715/realm-portal/releases/latest` (this endpoint excludes drafts and
  pre-releases). HTTPS only, unauthenticated, `User-Agent: RealmPortal/<version>`, `If-None-Match: <etag>`, timeout 10 s.
  No other headers, no identifiers.
- The automatic check runs in the background after startup, and only if `now - last_check_utc >= 24 h`. It never blocks the
  UI. Failures are silent (logged locally) and retried on the next day.
- Versions: `tag_name` is parsed as SemVer `vX.Y.Z`. The user is notified only if it is greater than the running version and
  not equal to `skipped_version`. An unparseable tag means no notification.
- When a newer version exists, the app shows **its own dialog** (never a desktop notification) with the version, the
  release notes as plain text (no HTML, no remote images), and three buttons: **Update**, **Later** (wait for the next daily
  check) and **Skip this version** (stores `skipped_version`).

### Install kind

The install kind is a build-time constant set by packaging: `InstallKind` is one of `AppImage`, `Deb`, `WindowsZip` or
`Unknown`. Development builds are `Unknown`. For `AppImage`, the `APPIMAGE` environment variable (set by the AppImage
runtime to the path of the running file) must also be present; if it is missing the kind is treated as `Unknown`.
No runtime heuristics on install paths or `dpkg`.

### Asset selection

Assets are matched by exact name for the install kind:

| Kind | Asset |
|---|---|
| `AppImage` | `RealmPortal-{v}-x86_64.AppImage` |
| `Deb` | `realm-portal_{v}_amd64.deb` |
| `WindowsZip` | `RealmPortal-{v}-win-x64.zip` |

The checksum comes from the release's `SHA256SUMS` asset. If the asset or its line in `SHA256SUMS` is missing, the app falls
back to opening the release page.

### Download rules (all kinds)

1. Download `SHA256SUMS` and the asset from its `browser_download_url`.
2. Follow redirects only to `github.com` or `*.githubusercontent.com`, over HTTPS.
3. Refuse anything larger than 300 MB.
4. Write to a `.part` file and compute SHA256 while streaming; compare it to the `SHA256SUMS` line.
5. On mismatch, delete the file, show an error and offer to open the release page.
6. Never execute anything that was downloaded.

### What "Update" does

| Kind | Action |
|---|---|
| `AppImage` | Download into the directory of `$APPIMAGE` as `.RealmPortal-{v}.AppImage.part`, verify, `chmod +x`, then rename it atomically over the running file (safe on Linux while it runs). The dialog then offers **Restart now**. If the directory is not writable, behave as `Unknown`. |
| `Deb` | **No in-app install**: it needs root, and the app never calls `pkexec` or `sudo`. Download the verified `.deb` to the XDG Downloads folder, open that folder, and show the command `sudo apt install ./realm-portal_{v}_amd64.deb`. |
| `WindowsZip` | Download the verified zip to the user's Downloads folder, open it in Explorer, and show "extract over your current folder" instructions. No self-replacement in 1.1.0. |
| `Unknown` or any failure | Open the release page (`html_url`) in the browser. |

### Integrity caveat

`SHA256SUMS` is published in the same release, so the check proves the download was not corrupted, not who published it.
Signing `SHA256SUMS` (for example with minisign and a public key embedded in the app) or verifying GitHub artifact
attestations is tracked as a follow-up after 1.1.0 in [#116](https://github.com/KarloZ7715/realm-portal/issues/116).

## 2. Server status dot (#104)

### Probe

- TCP connect to `host:port` from the server's first `set realmlist` line, using port 3724 when none is given. Only the
  already validated realmlist is used; unvalidated input is never probed.
- Timeout 3 s. **No data is sent**: the socket is closed right after the connection succeeds.
- System DNS, dual stack (IPv4 and IPv6) as the OS resolves the name.
- States: `online` (connected), `offline` (refused, timed out, or DNS failure; the tooltip gives the reason), `checking`,
  `unknown` (not probed yet).
- The tooltip says **"Login server reachable"**, not "server online": only the login port is checked, not the world server.

### Scheduling

- Probe every listed server when the list opens, then every **5 min ± 10 % jitter** while the window is open and not
  minimized.
- At most **4 concurrent** probes, and never two probes at once for the same server.
- A result younger than 60 s is reused when the list is reopened.
- Probing stops when the app closes. Nothing is probed at startup unless the list is shown.
- Turning `status.probe_enabled` off cancels in-flight probes and hides the dot.

### Privacy

Probing reveals the user's IP address only to servers the user added, at most once every 5 minutes. Nothing else is
contacted. The README's privacy section should state this and the update check.

## 3. Placement

- Core: `IReleaseFeed`, `UpdateChecker`, `UpdateDownloader` (verify and place atomically), `InstallKind`, `IServerProbe`
  with `TcpServerProbe`, and `ProbeScheduler`. The UI only owns the dialogs and the dot.
- New error codes: `UpdateCheckFailed`, `ChecksumMismatch`, `AssetMissing`, `DownloadTooLarge`, `UntrustedRedirect`.

## 4. Tests

- A fake `HttpMessageHandler` with release JSON fixtures: newer, same, skipped, unparseable tag, `304` with ETag, missing
  asset, missing `SHA256SUMS` line, checksum mismatch, redirect to an untrusted host, oversized download.
- No request is made when `updates.auto_check` is `false`, or within 24 h of the last check.
- AppImage replacement in a temporary directory; the `.deb` and zip paths never execute anything.
- Probe against a loopback `TcpListener` (online), a closed port (offline), and an injected connect delegate for timeouts.
  No test touches the real internet.
- Concurrency cap; disabling the probe cancels in-flight probes and hides the dot.
