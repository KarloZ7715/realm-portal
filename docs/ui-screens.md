# UI screens (F3b, simulated data)

The window keeps the system title bar. Its client area is 720x440, which is the compact 720x480 mockups (`figma-exports/compact-*.png`) without their drawn
title bar. From 960 px wide the sidebar shows labels and the non-affiliation note instead of icons only.

Everything here runs on **simulated data**: `Demo/DemoData.cs` (made-up servers, clients and `/home/demo/...` paths) and `Demo/DemoBackend.cs` (timed steps,
a pretend game process). No game client, no `~/.config/wow-servidores`, no network.

## Screens and states

| Screen / state | Mockup | How to reach it in the demo |
|---|---|---|
| Servers list | `compact-05-deshacer` (without the notice) | start |
| Filtered list | `compact-02-lista-filtrada` | type in the search box ("otro") |
| No results | `compact-02b-sin-resultados` | search "zzz"; "Limpiar búsqueda" clears it |
| Switching | `compact-04-cambiando` | card menu, "Solo cambiar" |
| Undo | `compact-05-deshacer` | right after a switch; the notice lasts 8 s |
| Opening the game | `compact-06-jugar` | "Jugar" on a compatible server |
| Game running | `compact-06b-jugando` | after opening; the simulated game closes by itself after 20 s, then "Reintentar" is enabled |
| Clients | `compact-01-clientes` | sidebar, second item; tick rows, "Elegir carpeta a mano" (adds an example folder), "Buscar de nuevo" |
| Add server | `compact-03-anadir` | "Añadir" in the servers header; "Probar conexión" answers unless the address contains "offline" or ends in ".invalid" |
| Settings | none | placeholder |

The language toggle in the status bar ("ES · v0.1.0") switches Spanish and English at run time. The first language follows the system language.

## Decisions that are not in the mockups

These are implemented as designed in `UX-DESIGN.md` section 2b ("Definido aquí") or are assumptions; they wait for the product owner's approval:

- Play is disabled for an incompatible server (the card shows the amber seal and badge).
- Play runs Realmlist, Account, Launch (3 steps, progress 25/60/100 %); "Solo cambiar" runs Copy, Realmlist, Account (3 steps, progress 20/55/85 %).
- While busy, the search box and "Añadir" are hidden and every "Jugar" is disabled; the card being worked on goes first, then the active server, then the saved order.
- The simulated game closes by itself after 20 s; when it does, the banner says so and "Reintentar" returns to the list.
- Undo restores the previous active server at once (no steps) and hides the notice.
- The card list marks the active server as selected (bronze corner brackets); clicking a card does not select it.
- The add-server form shows errors only after "Probar conexión" or "Guardar servidor" was pressed.
- The wide sidebar (960 px and up) and the clickable language toggle are not in the 720x480 mockups.
- The status bar text: idle shows `realmlist · <active host>`; right after a switch `Activo: <name>`.

## Art used without a Figma reference

See "Pieces to validate with the designer" in `assets/ART-GAPS.md`.
