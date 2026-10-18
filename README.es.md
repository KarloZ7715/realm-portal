<p align="center">
  <img src="assets/branding/icon/png/realm-portal-256.png" alt="Logo de Realm Portal: una moneda de bronce partida en dos mitades, unidas por una llave de lacre rojo" width="128" height="128">
</p>

<h1 align="center">Realm Portal</h1>

<p align="center"><em>Cambia entre tus servidores privados de World of Warcraft con un clic.</em></p>

<p align="center"><a href="README.md">English</a> · Español</p>

## Qué es

Si juegas en más de un servidor privado de World of Warcraft, cambiar entre ellos suele significar editar archivos del juego a mano. Realm Portal es una aplicación de escritorio que busca hacerlo por ti: añades los servidores en los que juegas, eliges uno y el juego queda apuntando a él.

Está pensada para jugadores, no para programadores. Tú pones tu propio cliente del juego y tus propios servidores. La aplicación está disponible en español e inglés.

<p align="center">
  <img src="docs/screenshots/servers.png" alt="La pantalla de servidores de Realm Portal: una lista de servidores de ejemplo, cada uno con su sello de estado y un botón Jugar" width="720">
  <br>
  <em>La pantalla de servidores, con servidores de ejemplo inventados.</em>
</p>

## Estado: desarrollo temprano

Realm Portal **todavía no está listo para el uso diario**.

- **La aplicación de escritorio todavía no cambia archivos del juego.** La ventana existe, pero solo muestra servidores, clientes y pasos de ejemplo (datos simulados). El servicio real aún no está conectado a la interfaz.
- Todavía no hay descargas.
- La herramienta de línea de comandos ya funciona para gestionar tu lista de servidores (ver [Usarlo hoy](#usarlo-hoy)). Tampoco edita todavía los archivos del juego.

## Instalación

**Próximamente.** Primero Linux, en dos formatos:

- **AppImage**: un único archivo que se descarga y se ejecuta, sin instalar nada.
- **Paquete `.deb`**: para Debian, Ubuntu y distribuciones derivadas.

Todavía no hay versiones publicadas, así que no hay archivos que descargar ni comandos de instalación. Esta página enlazará a las descargas cuando existan.

**Windows llegará después**, cuando la versión de Linux esté lista.

## Usarlo hoy

Ahora mismo solo la herramienta de línea de comandos hace trabajo real, y primero hay que compilarla desde el código fuente (ver [CONTRIBUTING.md](CONTRIBUTING.md#build-and-test), en inglés).

### Elegir la carpeta del cliente

Realm Portal no descarga el juego: usa tu propia copia. Indica la carpeta de tu cliente con `--client-dir "/ruta/a/tu/juego"` (o con la variable de entorno `REALM_PORTAL_CLIENT_DIR`). Con ella, la herramienta sabe qué versión del juego tienes y qué servidores son compatibles.

En la aplicación de escritorio habrá una pantalla **Clientes** para elegir la carpeta (también "Elegir carpeta a mano"), pero hoy solo muestra carpetas de ejemplo.

### Añadir un servidor

```bash
realm-portal add "Mi servidor" logon.example.org
```

Para ver tus servidores: `realm-portal list`. Añade `--client-dir "/ruta/a/tu/juego"` para mostrar solo los que encajan con tu versión del juego (`--all` los muestra todos).

En la aplicación, el botón **Añadir** abre el formulario de nuevo servidor; hoy funciona solo con datos simulados.

### Cambiar de servidor

1. **Elige el servidor activo:** `realm-portal switch "Mi servidor"`
2. **Comprueba cuál está seleccionado:** `realm-portal status`

También puedes abrir el juego con `realm-portal launch "Mi servidor" --client-dir "/ruta/a/tu/juego"`.

La aplicación de escritorio hará estos mismos pasos con botones ("Jugar" y "Solo cambiar") cuando esté conectada al motor real.

### Deshacer

`realm-portal undo` vuelve al servidor que estaba seleccionado antes de tu último cambio (o a "ningún servidor seleccionado" si no había ninguno). Solo recuerda un paso.

El motor ya incluye una forma más segura de cambiar los propios archivos del juego: antes de modificar cualquier archivo se hace una copia de seguridad, y todo se puede restaurar exactamente como estaba. Todavía no está conectada a la aplicación ni a la herramienta de línea de comandos. Los detalles están en [docs/client-files-backup.md](docs/client-files-backup.md) (en inglés).

## Limitaciones

- **La aplicación todavía no cambia archivos del juego**: solo muestra datos simulados.
- **No hay descargas todavía**, ni AppImage ni `.deb`.
- **Por ahora solo Linux**; Windows viene después.
- **Deshacer recuerda un solo paso.**
- **Sin relación con Blizzard.** Realm Portal no está afiliado ni respaldado por Blizzard Entertainment.
- **No descarga el juego.** Nunca descarga ni distribuye clientes ni contenido del juego. Usas tu propia copia.
- **No incluye lista de servidores.** Añades los servidores en los que quieres jugar.
- **Solo servidores privados, por ahora.**

## Créditos de arte y fuentes

- **Texturas** (cuero, pergamino y bronce): [ambientCG](https://ambientcg.com) — Leather 037, Paper 003 y Metal 008, bajo **CC0 1.0**. Las versiones procesadas están en `assets/textures/`.
- **Tipografías** (de [Google Fonts](https://github.com/google/fonts)), bajo **SIL Open Font License 1.1**, cada una con su `OFL.txt` en `assets/fonts/`:
  - Almendra (Bold) — © 2011-2012 Ana Sanfelippo
  - Marcellus SC — © 2012 Brian J. Bonislawsky DBA Astigmatic (AOETI)
  - Alegreya — © 2011 The Alegreya Project Authors
  - Alegreya Sans — © 2013 The Alegreya Sans Project Authors
  - JetBrains Mono — © 2020 The JetBrains Mono Project Authors
- **Icono de la aplicación ("Tesera"), marcos, sellos, iconos de interfaz y kit de UI**: diseño propio de este proyecto, bajo **MIT** (ver [assets/branding/LICENSE-ART.md](assets/branding/LICENSE-ART.md)). Las piezas que llevan integradas las texturas de ambientCG conservan esa parte bajo CC0 1.0.
- **Sin recursos de Blizzard**: no se usa ningún logo, marca, captura ni tipografía de Blizzard.

El detalle completo está en [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) (en inglés).

## Licencia

MIT. Ver [LICENSE](LICENSE). Las licencias de componentes de terceros (paquetes, texturas, tipografías) están en [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

## Para desarrolladores

Las instrucciones de compilación, las pruebas, la referencia de la línea de comandos y la estructura del proyecto están en [CONTRIBUTING.md](CONTRIBUTING.md) (en inglés).
