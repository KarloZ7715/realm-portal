# UI screens (`F3b`–`F4` & `1.0.0` flows)

The window keeps the system title bar. Its client area is 720×440, which is the compact 720×480 mockups (`figma-exports/compact-*.png`) without their drawn
title bar. From 960 px wide the sidebar shows labels and the non-affiliation note instead of icons only.

Everything in the `F3b`/`F4` prototype runs on **simulated data**: `Demo/DemoData.cs` (made-up servers, clients and `/home/demo/...` paths) and `Demo/DemoBackend.cs` (timed steps,
a pretend game process). For `1.0.0`, the UI screens and flows below connect to `IPortalService` while keeping the exact **Style B ("Pergamino y bronce")** visual language and WCAG 1.4.11 parchment button surface rules.

## Screens and states

| Screen / state | Mockup / Figma status | How to reach it |
|---|---|---|
| Servers list | `compact-05-deshacer` (without the notice) — *Exists in Figma* | start |
| Filtered list | `compact-02-lista-filtrada` — *Exists in Figma* | type in the search box ("otro") or filter by client in use |
| No results | `compact-02b-sin-resultados` — *Exists in Figma* | search "zzz"; "Limpiar búsqueda" clears it |
| Switching | `compact-04-cambiando` — *Exists in Figma* | card menu, "Solo cambiar" / "Switch only" |
| Undo | `compact-05-deshacer` — *Exists in Figma* | right after a switch; the notice stays until confirmed, undone, or superseded |
| Opening the game | `compact-06-jugar` — *Exists in Figma* | "Jugar" / "Play" on a compatible server |
| Game running | `compact-06b-jugando` — *Exists in Figma (extended for per-client concurrency)* | after opening; locks only the running client while other clients remain playable |
| Server `Play ▾` menu (with Edit / Delete) | `compact-play-menu@2x.png` / `compact-play-menu-disabled@2x.png` — *Exists in Figma (extended)* | chevron on a server card's `Play ▾` (`Jugar ▾`) SplitButton |
| Add / Edit server | `compact-03-add-server@2x.png` / `05-add-edit-server@2x.png` — *Exists in Figma* | "Añadir" in the servers header (Add mode) or "Editar servidor…" in `Play ▾` (Edit mode) |
| Confirm delete server | Reuses `07b-confirm-incompatible-switch@2x.png` modal — *New (1.0.0)* | "Eliminar servidor…" in `Play ▾` |
| Clients | `compact-01-clients@2x.png` — *Exists in Figma (extended with inline Edit / Remove)* | sidebar, second item; tick rows, "Elegir carpeta a mano", "Buscar de nuevo", inline `✎` / `✕` |
| Edit client & launcher overrides | Follows `compact-03` + `compact-07` card layout — *New (1.0.0)* | inline `✎` ("Editar cliente") button on a client row |
| Confirm remove client | Reuses `07b-confirm-incompatible-switch@2x.png` modal — *New (1.0.0)* | inline `✕` ("Quitar cliente") button on a client row |
| Settings | `compact-07-settings-no-cleanup@2x.png` & `compact-07-settings-no-cleanup-scrolled@2x.png` — *Exists in Figma (extended)* | sidebar, gear (third item) |
| Client backups manager (`BackupsView`) | Follows `compact-01` + `compact-07` sub-sheet layout — *New (1.0.0)* | "Gestionar respaldos…" / "Manage backups…" inside the `BACKUP` card in Settings |
| Confirm delete backup | Reuses `07b-confirm-incompatible-switch@2x.png` modal — *New (1.0.0)* | inline `✕` ("Eliminar respaldo") button on a backup row |
| Backup restore conflict (`Restore anyway`) | `compact-09-backup-conflict@2x.png` & `compact-09b-backup-conflict-restore-anyway@2x.png` — *Exists in Figma* | "Restaurar" / "Restore" on an older backup while a recent switch has a pending Undo |

The language toggle in the status bar ("ES · v1.0.0"; the version comes from `Directory.Build.props` through `AppInfo.Version`) switches Spanish and English at run time. The first language follows the system language.

## Decisions that are not in the mockups

These are implemented as designed in `UX-DESIGN.md` section 2b ("Definido aquí") or approved for `1.0.0`:

- Play is disabled for an incompatible server (the card shows the amber seal and badge).
- Play runs Realmlist, Account, Launch (3 steps, progress 25/60/100 %); "Solo cambiar" runs Copy, Realmlist, Account (3 steps, progress 20/55/85 %).
- While busy switching/launching, the search box and "Añadir" are hidden and every "Jugar" is disabled; the card being worked on goes first, then the active server, then the saved order.
- Undo restores the previous active server at once (no steps) and hides the notice.
- The card list marks the active server as selected (bronze corner brackets); clicking a card does not select it.
- The add/edit-server form shows errors only after "Probar conexión" or "Guardar servidor" / "Guardar cambios" was pressed.
- The wide sidebar (960 px and up) and the clickable language toggle are not in the 720×480 mockups.
- The status bar text: idle shows `realmlist · <active host>`; right after a switch `Activo: <name>`.

---

## 1.0.0 UI/UX Specification (Step 7 Deliverables)

### Style B & WCAG 1.4.11 Surface Invariants (Mandatory Across All 1.0.0 Views)

