<p align="center">
  <img src="assets/branding/icon/png/realm-portal-256.png" alt="Icono de Realm Portal: una moneda de bronce partida en dos mitades, unidas por una llave de lacre roja" width="128" height="128">
</p>

<h1 align="center">Realm Portal</h1>

<p align="center"><em>Gestiona tus servidores privados de World of Warcraft y cambia el cliente del juego entre ellos.</em></p>

<p align="center"><a href="README.md">English</a> · Español</p>

<p align="center">
  <a href="https://github.com/KarloZ7715/realm-portal/actions/workflows/ci.yml"><img src="https://github.com/KarloZ7715/realm-portal/actions/workflows/ci.yml/badge.svg" alt="Estado de CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="Licencia: MIT"></a>
</p>

<p align="center"><img src="docs/screenshots/servers.png" alt="Pantalla de servidores de Realm Portal (interfaz en inglés) con cuatro servidores de ejemplo, cada uno con su botón Play" width="800"></p>

## Qué hace

Realm Portal es una aplicación de escritorio para servidores privados de World of Warcraft. Registra los servidores en los que juegas (nombre, dirección, reino, cuenta y build de cliente requerida), busca clientes del juego en tu equipo y apunta el cliente que elijas al servidor que selecciones.

Al cambiar de servidor, Realm Portal escribe la configuración de conexión en los archivos de reino gestionados del cliente (`realmlist.wtf` cuando la versión del cliente lo usa, y `WTF/Config.wtf`), y antes guarda una copia de esos archivos. Después puede abrir el juego por ti. La interfaz está disponible en español e inglés.

## Compatibilidad y requisitos

- **Clientes del juego** (según su distribución de archivos, ver [docs/client-files.md](docs/client-files.md)): 1.12.1 (5875), 2.4.3 (8606), 3.3.5a (12340), 4.3.4 (15595), 5.4.8 (18414), 6.x (WoD) y 7.x (Legion). Otras versiones quedan fuera de esta cobertura.
- **Sistemas operativos**: Linux x64 y Windows x64.
- **Entorno de Linux**: un servidor X11 (o XWayland) y `libfontconfig1`, `libfreetype6`, `libx11-6`, `libice6`, `libsm6`.
- Las compilaciones publicadas para Linux y Windows son autocontenidas: no necesitan un runtime de .NET aparte.

## Instalación

