# Writing a server into the game client

`RealmPortal.Core.ClientFiles` changes the game client's own files so that it connects to the selected server.
This page describes **what is written** (the planner). Backup, apply and undo are described in
a separate page once that part lands.

## Source of truth

The server's realmlist text is written as is; the app invents nothing. It is validated first and any violation fails with
`ClientFilesErrorCode.InvalidRealmlist`, with nothing written:

- ASCII only; every line is `set <key> <value>` (lowercase `set` and key, one space between the parts);
- keys: `realmlist` and `patchlist`, plus `portal` only for WoD/Legion clients; each key at most once;
- the first line is `set realmlist <host>` (contract with `activo.json`);
- values contain no quotes, spaces or control characters; no blank or other lines.

Lines may end with CR LF or LF in the input; the realm name (optional) must not contain quotes or control characters.

## Files per client (by major version)

Every path segment is matched case-insensitively against the disk (Linux is case-sensitive) and the real name is kept.

| Client | `realmlist.wtf` | `WTF/Config.wtf` |
|---|---|---|
| 1.12.1 (5875), 2.4.3 (8606) | root | only if it already exists, and only the `realmName` line; never created |
| 3.3.5a (12340), 4.3.4 (15595) | `Data/<locale>/` for every locale folder that exists (`^[a-z]{2}[A-Z]{2}$`) | `realmList`, `patchlist` (if in the text), `realmName` |
| 5.4.8 (18414) | not used | `realmList`, `patchlist` (if in the text), `realmName` |
| 6.x WoD, 7.x Legion | not used | `portal` (from `set portal`, else the `set realmlist` host), `realmName` |
| other versions | `UnsupportedClient` | |

Safety rules: an extra `realmlist.wtf` that **already exists** (root, or in any existing locale folder) is updated too, but new files are
only created at the main paths above. Locale folders are never created; nothing else is touched (`accountName`, `locale`, caches).
`WTF/` is created when `Config.wtf` has to be created; a new `Config.wtf` holds only managed keys.

## Content rules

- **`realmlist.wtf`**: exactly the validated text, one line per key, each line terminated. The existing file's line ending (CR LF or LF) and
  BOM are kept; a new file uses CR LF.
- **`Config.wtf`**: managed keys are `realmList`, `patchlist`, `realmName` and `portal`. For each managed key of the client the first line is
  updated in place (only the value, keeping the key's capitalization and spacing) and later duplicates are deleted; missing keys are appended
  as `SET realmList "host"`, `SET patchlist "value"`, `SET realmName "name"`, `SET portal "host"`. `realmName` is written when the server
  has a realm and **removed** when it has none, so another server's realm is not preselected. Every other line, the BOM and the line endings
  are preserved byte for byte (the file is handled as Latin-1; a non-ASCII realm name is written as UTF-8). A new file uses CR LF.
- A file whose new content equals the current one is left out of the plan; an empty plan means nothing to do.

The rule that decides which realmlist lines reach `realmlist.wtf` lives in one method (`ClientFileContent.BuildRealmlist`).

## Known open points

- A `patchlist` line missing from the server's text does not remove an existing `patchlist` line in `Config.wtf` (only `realmName` is removed).
- For WoD/Legion, `patchlist` from the text is not written (the clients have no patchlist).