1. **Parchment Button Surface Rule (WCAG 1.4.11 Non-Text Contrast):**
   - Every `Button` (`primary`, `secondary`, `ghost`) and `SplitButton` placed on a parchment card, sheet, or modal (`ParchmentPanel` `Variant="Tile"` or `Variant="Thin"`) **must** set `c:ButtonSurface.Surface="Parchment"`.
   - This applies `button-{primary,secondary}-parchment-*` with the `#5A3D22` (`1.5 px`) inner border (`5.55:1` contrast against `#D8BF92` parchment) and `focus-ring-parchment` (`#0F5561` inner ring + `#120C08` outer ring).
   - Buttons placed directly on the dark leather background (`#120C08`..`#25190F`, such as the Servers header `+ Add`, Clients header `Pick a folder by hand`, and Settings footer `Cancel` / `Save`) keep the default `Leather` surface (`c:ButtonSurface.Surface="Leather"`).
2. **9-Slice Width Snapping:**
   - Plain `Button.primary` / `Button.secondary`: snap `Width` or `MinWidth` to `160 + 6k` px (`130`, `148`, `166`, `172`, `208`, `214`) so the 6 px middle tile repeats cleanly.
   - `SplitButton`: snap `Width` to `176` (`200 - 4×6`) or `200` px.
3. **Icon Reuse:**
   - All delete/remove actions reuse the existing `IconClose` (`✕`, `close.svg`) and edit actions reuse `IconPencil` (`✎`, `pencil.svg`) from `Themes/Icons.axaml`. No new art assets are required.

---

### Deliverable 1 — Server Card Edit & Delete UX (Req #2)

#### 1.1 `Play ▾` (`Jugar ▾`) SplitButton Flyout Menu (`ServersView.axaml`)
- **Figma reference:** `compact-play-menu@2x.png` (`49:5882`) and `compact-play-menu-disabled@2x.png` (`49:6127`).
- **Placement (Approved):** Inside the server card's existing `SplitButton.Flyout` (`MenuFlyout` with `FlyoutPresenterClasses="parchment"`), below a `<Separator />`.
- **Menu structure:**

```
+---------------------------------------------------+
| ▶  Play (Jugar)                                   |
| ↻  Switch only (Solo cambiar) / Repair (Reparar)  |
| ------------------------------------------------- |
| ✎  Edit server… (Editar servidor…)                |
| ✕  Delete server… (Eliminar servidor…)            |
| ------------------------------------------------- |
|    (Disabled reason hint when client is running)  |
+---------------------------------------------------+
```

- **Items & states:**
  1. **`Play` (`Jugar`):** `IconPlay`, `Command="{Binding PlayCommand}"`, `IsEnabled="{Binding CanPlay}"`.
  2. **`Switch only` (`Solo cambiar`) or `Repair` (`Reparar`):** `IconRefresh`, `Command="{Binding SwitchOnlyCommand}"`, `IsEnabled="{Binding CanSwitchOnly}"`.
  3. **`<Separator />`:** 1 px `#9A7444` (`BorderDefault`) horizontal divider.
  4. **`Edit server…` (`Editar servidor…`):** `IconPencil`, `Command="{Binding EditCommand}"`, `IsEnabled="{Binding CanEdit}"`.
  5. **`Delete server…` (`Eliminar servidor…`):** `IconClose`, `Command="{Binding DeleteCommand}"`, `IsEnabled="{Binding CanDelete}"`.
  6. **Disabled-reason caption (`IsVisible="{Binding ShowMenuDisabledHint}"`, `IsEnabled="False"`):**
     - When the server's resolved target client is running a game session: `menu.disabled.clientrunning` (`"Close the game on {0} to switch or edit"` / `"Cierra el juego en {0} para cambiar o editar"`).
     - When no compatible client is marked `"In use"`: `menu.disabled.noinuse` (`"Mark a compatible client as 'In use' in Clients"` / `"Marca un cliente compatible como «En uso» en Clientes"`).

#### 1.2 Add / Edit Server Sheet (`AddServerView.axaml` & `AddServerViewModel.cs`)
- **Figma reference:** `05-add-edit-server@2x.png` (`20:1194`) and `compact-03-add-server@2x.png` (`42:4972`).
- **Modes:**
  - **Add mode (`EditingServerId == null`):** Header title `Add server` (`Añadir servidor`); primary button `Save server` (`Guardar servidor`). Calls `IPortalService.AddServer(AddServerRequest)`.
  - **Edit mode (`EditingServerId != null`):** Header title `Edit server` (`Editar servidor`) + subtitle `Update the connection details for this server` (`Actualiza los datos de conexión de este servidor`); pre-fills `Name`, `Host` (`Address`), `Realm`, `SelectedClient` (pinned `ClientId` or `"Automatic (match version)"`), and `Account` (`DefaultAccount`), preserving `PatchlistHost` if imported from `Config.wtf`; primary button `Save changes` (`Guardar cambios`). Calls `IPortalService.UpdateServer(UpdateServerRequest)`.
- **Form fields (inside `ParchmentPanel` `Variant="Tile"`, all buttons with `c:ButtonSurface.Surface="Parchment"`):**
  - `Name` (`Nombre`): required `TextBox.parchment`.
  - `Address (host or host:port)` (`Dirección (host o host:puerto)`): required `TextBox.parchment` (`FontMono`). Validated via `HostValidator.Validate(host)`. On `ServiceErrorCode.ServerHostTaken` (duplicate normalized `host[:port]` + `realm`), shows inline error `add.error.duplicate`.
  - Two-column row:
    - Left (`*`): `Client` (`Cliente`) `ComboBox.parchment` (`"Automatic (match version)"` + saved clients `"{DisplayName} · {Version}"`).
    - Right (`*`): `Realm (optional)` (`Reino (opcional)`) `TextBox.parchment` (watermark `"e.g. Icecrown"` / `"p. ej. Icecrown"`), mapped to `SET realmName` in `WTF/Config.wtf`.
  - `Account (optional)` (`Cuenta (opcional)`): `TextBox.parchment`, mapped to `SET accountName`.
  - Footer buttons: `Test connection` (`Probar conexión`, `Button.secondary`, `MinWidth="166"`), `Cancel` (`Cancelar`, `Button.secondary`, `MinWidth="130"`), and `Save server` / `Save changes` (`Button.primary`, `MinWidth="166"`).

