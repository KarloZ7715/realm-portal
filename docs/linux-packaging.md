# Linux packaging notes (for F6)

## Window icon on X11

`MainWindow` sets the `_NET_WM_ICON` property itself once it is open (`RealmPortal.App.Platform.X11WindowIcon`, Linux/X11 only, best effort).
It writes all the natively drawn sizes (16, 22, 24, 32, 48, 64, 128 and 256 px: micro up to 24 px, simplified at 32 px, full from 48 px) so the window
manager can pick the right one for the panel, the task switcher and Alt-Tab, instead of scaling the single 256 px `Window.Icon` bitmap that
Avalonia sets. The PNGs are bundled from `assets/branding/icon/png` as `Assets/Icons/realm-portal-<N>.png`. Under Wayland (XWayland) the window is
still an X11 window and works the same; on a native Wayland session or without `libX11.so.6` nothing is written and the Avalonia icon stays.
Checked on a real X server (Xvfb) with `xprop -len 1000000 -name "Realm Portal" _NET_WM_ICON`.

## Open points for the packages

- **`Exec=realm-portal` does not match the binary.** `packaging/linux/com.karloz7715.RealmPortal.desktop` starts `realm-portal`, which is the name of the
  CLI. The app's executable is `RealmPortal.App` (assembly name). The package must install a launcher called `realm-portal` for the app (a symlink or
  a wrapper), or the `.desktop` must use `Exec=RealmPortal.App`; the CLI would then need another name or live in another package.
- **No `StartupWMClass`.** The X11 `WM_CLASS` of the window is `RealmPortal.App` (res_class) when the app is started from its own executable (it
  is `dotnet` / `RealmPortal.App` when started with `dotnet RealmPortal.App.dll`). Docks match windows with their `.desktop` entry through that value,
  so add `StartupWMClass=RealmPortal.App` to the `.desktop` file.
- The `.desktop` icon name is `com.karloz7715.RealmPortal`; the hicolor icons under `packaging/linux/icons` already follow that name.
