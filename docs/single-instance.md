# Single instance (1.2.0)

Design for [#197](https://github.com/KarloZ7715/realm-portal/issues/197). Launching the desktop app while it is already
running does not open a second window: the new launch asks the running app to bring its window to the front and exits.
This is a UX guard only. Data safety between processes (app and CLI, or two apps) still comes from the config-directory
locks of [#39](https://github.com/KarloZ7715/realm-portal/issues/39); the CLI is not affected by this guard.

## Scope

- One app window **per config directory** (`PortalPaths.ConfigDirectory`, so `REALM_PORTAL_CONFIG_DIR` gives a
  separate instance; tests rely on this).
- `--demo` and `--gallery` (developer modes without real data) skip the guard.
- The CLI (`realm-portal`) never takes this lock and can run next to the app as before.

## How it works

Code: `src/RealmPortal.Core/Instances/` (no Avalonia dependency), wired in `Program.cs` through
`src/RealmPortal.App/Platform/AppInstance.cs`, and `InstanceActivation.cs` for the window. `Program.Run` keeps the
startup order fixed: crash log handlers ([#178](https://github.com/KarloZ7715/realm-portal/issues/178)) first, then the
instance claim, then the UI (`StartupTests` checks it).

1. **Lock.** At startup the app opens `<config dir>/.realm-portal-app.lock` with `FileShare.None` and does not wait.
   On Linux this is an `flock` on the open file; on Windows it is the file sharing lock. Both are released by the OS
   when the process ends, however it ends (crash, `kill -9`, power loss), so a leftover lock file never blocks a start.
   The file is `0600` and the config directory `0700` on Unix.
2. **Primary.** The process that gets the lock listens on a channel:
   - Linux: Unix domain socket `<config dir>/.realm-portal-app.sock`, mode `0600`. Whatever is left at that path is
     stale by definition (we hold the lock): a socket, a regular file or a symlink is unlinked (a symlink is removed
     itself; its target is never touched), and an empty directory is removed. A **non-empty directory** is left alone:
     the bind fails, the app runs without the channel and writes a line to stderr, and later launches open a second
     window after 3 s until that directory is removed by hand. On exit (window closed, or `SIGTERM`) the socket is
     removed before the lock is released.
   - Both ends check the other side on Linux with `SO_PEERCRED`: a primary only answers clients of the same user, and
     a second launch only talks to a primary of the same user.
   - Windows: named pipe `RealmPortal-app-<hash>`, where the hash covers the config directory (case-insensitive) and
     the user name. The pipe is created with `PipeOptions.CurrentUserOnly`, so other users can neither connect nor
     impersonate the server (the client also requires the server to belong to the current user).
3. **Second launch.** If the lock is taken, the new process connects, sends one request and waits for one reply, then
   exits with code 0.

Lock and socket handles are close-on-exec / not inheritable, so a game started from the app does not keep the lock
alive after the app exits.

### Long paths

`sun_path` holds 108 bytes on Linux and 104 on macOS, including the terminating NUL. Every candidate socket path must be
at most **100 UTF-8 bytes** (a margin on both); the first candidate that fits is used:

1. `<config dir>/.realm-portal-app.sock`
2. `$XDG_RUNTIME_DIR/realm-portal-app-<hash>.sock` (a per-user `0700` directory by spec), when `XDG_RUNTIME_DIR` is set
3. `<temp>/realm-portal-<user hash>/app-<hash>.sock`, where `<temp>` is `$TMPDIR` or `/tmp`

If none fits, there is no channel: the lock still works, and a second launch opens a second window after 3 s.
The hash is derived from the config directory, so different directories never share a socket.

The temp folder is shared with other users, and its name is predictable, so it is treated with care:

- The primary creates it `0700`, refuses a symlink, and sets the mode explicitly. Setting the mode only works for the
  owner, so a folder pre-created by another user makes the primary run without a channel.
- A second launch refuses to connect unless the folder is a real directory (not a symlink) with mode `0700`. A `0700`
  folder of another user cannot be entered at all. On Linux the peer credential check above also rules out a socket
  planted by another user, even if the folder is swapped after the mode check.

### Protocol

One UTF-8 JSON line each way, at most 4 KiB:

```text
request: {"protocol":1,"verb":"focus","args":[]}
reply:   {"protocol":1,"reply":"ok"}          (or "unsupported", "invalid")
```

The verb is generic (lowercase letters, digits and `-`, up to 32 characters, up to 16 string arguments) so a later
quick play (`--play`) can reuse the channel. Today the app handles `focus` only and answers `unsupported` to anything
else. Malformed, oversized (no newline within 4 KiB) or slow requests (no full line within 1 s) are answered `invalid`
or dropped; they never reach the handler and never stop the listener. Up to 8 connections are served at the same
time, so a few silent connections cannot use up a second launch's 3 s budget; more wait in the backlog for at most
1 s per slot.

## Timeouts and failure behavior

| Situation | Behavior |
|---|---|
| First instance crashed or was killed | The OS released the lock; the next launch becomes primary and replaces the stale socket. |
| Running instance answers | The new launch exits (code 0). |
| Running instance holds the lock but does not answer within 3 s (stopped with `SIGSTOP`, deadlocked, or its channel could not be opened) | The new launch **starts anyway** as a second window and writes a line to stderr. It does not listen; #39 keeps the data consistent. A hung window must not lock the user out. |
| The lock cannot be created (read-only or invalid config directory) | The app starts normally without the guard and writes a line to stderr. |
| The channel is refused (socket folder not private, primary or pipe owned by another user) | Like "does not answer": the new launch starts as a second window, without sending anything. |

The 3 s budget covers connecting (retried every 100 ms, because the primary takes the lock a moment before it binds),
sending and reading the reply. The listener runs on a background thread, so a busy UI thread does not make the primary
"unresponsive"; `ok` means the request was queued for the UI thread.

## Bringing the window to the front

On `focus`, the primary posts to the UI thread: show the window if it is hidden, restore it if minimized (back to
maximized if it was maximized before), then `Window.Activate()`. What `Activate()` does in Avalonia 11.3.22:

- **X11** (Linux Mint XFCE / xfwm4 and other EWMH window managers): sends a `_NET_ACTIVE_WINDOW` client message with
  source "application" and the timestamp of the last input the app received (or calls `XRaiseWindow` when the window
  manager does not support `_NET_ACTIVE_WINDOW`). Window managers with focus-stealing prevention may refuse an
  activation from a process that has had no recent input, and only mark the window as urgent (it flashes in the panel)
  instead of raising it. xfwm4 has this as the "Activate focus stealing prevention" option (Window Manager Tweaks →
  Focus); with it off (the default), the window is raised and focused.
- **Windows:** calls `SetForegroundWindow`. Windows only lets the foreground process hand over the foreground, so the
  second launch (which the user just started) first calls `AllowSetForegroundWindow` for the primary's process id
  (from `GetNamedPipeServerProcessId`). If Windows still refuses, the taskbar button flashes.
- **Wayland** (via XWayland, since Avalonia 11 has no native Wayland backend): compositors generally do not let an
  application raise itself without an activation token (`xdg-activation`), which Avalonia does not pass. Expect the
  window to be marked as demanding attention rather than raised. The guard itself (one window, second launch exits)
  works the same.

## Known limitations

- **Windows** is covered by CI (`windows-latest`) but has not been tried on a real desktop: focus with another app in
  the foreground, a minimized window, "End task" followed by a relaunch, and two user accounts on one machine.
- **Pipe name squatting (Windows):** the pipe name is predictable. Another local user could create it first. Because of
  `CurrentUserOnly` the victim never talks to that pipe, so the only effect is that the victim's second launches open
  second windows (no data reaches the other user). Not hardened further.
- **Focus** depends on the window manager: see above for focus-stealing prevention and Wayland.
- A primary whose UI thread is hung but whose listener runs answers `ok`, so the second launch exits and nothing
  visible happens.

## Testing

`tests/RealmPortal.Tests/SingleInstanceTests.cs` runs every case in its own temp directory: in-process claims, and real
child processes (the test assembly runs itself as a probe through `Support/ProbeProgram.cs`) for a primary in another
process, a second launch that exits, a primary killed without cleanup, and a child process that outlives a killed
primary. `tests/RealmPortal.App.Tests/InstanceActivationTests.cs` covers restore, show and activation with the headless
platform. Nothing touches the user's real config directory.

For a manual check, use a throwaway config directory so the real one is not used:

```bash
export REALM_PORTAL_CONFIG_DIR=$(mktemp -d) REALM_PORTAL_PROJECTION_DIR=$(mktemp -d) REALM_PORTAL_STATE_DIR=$(mktemp -d)
./RealmPortal.App &      # first window
./RealmPortal.App        # exits at once; the first window comes to the front
```