#### 1.3 Confirm Delete Server Dialog (`ConfirmDialogView.axaml`)
- **Figma reference:** Reuses `07b-confirm-incompatible-switch@2x.png` (`21:1846`) parchment modal template.
- **Behavior (Approved):** Deleting a server removes it from `servers.json`. If the deleted server is currently marked active (`IsActive == true`), `activo.json` is cleared while the client's `realmlist.wtf` on disk is **left intact** until the user switches to another server.
- **Content:**
  - **Title:** `IconWarning` + `Delete this server?` (`¿Eliminar este servidor?`) — `confirm.deleteserver.title`.
  - **Body:**
    - Non-active server: `confirm.deleteserver.body` (`"“{0}” ({1}) will be removed from your server list."` / `"Se eliminará «{0}» ({1}) de tu lista de servidores."`).
    - Active server (`IsActive == true`): `confirm.deleteserver.activebody` (`"“{0}” ({1}) will be removed. It is currently marked as active; your client's realmlist.wtf will stay as-is until you switch to another server."` / `"Se eliminará «{0}» ({1}). Actualmente figura como activo; el archivo realmlist.wtf de tu cliente no cambiará hasta que elijas otro servidor."`).
  - **Buttons (`c:ButtonSurface.Surface="Parchment"`):**
    - Primary: `Delete server` (`Eliminar servidor`, `confirm.deleteserver.accept`, `Button.primary`, `MinWidth="166"`).
    - Cancel: `Cancel` (`Cancelar`, `confirm.cancel`, `Button.secondary`, `MinWidth="130"`, `IsCancel="True"`, **initial keyboard focus**).

---

### Deliverable 2 — Client List CRUD, Per-Client Launcher Overrides & Scan Summary UX (Req #1 & Req #2)

#### 2.1 First-Launch Auto-Scan & Manual Rescan Summary (`Discover`)
- **Figma reference:** `02-detection-found@2x.png`, `02b-detection-searching@2x.png`, `02c-detection-none@2x.png`, and `compact-01-clients@2x.png`.
- **Backend contract:** `IPortalService.Discover(bool importServers = true)` → `ServiceResult<DiscoveryOutcome>` (`AddedClients`, `TotalClients`, `ImportedServers`, `SkippedClients`).
- **First-launch auto-scan:** Runs automatically on startup when `clients.json` is empty (`!uiPreferences.FirstScanCompleted`).
  - While scanning: status bar shows `StatusKind.Busy` (`#D8B168` dot) + `status.scanning`.
  - When completed (`FirstScanCompleted = true`):
    - If clients and servers were discovered: shows notice/status `scan.outcome.found` (`"Found {0} client(s) and imported {1} server(s)."` / `"Se encontraron {0} cliente(s) y se importaron {1} servidor(es)."`).
    - If only clients were discovered (`ImportedServers.Count == 0`): shows `scan.outcome.clientsonly` (`"Found {0} client(s). Add a server to start playing."` / `"Se encontraron {0} cliente(s). Añade un servidor para empezar a jugar."`).
    - If nothing was found (`TotalClients == 0`): navigates to `ClientsView` showing `clients.empty` (`02c`).
- **Manual Rescan (`Rescan` / `Buscar de nuevo` button on `ClientsView`):**
  - Calls `Discover(importServers: true)`, refreshes `ClientsViewModel` and `ServersViewModel`, and updates the status bar with `scan.outcome.found` or `scan.outcome.nochange` (`"Scan complete: no new clients or servers found."` / `"Búsqueda completada: no se encontraron clientes ni servidores nuevos."`).

#### 2.2 Saved Clients List with Inline Edit & Remove (`ClientsView.axaml`)
- **Figma reference:** `compact-01-clients@2x.png` (`41:4639`), extended with inline `✎` (`IconPencil`) and `✕` (`IconClose`) parchment icon buttons on each row.
- **Row layout (`ParchmentPanel` `Variant="Thin"`, `MinHeight="64"`, `InnerPadding="12,8"`):**

```
+-----------------------------------------------------------------------+
| [✓]  WotLK 3.3.5a (ES) · 3.3.5a (build 12340)    [LUTRIS]  [✎]  [✕]   |
|      /home/user/Games/wow-wotlk · Custom launcher                     |
+-----------------------------------------------------------------------+
```

