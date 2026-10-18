# Backup, apply and undo of client files

`ClientFilesApplier` (`RealmPortal.Core.ClientFiles`) is the implementation of `IClientFilesApplier`. What gets written is described
in [client-files.md](client-files.md); this page covers how it is written safely and how it is undone.

## Wiring

```csharp
IClientFilesApplier applier = new ClientFilesApplier(
    new ClientFilesPlanner(),            // IClientFilesPlanner
    new AtomicFileWriter(),              // IAtomicFileWriter
    new GameProcessProbe(),              // IGameProcessProbe
    ClientFilesApplier.DefaultBackupRoot());

var plan = applier.Plan(new ClientFilesRequest(installationId, profileId, clientRoot, clientVersion, server.Realmlist, server.Realm));
var result = applier.Apply(plan);        // ApplyResult(BackupId, Edits)
var undone = applier.Undo(installationId);
```

The backup root is `$REALM_PORTAL_BACKUP_DIR`, else `realm-portal/backups` under the platform's local application data folder
(`$XDG_DATA_HOME` or `~/.local/share` on Linux). Tests always pass a temporary folder.

## Apply

1. An empty plan returns `ApplyResult(null, [])`; no backup is made.
2. The game must not run (`IGameProcessProbe`), otherwise `GameRunning`.
3. Every target is read again; if its sha256 is not the plan's `Sha256Before` (or a file to create now exists) the call fails with
   `HashMismatch` and nothing is changed. Plan again to refresh.
4. A backup is written **first**: the original bytes of each existing file plus `manifest.json` (last, atomically, flushed to disk).
5. Each file is written with `IAtomicFileWriter` and read back; a different hash is `VerifyFailed`. For every existing target, Apply
   captures its permissions (`unix_mode`, `windows_read_only`), records them in the backup manifest so Undo can restore them, and
   reapplies them to the new file. Only the warning depends on read-only state: when a target file was read-only, Apply also
   records a warning in `ApplyResult.Warnings`.
6. On any failure every touched file is put back (created files are deleted) and the backup folder is removed, since nothing changed.
   If the revert itself fails, the backup stays so `Undo` can finish the job.

`ApplyResult.ManifestSha256` is the SHA-256 (64 lowercase hex digits) of the `manifest.json` bytes just written, or null when the plan was empty and no manifest was written. Callers that undo later keep this hash outside the backup folder and pass it back as `expectedManifestSha256`.

Apply and Undo of one installation are serialized, also across processes, with an exclusive lock file
`<backupRoot>/<installationId>/.lock`; a call that cannot get it within 10 s fails with `PermissionDenied`.
An incomplete or invalid `manifest.json` fails with `VerifyFailed`. A manifest whose `client_root` is not the installation's client
folder fails with `PathEscapesClient` before any file is read or written.

## Backup layout

```
<backupRoot>/<installationId>/<backupId>/manifest.json
<backupRoot>/<installationId>/<backupId>/files/<n>      original bytes of the n-th file of the manifest
<backupRoot>/<installationId>/<backupId>/files/<n>.symlink   optional UTF-8 link text when file <n> was a symlink at apply time
```

`files/<n>.symlink` is written only together with `symlink_sha256` (SHA-256 of those exact bytes) on the matching manifest entry. Undo uses a sidecar only when the entry declares that field and the bytes still hash to it.

`backupId` is `yyyyMMddTHHmmssfffZ-NNNNNN` (UTC time plus a sequence number). It is always greater than the newest existing id of the installation
(the same millisecond or a clock that went backwards bump the sequence), so ids sort in apply order.

```json
{
  "version": 1,
  "backup_id": "20261017T120001000Z-000000",
  "installation_id": "install-1",
  "profile_id": "profile-1",
  "client_root": "/path/to/client",
  "client_build": "12340 (3.3.5.12340)",
  "created_at": "2026-10-17T12:00:01+00:00",
  "undone_at": null,
  "files": [
    { "path": "Data/enUS/realmlist.wtf", "existed": true, "backup_file": "files/0",
      "sha256_before": "…", "sha256_after": "…", "unix_mode": 292, "windows_read_only": null,
      "symlink_sha256": null },
    { "path": "WTF/Config.wtf", "existed": false, "backup_file": null,
      "sha256_before": null, "sha256_after": "…", "symlink_sha256": null }
  ]
}
```

`path` uses `/` and the real casing found on disk. `undone_at` is set when the backup was fully undone.
`symlink_sha256` is null when the file was not a symlink. `existed: false` requires `backup_file`, `sha256_before` and `symlink_sha256` to be null; `existed: true` requires `backup_file` and `sha256_before`.

## Undo

`Undo(installationId)` processes the newest backup of the installation that is not undone yet. For each file, by current sha256:

