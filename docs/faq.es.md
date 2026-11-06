# Preguntas frecuentes y solución de problemas

[English](faq.md) · Español

Respuestas a las preguntas que más se repiten. Cada problema indica la causa probable y qué hacer. Los nombres de botones y menús son los de la interfaz en español.

Si tu problema no aparece aquí, mira [Reportar un fallo](#reportar-un-fallo).

## Contenido

- [El juego sigue conectándose al servidor anterior](#el-juego-sigue-conectándose-al-servidor-anterior)
- [Realm Portal no encuentra mi cliente del juego](#realm-portal-no-encuentra-mi-cliente-del-juego)
- [Abrir el juego con Wine u otro envoltorio](#abrir-el-juego-con-wine-u-otro-envoltorio)
- [«Sin permiso para escribir» o un `realmlist.wtf` de solo lectura](#sin-permiso-para-escribir-o-un-realmlistwtf-de-solo-lectura)
- [Deshacer un cambio de servidor](#deshacer-un-cambio-de-servidor)
- [Comprobar una descarga con `SHA256SUMS`](#comprobar-una-descarga-con-sha256sums)
- [Dónde guarda Realm Portal sus archivos](#dónde-guarda-realm-portal-sus-archivos)
- [Lo que Realm Portal no hace](#lo-que-realm-portal-no-hace)
- [Reportar un fallo](#reportar-un-fallo)

## El juego sigue conectándose al servidor anterior

**Carpeta de cliente equivocada.** Realm Portal solo cambia el cliente al que aplicó el cambio. Si tienes más de una copia del juego, o abres el juego desde un acceso directo u otro lanzador, puede que estés abriendo otra carpeta. Revisa la carpeta en **Clientes** y usa **Jugar** en Realm Portal para que se cambie y se abra el mismo cliente.

**Editaste la dirección del servidor después del cambio.** Editar el servidor activo no reescribe por sí solo los archivos del cliente, y la tarjeta del servidor no muestra ningún aviso. Elige **Jugar** en ese servidor para escribir la dirección nueva. Así se descarta el deshacer pendiente del cambio anterior; los archivos de antes de ese cambio siguen en **Ajustes → Copia de respaldo → Gestionar respaldos…**.

**Datos viejos del servidor anterior.** Si entras al servidor correcto pero ves nombres de objetos, misiones o criaturas que no corresponden, la caché del cliente es del servidor anterior. En **Ajustes → Caché del cliente**, activa **Vaciar Cache/WDB al cambiar de servidor** (Realm Portal 1.1.0 o posterior). Viene desactivado. Cuando está activo, Realm Portal borra solo las carpetas `Cache` y `WDB` del cliente seleccionado, después de un cambio correcto. Para aplicarlo a un solo servidor, usa **Vaciar Cache/WDB al cambiar** en **Editar servidor…** (en el menú junto a **Jugar**).

**El juego estaba abierto.** Realm Portal no cambia de servidor mientras el juego está en marcha y muestra **El juego está abierto**. Cierra el juego y vuelve a cambiar.

## Realm Portal no encuentra mi cliente del juego

**El cliente no está en un lugar que revise la búsqueda.** La búsqueda revisa los prefijos de Wine y Bottles, los juegos de Lutris, las bibliotecas de Steam, `~/Games` y `~/Juegos`, `/opt/games` en Linux y, en Windows, las carpetas `Program Files`, una carpeta `Games` en la raíz de cada unidad fija y el registro. La lista completa está en [What `scan` searches](cli.md#what-scan-searches) (en inglés). Solo baja unos pocos niveles de carpetas y se salta las carpetas ocultas y los enlaces simbólicos.

Para añadir un cliente que la búsqueda no encontró, abre **Clientes**, elige **Elegir carpeta a mano** y selecciona la carpeta que contiene `Wow.exe`. **Buscar de nuevo** hace una búsqueda nueva.

**La carpeta no tiene `Wow.exe`.** Realm Portal reconoce un cliente por el archivo `Wow.exe` que está directamente dentro de la carpeta. Elige la carpeta que lo contiene, no una carpeta superior ni una subcarpeta. Si falta, la app muestra **No está el ejecutable del juego**.

**La versión del cliente no es compatible.** Los clientes compatibles son 1.12.1 (5875), 2.4.3 (8606), 3.3.5a (12340), 4.3.4 (15595), 5.4.8 (18414), 6.x (WoD) y 7.x (Legion). Con otras versiones aparece **Cliente no compatible**.

**El cliente aparece pero los servidores están desactivados.** Un servidor puede exigir una build de cliente. Los servidores que no coinciden con el cliente seleccionado se desactivan. En **Editar servidor…**, pon **Clientes compatibles** en **Cualquier cliente** si el servidor funciona con todas las versiones. Comprueba también que el cliente esté marcado como **En uso** en **Clientes**.

## Abrir el juego con Wine u otro envoltorio

Por defecto, Realm Portal abre `Wow.exe` directamente. Eso funciona en Windows. En Linux tienes que indicarle cómo abrir el juego, en **Ajustes → Lanzador**:

- **Plantilla de comando**: el comando que abre el juego. `{exe}` se reemplaza por la ruta de `Wow.exe` y es obligatorio. `{dir}` se reemplaza por la carpeta del cliente. Ejemplo: `wine {exe}`.
- **Prefijo**: un envoltorio que va antes de todo, por ejemplo `gamemoderun`.
- **Argumentos**: argumentos extra para el juego, por ejemplo `-windowed`.
- **Variables de entorno**: una por línea, como `CLAVE=valor`. Para usar un prefijo de Wine concreto, añade `WINEPREFIX=/home/user/.wine-wow` (la carpeta que contiene `drive_c`). Escribe la ruta completa: aquí `~` no se expande.

Con estos ajustes, **Jugar** ejecuta el prefijo, luego la plantilla (o `Wow.exe`) y luego los argumentos. Nada pasa por una shell, así que `$VAR`, `;` y similares no se interpretan.

Si ves **No se pudo iniciar el juego**, comprueba que el comando de la plantilla o del prefijo está instalado y en tu `PATH` (por ejemplo, `command -v wine` en una terminal muestra su ruta). **Ajustes del lanzador no válidos** significa que a la plantilla le falta `{exe}` o tiene una comilla sin cerrar, o que una línea de entorno no tiene la forma `CLAVE=valor`.

## «Sin permiso para escribir» o un `realmlist.wtf` de solo lectura

Un `realmlist.wtf` de solo lectura no es un problema por sí mismo. Realm Portal lo reemplaza y lo deja de solo lectura, y **Deshacer** restaura el archivo original con sus permisos originales.

**Sin permiso para escribir** significa que el sistema no deja a tu usuario cambiar la carpeta del cliente. Causas habituales:

- En Linux, el cliente se copió o instaló como otro usuario (por ejemplo con `sudo`), y ese usuario es el dueño de la carpeta.
- En Windows, el cliente está en `C:\Program Files`, que necesita permisos de administrador para cambiarse.

La solución más sencilla es poner una copia del cliente en una carpeta tuya, como `~/Games` en Linux o `C:\Games` en Windows (las dos se revisan en la búsqueda), y luego elegir **Buscar de nuevo** en **Clientes**. Realm Portal solo escribe `realmlist.wtf` y `WTF/Config.wtf`, así que necesita permiso de escritura en las carpetas que contienen esos archivos.

**Carpeta de solo lectura** significa que el cliente está en un sistema de archivos montado como solo lectura, como un disco o una carpeta de red de solo lectura. Mueve el cliente a un disco donde se pueda escribir.

## Deshacer un cambio de servidor

Antes de que un cambio modifique `realmlist.wtf` o `WTF/Config.wtf` del cliente, Realm Portal respalda los archivos que va a cambiar. Un cambio que no modifica nada no crea respaldo.

- **Último cambio**: elige **Deshacer** en el aviso que aparece tras un cambio, o **Deshacer último cambio** arriba en **Servidores**. Ese botón sigue disponible aunque cierres y vuelvas a abrir la app, hasta que no quede nada por deshacer. Deshacer solo retrocede un paso. Si eliges **Jugar** otra vez en el servidor activo después de editar su dirección, ese deshacer se descarta; restaura los archivos anteriores desde **Respaldos anteriores**.
- **Respaldos anteriores**: en **Ajustes → Copia de respaldo**, elige **Gestionar respaldos…** y luego **Restaurar** en el respaldo que quieras. Por defecto, Realm Portal guarda los 10 respaldos más recientes de cada cliente.
- **Deshacer detenido**: los archivos del cliente se cambiaron a mano después del cambio. Déjalos como estaban, o elige **Descartar** para abandonar el deshacer pendiente y dejar los archivos como están.

## Comprobar una descarga con `SHA256SUMS`

Cada versión publicada incluye un archivo `SHA256SUMS`. Descárgalo en la misma carpeta que el paquete y ejecuta (si tu carpeta de descargas se llama `Downloads`, usa `cd ~/Downloads`):

```bash
cd ~/Descargas
sha256sum --ignore-missing -c SHA256SUMS
```

La línea de tu archivo debe terminar en `OK`. En Windows, en PowerShell:

```powershell
Get-FileHash .\RealmPortal-1.1.0-win-x64.zip -Algorithm SHA256
```

Compara el resultado con la línea de ese archivo en `SHA256SUMS`. Esta comprobación detecta una descarga dañada o incompleta. No demuestra quién publicó el archivo.

## Dónde guarda Realm Portal sus archivos

| Qué | Linux | Windows |
| --- | --- | --- |
| Servidores, clientes y ajustes | `~/.config/realm-portal` (o `$XDG_CONFIG_HOME/realm-portal`) | `%APPDATA%\realm-portal` |
| Respaldos de los archivos del cliente | `~/.local/share/realm-portal/backups`, o la carpeta elegida en **Ajustes → Copia de respaldo** | `%LOCALAPPDATA%\realm-portal\backups`, o la carpeta elegida en **Ajustes → Copia de respaldo** |
| Registro de errores (1.1.0 o posterior) | `~/.local/state/realm-portal/logs/realm-portal.log` (o dentro de `$XDG_STATE_HOME`) | `%LOCALAPPDATA%\RealmPortal\logs\realm-portal.log` |
| `activo.json` | `~/.config/wow-servidores` | `%USERPROFILE%\.config\wow-servidores` |

Desinstalar la app no borra estas carpetas. En el cliente del juego, Realm Portal solo escribe `realmlist.wtf` (cuando la versión del cliente lo usa) y `WTF/Config.wtf`. Si activaste el vaciado de caché, también borra las carpetas `Cache` y `WDB` del cliente.

## Lo que Realm Portal no hace

- No descarga ni incluye clientes del juego. Necesitas tu propia copia del juego.
- No trae una lista de servidores. Tú añades los servidores en los que juegas.
- Es solo para servidores privados y no está afiliado a Blizzard Entertainment.
- Los paquetes publicados solo contienen la app de escritorio. La herramienta de línea de comandos se ejecuta desde el código fuente (ver [docs/cli.md](cli.md), en inglés).

## Reportar un fallo

Abre un [reporte de fallo](https://github.com/KarloZ7715/realm-portal/issues/new?template=bug_report.yml). Incluye:

- la versión de Realm Portal (en la barra de estado, o en **Ajustes → Acerca de Realm Portal…**);
- tu sistema operativo y cómo instalaste la app (`.deb`, AppImage, zip de Windows o desde el código fuente);
- la versión del cliente del juego, si el problema tiene que ver con un cliente;
- el mensaje de error exacto, y una captura de pantalla si el problema es visual.

Si la app se cerró de golpe o una acción falló sin un mensaje claro, adjunta las líneas relevantes del registro de errores indicado en [Dónde guarda Realm Portal sus archivos](#dónde-guarda-realm-portal-sus-archivos). El registro ya reemplaza tu carpeta personal por `~`, pero léelo antes de publicarlo y quita nombres de cuenta, direcciones de servidores o cualquier otra cosa que no quieras hacer pública.

Los problemas de seguridad no van en un issue público. Mira [SECURITY.md](../SECURITY.md).