- **Columns:**
  - **Col 0 (`Auto`):** `CheckBox` (`IsInUse`).
  - **Col 1 (`*`, `Margin="10,0"`):**
    - Line 1: Custom `Name` (`Cinzel` 14 px Bold `#23170B`) + `· {Version} (build {Build})` (`Source Sans 3` 12 px `#5A3D22`), or `Client {Version} (build {Build})` when `Name` is empty.
    - Line 2: `Path` (`JetBrains Mono` 11.5 px `#5A3D22`, `TextTrimming="CharacterEllipsis"`). If the client has any non-empty per-client launcher override, appends `· Custom launcher` (`· Lanzador propio`, `clients.item.customlauncher`) in `#6E4B1F`.
  - **Col 2 (`Auto`, `Margin="8,0"`):**
    - Shows `StatusBadge` (`Status="Active"`, `badge.running`: `"Running"` / `"En ejecución"`) when a game session is active on this client; otherwise shows the origin pill (`LUTRIS`, `WINE`, `STEAM`, `MANUAL`).
  - **Col 3 (`Auto`, inline actions on parchment):**
    - **`Edit client` (`Editar cliente`):** `Button.ghost.surface-parchment` (`Width="28"`, `Height="28"`, `Padding="6"`, `c:ButtonSurface.Surface="Parchment"`), icon `IconPencil` (`14×14`, `#3E2813`), tooltip `clients.action.edit`. Disabled while this client is running.
    - **`Remove client` (`Quitar cliente`):** `Button.ghost.surface-parchment` (`Width="28"`, `Height="28"`, `Padding="6"`, `Margin="4,0,0,0"`, `c:ButtonSurface.Surface="Parchment"`), icon `IconClose` (`14×14`, `#7A1F19`), tooltip `clients.action.remove`. Disabled while this client is running.

#### 2.3 Edit Client & Per-Client Launcher Overrides Sheet (`EditClientView.axaml` / `EditClientViewModel.cs`)
- **Placement (Approved):** Clicking `✎` on a client row opens the `EditClientView` sheet in the main content area (with `← Back` / `← Volver` header navigation returning to `ClientsView`).
- **Backend contract:** `IPortalService.UpdateClient(UpdateClientRequest)` (`ClientId`, `Name`, `Path`, `IsInUse`, and optional per-client `LauncherOverrides`: `CommandTemplate`, `Prefix`, `Arguments`, `Environment`).
- **Sheet sections (inside a `ParchmentPanel` `Variant="Tile"`, all buttons with `c:ButtonSurface.Surface="Parchment"`):**
  1. **Client identity & folder:**
     - `Display name (optional)` (`Nombre visible (opcional)`): `TextBox.parchment` (`MaxLength="64"`, watermark `edit.client.name.watermark`).
     - `Installation folder` (`Carpeta de instalación`): `TextBox.parchment` (`FontMono`) + `Browse…` (`Examinar…`, `Button.secondary.compact`, `c:ButtonSurface.Surface="Parchment"`). Re-validates the directory (`WoW.exe` + `Data/`) and checks `ClientAlreadyExists` (`edit.client.error.duplicate`) on save.
     - `In use`: `CheckBox` with label `edit.client.inuse`.
  2. **Divider:** `<Rectangle Classes="stitch parchment" Margin="0,6" />`
  3. **`LAUNCHER FOR THIS CLIENT (OPTIONAL)` (`LANZADOR PARA ESTE CLIENTE (OPCIONAL)`):**
     - Caption (`edit.client.launcher.hint`): `"Leave fields blank to use the global launcher from Settings. Pre-filled automatically for Lutris/Wine clients."`
     - 2×2 grid matching the Settings `LAUNCHER` card (`Template`, `Prefix`, `Arguments`, `Environment` e.g. `WINEPREFIX=/path/to/prefix`), using the same `{exe}` and `KEY=value` validation rules.
     - `Use global launcher` (`Usar lanzador global`, `edit.client.launcher.reset`, `Button.ghost.surface-parchment`): clears the 4 override fields.
  4. **Footer row (inside parchment card, right-aligned):**
     - `Cancel` (`Cancelar`, `Button.secondary`, `c:ButtonSurface.Surface="Parchment"`, `MinWidth="130"`).
     - `Save` (`Guardar`, `Button.primary`, `c:ButtonSurface.Surface="Parchment"`, `MinWidth="148"`, `IsDefault="True"`).

#### 2.4 Confirm Remove Client Dialog (`ConfirmDialogView.axaml`)
- **Behavior (approved by the maintainer):** Removing a client deletes its entry from `clients.json`, leaves the game installation on disk untouched, and **silently preserves `<backupRoot>/<clientId>` on disk** (`deleteBackups: false`). There is **no** `"Also delete backups"` checkbox in this dialog.
- **Content:**
  - **Title:** `IconWarning` + `Remove this client?` (`¿Quitar este cliente?`) — `confirm.deleteclient.title`.
  - **Body:**
    - Standard: `confirm.deleteclient.body` (`"“{0}” ({1}) will be removed from Realm Portal. Your game files on disk will not be touched."`).
    - When one or more servers are linked to `ClientId`: `confirm.deleteclient.linkedbody` (`"“{0}” ({1}) will be removed from Realm Portal. Your game files on disk will not be touched. Servers linked to this client will stay in your list."`).
  - **Buttons (`c:ButtonSurface.Surface="Parchment"`):**
    - Primary: `Remove client` (`Quitar cliente`, `confirm.deleteclient.accept`, `Button.primary`, `MinWidth="166"`).
    - Cancel: `Cancel` (`Cancelar`, `confirm.cancel`, `Button.secondary`, `MinWidth="130"`, `IsCancel="True"`, **initial keyboard focus**).

---

### Deliverable 3 — Multi-Client Selector & Per-Client Concurrent Running State UX (Req #4 & Req #6)

- **Backend contract:** `IPortalService.ListRunningSessions()` returns `IReadOnlyList<RunningSessionInfo>` (`ClientId`, `ServerId`, `ProcessId`, `StartedAt`).
  - A single client (`ClientId`) can run at most **one** game session at a time (`ServiceErrorCode.GameRunning`).
  - Two **different** clients (`ClientA != ClientB`) can run simultaneously on the same or different servers.