| Current state | Result |
|---|---|
| equals `sha256_before` (or is missing when the file did not exist) | `AlreadyRestored` (an earlier undo was interrupted) |
| equals `sha256_after`, file existed | the original bytes are written back and verified: `Restored`. A symlink is recreated only from a bound sidecar whose chain stays inside the client; otherwise the original bytes are written as a regular file |
| equals `sha256_after`, file did not exist | the file is deleted: `Deleted` (folders are never removed) |
| anything else, including a live file that is not a regular file or is larger than 16 MiB | `Conflict`: the file is not touched, the UI decides |

Before any client file is written or deleted, Undo checks every entry: backup copies of files that existed are opened once and hashed, and every sidecar is checked. That open is the check. On Linux and macOS it uses `O_RDONLY|O_NOFOLLOW|O_NONBLOCK|O_CLOEXEC`, so a final symlink is not followed and a FIFO does not block the open. The same descriptor must be a seekable regular file no larger than 16 MiB; a symlink, FIFO, or other device is `VerifyFailed` (`The backup copy is not a regular file.`) and is not read. A missing payload is `PathNotFound`. A backup copy whose hash no longer matches `sha256_before` is never restored (`HashMismatch`), and nothing is changed. The same is true of a `files/<n>.symlink` that exists without `symlink_sha256`, or whose bytes do not hash to that field. On Windows the reparse-point attributes are checked and then the file is opened by path; a symlink swapped in between those two steps can still be followed.

The live file is hashed the same way, except a managed path may be a symlink and is followed. On Unix that open is `O_RDONLY|O_NONBLOCK|O_CLOEXEC` (no `O_NOFOLLOW`). A FIFO, a character device, or a symlink to `/dev/zero` is not read without a bound: it is `Conflict`, because it does not match `sha256_after`. A regular file larger than 16 MiB is `Conflict` too.

`Undo` accepts an optional `expectedManifestSha256`. When it is set, Undo hashes the manifest bytes it read and throws `HashMismatch` if they differ, before it writes or deletes any client file. The hash belongs outside the backup folder: every file in that folder, including `manifest.json`, can be edited together. The service stores it in `last-switch.json` (see [app-service.md](app-service.md)). When the argument is omitted, Undo does not check the manifest hash. A journal written before `manifest_sha256` existed has no such field; the service then calls `Undo` without the hash, and those journals keep working as before.

A sidecar is refused with `VerifyFailed` (nothing changed) when its path is itself a symlink or not a regular file, when it is larger than 4096 bytes, or when it is not valid UTF-8, is empty, or contains a NUL or other control character. It is opened with the same no-follow open as a backup payload. A FIFO or other non-regular sidecar is not a regular file (`VerifyFailed`, message "The symlink sidecar is not a regular file."). On Linux, if this CPU has no known `O_NOFOLLOW` value, the sidecar is refused (`VerifyFailed`) instead of being opened with a following read. A failure to bind `libc.open` is `VerifyFailed` as well.

The link is recreated only when following its text one hop at a time stays inside the client. Each hop reads one link with `ResolveLinkTarget(false)` and resolves that text relative to the link's parent; symlinks in parent directories count too. The walk stops at 40 hops. The final target must exist and be a regular file inside the client. An absolute or relative target outside the client, a dangling link, or too many hops is not an error: Undo writes the original bytes back as a regular file. If creating the link fails, or the bytes read through it do not match `sha256_before`, Undo deletes only that link (never its target) and writes the original bytes as a regular file (`Restored`).

The manifest itself is refused with `VerifyFailed`, and nothing is changed, when:

- `files` is empty (Apply does not write a backup for an empty plan);
- an `existed: false` entry names a `backup_file`, `sha256_before` or `symlink_sha256`, or an `existed: true` entry lacks `backup_file` or `sha256_before`;
- two entries share a path (compared case-insensitively) or the same `backup_file`;
- a `files/<n>` or `files/<n>.symlink` in the backup folder is not referenced by an entry. That leftover payload means an entry was removed or flipped to `existed: false`.

Residual: an entry for a file the switch **created** (`existed: false`, no backup file) can be removed from the manifest without detection, because there is no payload left behind to notice. Undo then restores the other files and leaves that created file in place. It does not write or delete anything for the removed entry.

The backup is marked undone only when no file had a conflict; otherwise it stays pending and the next `Undo` reports the same conflicts instead of undoing something older.
Successive applies are undone one by one, newest first, as long as their backups are kept. No backup pending: `NothingToUndo`. Undo also refuses while the game runs.

## Game process probe

`GameProcessProbe` looks for a process named `Wow`, `Wow-64`, `WowB`, `WowT` or `WowClassic` (`.exe` ignored, so Wine works). It does not
compare folders, so a game running from another folder also blocks, which errs on the safe side.

## Retention

After a successful apply the newest `keepBackups` backups of the installation are kept (default 10, constructor parameter) and older
ones are deleted. The newest backup that is still pending undo is never deleted, and folders without a `manifest.json` are left alone.
Pruning is best effort and never fails an apply.
