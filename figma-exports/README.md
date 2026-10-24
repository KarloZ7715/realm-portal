# Figma exports

2× PNG screen exports from the project's Figma design, page **Pantallas**. Source design file: private Figma file maintained by the project owner. Exports are 2× of the design frame: full screens are 1920×1200 (a 960×600 frame) and compact screens are 1440×960 (a 720×480 frame). Re-export by selecting the frames on the page and using Export at 2×, PNG.

This page is the visual reference for the whole app. `docs/ui-screens.md` maps each implemented screen to its mockup.

| File | Screen | Figma node |
|---|---|---|
| `01-first-run@2x.png` | First run, Spanish/English, before any server is added. | `18:2951` |
| `02-detection-found@2x.png` | Client detection: a client was found. | `18:3053` |
| `02b-detection-searching@2x.png` | Client detection: still searching. | `18:3170` |
| `02c-detection-none@2x.png` | Client detection: nothing found. | `18:3277` |
| `03-empty@2x.png` | Server list, empty. | `20:441` |
| `04-server-list@2x.png` | Server list with cards. | `20:574` |
| `05-add-edit-server@2x.png` | Add or edit a server sheet. | `20:1194` |
| `06-game-open@2x.png` | Game open (running). | `21:1291` |
| `07-incompatible-server@2x.png` | Incompatible server (needs a different client). | `21:1634` |
| `07b-confirm-incompatible-switch@2x.png` | Confirm switching to a server that needs a different client. Cancel and Switch anyway. | `21:1846` |
| `08-login-no-response@2x.png` | Login with no response. | `21:2116` |
| `09-change-undone@2x.png` | Change undone. | `21:2450` |
| `10-settings@2x.png` | Settings screen. | `23:2787` |
| `11-about@2x.png` | About. | `23:2930` |
| `compact-01-clients@2x.png` | Clients list and folder choice (compact). | `41:4639` |
| `compact-02-filtered-list@2x.png` | Filtered server list (compact). | `41:4893` |
| `compact-02b-no-results@2x.png` | Server list, no results (compact). | `41:5075` |
| `compact-03-add-server@2x.png` | Add server (compact). | `42:4972` |
| `compact-04-server-list@2x.png` | Server list (compact). | `20:950` |
| `compact-04-switching-server@2x.png` | Switching server, in progress (compact). | `43:4938` |
| `compact-05-undo-change@2x.png` | Undo change, with the notice (compact). | `43:5223` |
| `compact-06-launching-game@2x.png` | Opening the game (compact). | `43:5556` |
| `compact-06b-game-open@2x.png` | Game open (compact). | `43:5843` |
| `compact-07-settings-no-cleanup@2x.png` | Settings, top of the page: language and launcher. No cleanup section. | `48:5882` |
| `compact-07-settings-no-cleanup-scrolled@2x.png` | Same settings page, scrolled to arguments, environment variables, and the backup folder. | `48:6144` |
| `compact-play-menu@2x.png` | `Play ▾` menu (compact). | `49:5882` |
| `compact-play-menu-disabled@2x.png` | `Play ▾` menu with the item disabled (compact). | `49:6127` |
| `compact-09-backup-conflict@2x.png` | Backup mismatch dialog with Cancel and Discard. | `55:5645` |
| `compact-09b-backup-conflict-restore-anyway@2x.png` | Same dialog with Restore anyway, Cancel, and Discard. | `56:5887` |

Two strips on the page are design annotations, not UI (`48:6406` and `49:6370`); they are not exported.

Other files dropped in this folder stay gitignored.