#### 3.1 Target Client Indicator & Selector on `ServersView`
1. **Per-Card Target Client Label:**
   - Each server card's metadata line (`Version · Realm · Account`) appends `· Client: {ClientDisplayName}` (`servers.card.client`) whenever ≥ 2 clients are saved.
2. **Header Client Filter / Target Selector (shown when ≥ 2 clients are marked `"In use"`):**
   - Placed next to the search box in the `ServersView` toolbar as a compact `ComboBox.parchment` (`Width="190"`, `Height="36"`):
     - Default item: `All clients in use` (`Todos los clientes en uso`, `servers.clientfilter.all`).
     - Specific `"In use"` clients: `{DisplayName} ({Version})`.
   - Selecting a specific client filters the visible list to servers compatible with that client and uses that client as the target when launching an unpinned server.

#### 3.2 Per-Client Running State Matrix (`ShowServerList` Stays Visible)
- **Key rule for `ServersView`:** Unlike the single-client demo where `ShowServerList` hid all cards during `LaunchPhase.Running`, in `1.0.0` **`ShowServerList` remains `true` while games are running**. The active session(s) are reported in the compact top `PhaseBanner` and per-card `Running` (`En ejecución`) badge so idle clients remain playable.

| Running State | Top `PhaseBanner` on `ServersView` | Server Card Targeting **Running Client A** | Server Card Targeting **Idle Client B** | Bottom Status Bar |
|---|---|---|---|---|
| **0 clients running** | Hidden (or Undo notice if `PendingUndo != null`) | `CanPlay = true`, `CanSwitchOnly = true`, `CanEdit = true`, `CanDelete = true` | `CanPlay = true`, `CanSwitchOnly = true`, `CanEdit = true`, `CanDelete = true` | `realmlist · <active host>` |
| **1 client (`Client A`) running on `Server X`** | Compact non-blocking `PhaseBanner` (`IconPlay` gold): `phase.running.client` (`"Playing {Server X} on {Client A} (PID {Pid})"`) | **`Server X`:** Badge = `Running` (`SealStatus.Active`). **All cards targeting `Client A`:** `CanPlay = false`, `CanSwitchOnly = false`, `CanEdit = false`, `CanDelete = false` (menu hint: `menu.disabled.clientrunning`). | **Remains fully enabled** (`CanPlay = true`, `CanSwitchOnly = true`, `CanEdit = true`, `CanDelete = true`) — user can switch and launch `Client B` concurrently. | `StatusKind.Ok`: `"Playing {Server X} on {Client A}"` |
| **N ≥ 2 clients running (`Client A` + `Client B`)** | Compact `PhaseBanner`: `phase.running.multiple` (`"{0} games running: {1}"`) | Locked for `Client A` & `Client B` | Enabled for any idle `Client C` | `StatusKind.Ok`: `"{0} games running"` |

---

### Deliverable 4 — Settings Backup Retention, Backups Manager Sub-Sheet & "Restore Anyway" Dialog (Req #7)

#### 4.1 Settings Screen (`SettingsView.axaml` — `compact-07`)
- **Figma reference:** `compact-07-settings-no-cleanup@2x.png` (`48:5882`) and `compact-07-settings-no-cleanup-scrolled@2x.png` (`48:6144`).
-Reached from the sidebar gear. The three `ParchmentPanel` (`Variant="Tile"`) cards (`LANGUAGE`, `LAUNCHER`, `BACKUP`) sit in one column and scroll; the header (`Back`) and the footer (`Cancel`, `Save`) stay fixed.
- **`BACKUP` (`COPIA DE SEGURIDAD`) Card Extensions for `1.0.0`:**
  1. **Row 1 — `Folder` (`Carpeta`):** `TextBox.parchment` (`FontMono`, bound to `BackupFolder`) + `Browse…` (`Examinar…`, `Button.secondary.compact`, `c:ButtonSurface.Surface="Parchment"`).
  2. **Row 2 — Retention & Sub-Sheet Trigger (`Margin="0,8,0,0"`):**
     - Left (`*`): Label `Keep backups per client` (`Respaldos a conservar por cliente`, `settings.backup.keep`) + compact numeric `TextBox.parchment` (`Width="72"`, range `1..50`, default `10`, bound to `KeepBackups`). Invalid values outside `1..50` show inline error `settings.backup.error.keep` and block `Save`.
     - Right (`Auto`): **`Manage backups…`** (`Gestionar respaldos…`, `settings.backup.manage`) `Button.secondary.compact` (`c:ButtonSurface.Surface="Parchment"`, `MinWidth="172"`, `Command="{Binding OpenBackupsManagerCommand}"`).

#### 4.2 Client Backups Manager Sub-Sheet (`BackupsView.axaml` / `BackupsViewModel.cs`)
- **Placement (Approved):** Dedicated sub-sheet opened from the **`Manage backups…`** (`Gestionar respaldos…`) button inside the `BACKUP` card in `SettingsView`. Clicking `← Back` (`← Volver`) in the sub-sheet header returns to `SettingsView`.
- **Backend contract:** `ListBackups(clientId)`, `CreateBackup(clientId)`, `RestoreBackup(clientId, backupId, force)`, `DeleteBackup(clientId, backupId)`.
- **Sub-sheet layout:**