Los pasos instalan la versión 1.1.0. Si la [página de versiones](https://github.com/KarloZ7715/realm-portal/releases/latest) muestra una versión más nueva, descárgala y cambia `1.1.0` por su número de versión en cada comando.

### Linux: Debian, Ubuntu, Linux Mint y similares (`.deb`)

1. Descarga [`realm-portal_1.1.0_amd64.deb`](https://github.com/KarloZ7715/realm-portal/releases/download/v1.1.0/realm-portal_1.1.0_amd64.deb).
2. Abre una terminal y ejecuta (si tu carpeta de descargas se llama `Downloads`, usa `cd ~/Downloads`):

   ```bash
   cd ~/Descargas
   sudo apt install ./realm-portal_1.1.0_amd64.deb
   ```

3. Abre **Realm Portal** desde el menú de aplicaciones, o ejecuta `realm-portal` en una terminal.

Para desinstalarlo, ejecuta `sudo apt remove realm-portal`. Tus servidores, ajustes y copias de seguridad se quedan en tu carpeta personal.

### Linux: cualquier distribución (AppImage)

Descarga [`RealmPortal-1.1.0-x86_64.AppImage`](https://github.com/KarloZ7715/realm-portal/releases/download/v1.1.0/RealmPortal-1.1.0-x86_64.AppImage), dale permiso de ejecución y ejecútalo (desde la carpeta donde lo guardaste):

```bash
cd ~/Descargas
chmod +x RealmPortal-1.1.0-x86_64.AppImage
./RealmPortal-1.1.0-x86_64.AppImage
```

### Windows

Descarga [`RealmPortal-1.1.0-win-x64.zip`](https://github.com/KarloZ7715/realm-portal/releases/download/v1.1.0/RealmPortal-1.1.0-win-x64.zip), descomprímelo y ejecuta `RealmPortal.App.exe`.

## Uso de la aplicación de escritorio

1. En el primer arranque, Realm Portal busca en las ubicaciones habituales (Steam, Lutris, prefijos de Wine y Bottles, y carpetas de juegos comunes) y registra los clientes y servidores que encuentra.
2. En **Clientes**, revisa las instalaciones detectadas y marca como **En uso** las que puedas lanzar. Usa **Buscar de nuevo** para repetir la búsqueda, o **Elegir carpeta a mano** para añadir una instalación manualmente.
3. En **Servidores**, selecciona **Añadir** e introduce el nombre y la dirección del servidor (`host` o `host:puerto`), y de forma opcional el reino, la cuenta y el cliente de destino.
4. Selecciona un servidor y pulsa **Jugar** para cambiar los archivos del cliente y abrir el juego, o **Solo cambiar** para modificar los archivos sin lanzarlo. Los servidores incompatibles con el cliente seleccionado quedan deshabilitados.
5. Tras un cambio, el aviso de Deshacer permite volver al servidor anterior en el cliente. Los archivos del cliente se respaldan antes de cada cambio (ver [Copias de seguridad y recuperación](#copias-de-seguridad-y-recuperación)).

## Copias de seguridad y recuperación

Realm Portal escribe una copia de seguridad antes de cambiar cualquier archivo del cliente, de modo que cada cambio se puede deshacer byte a byte.

- **Automática**: cada cambio guarda el contenido anterior de los archivos de reino gestionados en la carpeta de respaldos (`~/.local/share/realm-portal/backups` por defecto) y conserva los 10 respaldos más recientes por cliente.
- **Deshacer**: el aviso de Deshacer en la aplicación restaura el servidor que estaba activo antes del último cambio. Recuerda un solo paso. Si los archivos del cliente cambiaron a mano desde el cambio, Realm Portal pregunta antes de sobrescribirlos.
- **Manual**: en **Ajustes → Copia de respaldo**, usa **Gestionar respaldos…** para crear un respaldo, ver los guardados, restaurar uno anterior o eliminarlo. Los respaldos se guardan en la carpeta indicada en **Carpeta de respaldo**.

## Privacidad

Realm Portal no envía telemetría. Las únicas conexiones de red que hace son las dos funciones opcionales siguientes. Ambas están activadas por defecto y se pueden desactivar en **Ajustes**.

- **Buscar actualizaciones automáticamente.** Consulta GitHub una vez al día por una versión nueva (envía tu IP y la versión de la app a GitHub). Desactiva esta opción en Ajustes para detener las peticiones automáticas. El botón **Buscar actualizaciones** sigue funcionando con la comprobación automática desactivada, porque es una acción explícita tuya.
- **Mostrar estado del login del servidor.** Muestra si el puerto de login de cada servidor responde (tu IP es visible para ese servidor como máximo una vez cada 5 minutos mientras la lista está abierta). Resolver un nombre de host también puede consultar el resolvedor DNS del sistema. Desactiva esta opción en Ajustes para ocultar el indicador y no programar sondas.

## Alcance y avisos legales

- Realm Portal **no está afiliado ni respaldado por Blizzard Entertainment**.
- **No** descarga ni distribuye clientes ni contenido del juego; usas tu propia copia.
- **No incluye lista de servidores**; añades los servidores en los que quieres jugar.
- Admite **solo servidores privados**.
- Solo escribe los archivos de reino gestionados del cliente (`realmlist.wtf` cuando la versión del cliente lo usa, y `WTF/Config.wtf`); el resto de archivos del juego no se toca.

## Compilación y desarrollo

¿Prefieres la terminal? La herramienta de línea de comandos está documentada (en inglés) en [docs/cli.md](docs/cli.md). Las instrucciones de compilación, las pruebas y la estructura del proyecto están en [CONTRIBUTING.md](CONTRIBUTING.md).

## Licencia y créditos

MIT. Ver [LICENSE](LICENSE). Las licencias de los paquetes de terceros están en [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

El icono de la aplicación («Tesera»), el kit de interfaz, los sellos y los iconos son obra original, con licencia MIT (ver [assets/branding/LICENSE-ART.md](assets/branding/LICENSE-ART.md)). Las texturas proceden de [ambientCG](https://ambientcg.com) bajo CC0 1.0, y las tipografías incluidas proceden de [Google Fonts](https://github.com/google/fonts) bajo la SIL Open Font License 1.1. No se usa ningún recurso de Blizzard.
