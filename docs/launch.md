# Launching the game

`RealmPortal.Core.Launch` starts a client without a shell: a `GameLaunchSpec` is resolved into a program name plus an argument list.

```csharp
IGameLaunchService launcher = new ProcessGameLauncher();
launcher.Launch(new GameLaunchSpec(
    ExecutablePath: "/games/wow/Wow.exe",
    WorkingDirectory: "/games/wow",
    CommandTemplate: "wine {exe}",                     // optional
    Arguments: ["-windowed"],                          // optional
    Environment: new Dictionary<string, string?> { ["WINEPREFIX"] = "/home/me/.wine-wow", ["DISPLAY"] = null }, // null removes
    Prefix: ["gamemoderun"]));                         // optional
// runs: gamemoderun wine /games/wow/Wow.exe -windowed
```

Order: `Prefix` + (`CommandTemplate` or the executable) + `Arguments`.

Template rules: words are split on whitespace; single quotes are literal, double quotes group and honor `\"` and `\\`; a backslash outside quotes is
literal (Windows paths). `{exe}` (required) and `{dir}` are replaced **after** splitting, so paths with spaces stay one argument, and nothing
(`$VAR`, `;`, backticks, `$(...)`) is ever interpreted by a shell.

The simple `IGameLauncher.Launch(exe, dir, wrapper)` is kept and implemented on top of this (`wrapper` becomes the prefix).
Tests use a fake `IProcessRunner`, so no game is ever started.