```
+--------------------------------------------------------------------------+
| [← Back]  Client backups                                                 |
|           Snapshots of realmlist.wtf and WTF/Config.wtf                  |
+--------------------------------------------------------------------------+
|  PARCHMENT SHEET (panel-tile)                                            |
|  Client: [ WotLK 3.3.5a (ES) · 3.3.5a (12340) ▾ ]    [ + Create backup ] |
|  ......................................................................  |
|  SAVED BACKUPS (3 of 10 max)                                             |
|  +--------------------------------------------------------------------+  |
|  | 2026-10-18 01:42 · Before switching to My Server  [Pending undo]   |  |
|  | 2 files (realmlist.wtf, Config.wtf) · 1.8 KB      [Restore]  [✕]   |  |
|  +--------------------------------------------------------------------+  |
|  | 2026-10-17 19:15 · Manual backup                                   |  |
|  | 2 files (realmlist.wtf, Config.wtf) · 1.8 KB      [Restore]  [✕]   |  |
|  +--------------------------------------------------------------------+  |
+--------------------------------------------------------------------------+
```

- **Controls & row anatomy (all buttons inside the sheet set `c:ButtonSurface.Surface="Parchment"`):**
  - **Toolbar row:**
    - Left (`*`): `Client` label (`backups.client`) + `ComboBox.parchment` listing saved clients (`"{DisplayName} · {Version}"`).
    - Right (`Auto`): **`Create backup`** (`Crear respaldo`, `backups.create`) `Button.primary` (`c:ButtonSurface.Surface="Parchment"`, `MinWidth="166"`, `IconPlus`). Disabled while the selected client is running.
      - When `CreateBackup` returns `WasDeduplicated == true`, displays status `backups.status.deduplicated` (`"Backup already matches current client files ({0})."`).
      - When a new snapshot is written, prepends it and displays `backups.status.created` (`"Backup created ({0})."`).
  - **Subheader:** `SAVED BACKUPS` (`RESPALDOS GUARDADOS`, `backups.saved`) + counter `{0} of {1} max` (`{0} de {1} máx.`, `backups.count`).
  - **Empty state:** `Border.notice` on parchment displaying `backups.empty`.
  - **Backup row (`ParchmentPanel` `Variant="Thin"`, `MinHeight="60"`, `InnerPadding="12,8"`):**
    - **Col 0 (`*`):**
      - Line 1: Local timestamp `yyyy-MM-dd HH:mm` (`Cinzel` 13.5 px Bold `#23170B`) + `·` + label (`backups.item.server`: `"Before switching to {0}"` when `ServerName != null`, or `backups.item.manual`: `"Manual backup"`).
      - Line 2: `backups.item.meta` (`"{0} files ({1}) · {2}"`, e.g., `"2 files (realmlist.wtf, Config.wtf) · 1.8 KB"`). If `Error != null`, displays `backups.item.corrupt` in `#7A1F19`.
    - **Col 1 (`Auto`, badge):**
      - `Pending undo` (`Cambio pendiente`, `Status="Compat"`) when `Id == PendingUndo.BackupId`.
      - `Restored` (`Restaurado`, `Status="Active"`) when `RestoredAt != null`.
      - `Damaged` (`Dañado`, `Status="Incompat"`) when `Error != null`.
    - **Col 2 (`Auto`, actions on parchment):**
      - **`Restore` (`Restaurar`, `backups.action.restore`):** `Button.secondary.compact` (`c:ButtonSurface.Surface="Parchment"`, `MinWidth="112"`, `IconUndo`). Disabled while the client is running or when `Error != null`.
      - **`Delete` (`Eliminar respaldo`, `backups.action.delete`):** `Button.ghost.surface-parchment` (`Width="28"`, `Height="28"`, `Padding="6"`, `Margin="6,0,0,0"`, `c:ButtonSurface.Surface="Parchment"`, `IconClose` in `#7A1F19`). Disabled while the client is running. Clicking opens the 2-button **Confirm Delete Backup Dialog** (`confirm.deletebackup.*`, Cancel focused first).

#### 4.3 3-Button Backup Restore Conflict Dialog (`compact-09` & `compact-09b`)
- **Figma reference:** `compact-09-backup-conflict@2x.png` (`55:5645`) and `compact-09b-backup-conflict-restore-anyway@2x.png` (`56:5887`).
- **Trigger:** The user clicks **`Restore`** on a backup (or **`Undo`**) when the client's managed files on disk differ from the snapshot (`PendingUndo != null` or manual edits), causing `RestoreBackup(clientId, backupId, force: false)` to return `ServiceErrorCode.UndoConflict` (with `ConflictingFiles` and `Consistency.Pending`).
- **Service error mapping:** The canonical enum member in `RealmPortal.Core.Service.ServiceErrorCode` is `ServiceErrorCode.UndoConflict` (used by both `Undo()` and non-forced `RestoreBackup(clientId, backupId, force: false)`); there is no separate `BackupConflict` enum value, and `UndoConflict` maps directly to the `confirm.backupconflict.*` dialog strings below.
- **3-Button Parchment Modal Layout (`ConfirmDialogView.axaml` extended with optional `SecondaryActionText` / `SecondaryActionCommand`):**
  - **Title:** `IconWarning` (`#8C5214`) + `A recent change hasn't been confirmed` (`Hay un cambio reciente sin confirmar`, `confirm.backupconflict.title`).
  - **Body (`confirm.backupconflict.body`):**
    `"You switched to “{0}” at {1} and the Undo option is still active. Restoring this older backup ({2}) will replace the current client files. A safety backup of your current files will be saved first."`
  - **Footer buttons (right-aligned, `Spacing="8"`, all with `c:ButtonSurface.Surface="Parchment"` — `#5A3D22` 1.5 px inner border and `#0F5561` + `#120C08` focus ring):**
    1. **`Restore anyway`** (`Restaurar de todos modos`, `confirm.backupconflict.restoreanyway`): `Button.primary` (`MinWidth="172"`). Calls `RestoreBackup(clientId, backupId, force: true)` (saves a pre-restore safety snapshot, restores the chosen backup, and clears `PendingUndo`).
    2. **`Discard`** (`Descartar`, `confirm.backupconflict.discard`): `Button.secondary` (`MinWidth="130"`). Calls `DiscardUndo()` (clears the pending Undo state without altering the client files on disk) and closes the dialog.
    3. **`Cancel`** (`Cancelar`, `confirm.cancel`): `Button.secondary` (`MinWidth="130"`, `IsCancel="True"`, **initial keyboard focus**). Closes the dialog with no changes.

---

## Localization Keys Table (`en.json` & `es.json`)

All keys below are specified for `src/RealmPortal.App/Localization/Strings/en.json` and `es.json` across the `1.0.0` views, menus, and dialogs:

| Key | English (`en.json`) | Spanish (`es.json`) |
|---|---|---|
| `servers.clientfilter.all` | `All clients in use` | `Todos los clientes en uso` |
| `servers.card.client` | `Client: {0}` | `Cliente: {0}` |
| `servers.empty.scan` | `Scan for clients & servers` | `Buscar clientes y servidores` |
| `menu.play` | `Play` | `Jugar` |
| `menu.switch` | `Switch only` | `Solo cambiar` |
| `menu.repair` | `Repair` | `Reparar` |
| `menu.edit` | `Edit server…` | `Editar servidor…` |
| `menu.delete` | `Delete server…` | `Eliminar servidor…` |
| `menu.disabled.running` | `Unavailable while the game is running` | `No disponible con el juego en marcha` |
| `menu.disabled.clientrunning` | `Close the game on {0} to switch or edit` | `Cierra el juego en {0} para cambiar o editar` |
| `menu.disabled.noinuse` | `Mark a compatible client as 'In use' in Clients` | `Marca un cliente compatible como «En uso» en Clientes` |
| `phase.running.title` | `Game is open on {0}` | `El juego está abierto en {0}` |
| `phase.running.client` | `Playing {0} on {1} (PID {2})` | `Jugando a {0} en {1} (PID {2})` |
| `phase.running.multiple` | `{0} games running: {1}` | `{0} juegos en ejecución: {1}` |
| `add.title` | `Add server` | `Añadir servidor` |
| `edit.server.title` | `Edit server` | `Editar servidor` |
| `edit.server.subtitle` | `Update the connection details for this server` | `Actualiza los datos de conexión de este servidor` |
| `add.realm` | `Realm (optional)` | `Reino (opcional)` |
| `add.realm.watermark` | `e.g. Icecrown` | `p. ej. Icecrown` |
| `add.client.auto` | `Automatic (match version)` | `Automático (según versión)` |
| `add.save` | `Save server` | `Guardar servidor` |
| `edit.server.save` | `Save changes` | `Guardar cambios` |
| `add.error.duplicate` | `A server with this address and realm already exists.` | `Ya existe un servidor con esta dirección y reino.` |
| `confirm.deleteserver.title` | `Delete this server?` | `¿Eliminar este servidor?` |
| `confirm.deleteserver.body` | `“{0}” ({1}) will be removed from your server list.` | `Se eliminará «{0}» ({1}) de tu lista de servidores.` |
| `confirm.deleteserver.activebody` | `“{0}” ({1}) will be removed. It is currently marked as active; your client's realmlist.wtf will stay as-is until you switch to another server.` | `Se eliminará «{0}» ({1}). Actualmente figura como activo; el archivo realmlist.wtf de tu cliente no cambiará hasta que elijas otro servidor.` |
| `confirm.deleteserver.accept` | `Delete server` | `Eliminar servidor` |
| `clients.item.customlauncher` | `Custom launcher` | `Lanzador propio` |
| `clients.action.edit` | `Edit client` | `Editar cliente` |
| `clients.action.remove` | `Remove client` | `Quitar cliente` |
| `edit.client.title` | `Edit client` | `Editar cliente` |
| `edit.client.name` | `Display name (optional)` | `Nombre visible (opcional)` |
| `edit.client.name.watermark` | `e.g. WotLK 3.3.5a HD` | `p. ej. WotLK 3.3.5a HD` |
| `edit.client.folder` | `Installation folder` | `Carpeta de instalación` |
| `edit.client.inuse` | `Available to launch compatible servers` | `Disponible para iniciar servidores compatibles` |
| `edit.client.launcher.title` | `LAUNCHER FOR THIS CLIENT (OPTIONAL)` | `LANZADOR PARA ESTE CLIENTE (OPCIONAL)` |
| `edit.client.launcher.hint` | `Leave fields blank to use the global launcher from Settings. Pre-filled automatically for Lutris/Wine clients.` | `Deja los campos vacíos para usar el lanzador global de Ajustes. Se rellena solo en clientes de Lutris/Wine.` |
| `edit.client.launcher.reset` | `Use global launcher` | `Usar lanzador global` |
| `edit.client.error.duplicate` | `That folder is already registered in another client.` | `Esa carpeta ya está registrada en otro cliente.` |
| `confirm.deleteclient.title` | `Remove this client?` | `¿Quitar este cliente?` |
| `confirm.deleteclient.body` | `“{0}” ({1}) will be removed from Realm Portal. Your game files on disk will not be touched.` | `Se quitará «{0}» ({1}) de Realm Portal. Los archivos del juego en el disco no se tocarán.` |
| `confirm.deleteclient.linkedbody` | `“{0}” ({1}) will be removed from Realm Portal. Your game files on disk will not be touched. Servers linked to this client will stay in your list.` | `Se quitará «{0}» ({1}) de Realm Portal. Los archivos del juego en el disco no se tocarán. Los servidores vinculados seguirán en tu lista.` |
| `confirm.deleteclient.accept` | `Remove client` | `Quitar cliente` |
| `settings.backup.keep` | `Keep backups per client` | `Respaldos a conservar por cliente` |
| `settings.backup.manage` | `Manage backups…` | `Gestionar respaldos…` |
| `settings.backup.error.keep` | `Enter a number of backups between 1 and 50.` | `Introduce un número de respaldos entre 1 y 50.` |
| `backups.title` | `Client backups` | `Respaldos del cliente` |
| `backups.subtitle` | `Snapshots of realmlist.wtf and WTF/Config.wtf` | `Copias de realmlist.wtf y WTF/Config.wtf` |
| `backups.client` | `Client` | `Cliente` |
| `backups.create` | `Create backup` | `Crear respaldo` |
| `backups.saved` | `SAVED BACKUPS` | `RESPALDOS GUARDADOS` |
| `backups.count` | `{0} of {1} max` | `{0} de {1} máx.` |
| `backups.empty` | `No backups yet for this client. Backups are created automatically before each server switch, or you can create one now.` | `Aún no hay respaldos para este cliente. Se crean automáticamente antes de cada cambio de servidor, o puedes crear uno ahora.` |
| `backups.item.manual` | `Manual backup` | `Respaldo manual` |
| `backups.item.server` | `Before switching to {0}` | `Antes de cambiar a {0}` |
| `backups.item.meta` | `{0} files ({1}) · {2}` | `{0} archivos ({1}) · {2}` |
| `backups.item.corrupt` | `Corrupted backup` | `Respaldo dañado` |
| `backups.badge.pendingundo` | `Pending undo` | `Cambio pendiente` |
| `backups.badge.restored` | `Restored` | `Restaurado` |
| `backups.badge.damaged` | `Damaged` | `Dañado` |
| `backups.action.restore` | `Restore` | `Restaurar` |
| `backups.action.delete` | `Delete backup` | `Eliminar respaldo` |
| `backups.status.created` | `Backup created ({0}).` | `Respaldo creado ({0}).` |
| `backups.status.deduplicated` | `Backup already matches current client files ({0}).` | `El respaldo ya coincide con los archivos actuales ({0}).` |
| `backups.status.restored` | `Restored backup {0}.` | `Respaldo {0} restaurado.` |
| `backups.status.deleted` | `Backup deleted.` | `Respaldo eliminado.` |
| `confirm.deletebackup.title` | `Delete this backup?` | `¿Eliminar este respaldo?` |
| `confirm.deletebackup.body` | `The backup from {0} ({1}) will be permanently deleted from disk.` | `El respaldo del {0} ({1}) se eliminará permanentemente del disco.` |
| `confirm.deletebackup.accept` | `Delete backup` | `Eliminar respaldo` |
| `confirm.backupconflict.title` | `A recent change hasn't been confirmed` | `Hay un cambio reciente sin confirmar` |
| `confirm.backupconflict.body` | `You switched to “{0}” at {1} and the Undo option is still active. Restoring this older backup ({2}) will replace the current client files. A safety backup of your current files will be saved first.` | `Cambiaste a «{0}» a las {1} y la opción Deshacer sigue activa. Si restauras este respaldo anterior ({2}), se reemplazarán los archivos actuales del cliente. Antes se guardará una copia de seguridad del estado actual.` |
| `confirm.backupconflict.restoreanyway` | `Restore anyway` | `Restaurar de todos modos` |
| `confirm.backupconflict.discard` | `Discard` | `Descartar` |
| `scan.outcome.found` | `Found {0} client(s) and imported {1} server(s).` | `Se encontraron {0} cliente(s) y se importaron {1} servidor(es).` |
| `scan.outcome.clientsonly` | `Found {0} client(s). Add a server to start playing.` | `Se encontraron {0} cliente(s). Añade un servidor para empezar a jugar.` |
| `scan.outcome.nochange` | `Scan complete: no new clients or servers found.` | `Búsqueda completada: no se encontraron clientes ni servidores nuevos.` |
| `error.storelocked` | `Another Realm Portal operation is updating the configuration. Try again in a moment.` | `Otra operación de Realm Portal está actualizando la configuración. Inténtalo de nuevo en un momento.` |

---

## Art used without a Figma reference

See "Pieces to validate with the designer" in `assets/ART-GAPS.md`.

## Window size and position

The main window remembers its last normal size and position and whether it was maximized (`ui-preferences.json` in the app's own config directory, key `Window`; `WindowPlacementPersistence`). It is saved on close and restored at start, before the window is shown:

- No saved value: the default 720×440, centred.
- The saved bounds are fitted to the screens present now (`WindowPlacementFit`): never below 720×440, never larger than the working area (on a screen smaller than 720×440 the minimum wins and the window sits at the top-left corner), moved fully inside the screen that holds most of the window, or centred on the first screen if that monitor is gone.
- A maximized window restores maximized and keeps the size it returns to; a minimized window is never restored minimized.
- Demo (`--demo`), gallery and tests do not read or write it.

Known platform gap (plain Avalonia, X11): the window manager decides the final position and may offset it by its frame (`_NET_FRAME_EXTENTS`), so on some window managers the window can shift by the title-bar height between runs. Native Wayland ignores client positions (size and maximized still apply).
