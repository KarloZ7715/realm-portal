# Realm Portal — Assets sueltos de la dirección B «Pergamino y bronce»

Generado el 2026-10-17 (America/Bogota) para el spike de estilo en Avalonia. Origen visual: maquetas de Figma (https://www.figma.com/design/6kBPmTavrfpbceMSUvvWl3) y `DESIGN-TOKENS-B.md`. Los colores salen de esos tokens. Todos los PNG de `frames/` e `icons/` son RGBA (transparencia real, alfa recta, sin halos comprobados sobre fondo blanco, negro, magenta y cuero).

Vista de conjunto: `preview-composicion.png` (ventana 9-slice sobre magenta, tarjeta activa, botones, divisor, pestaña, sellos y esquinas; abajo, los mismos elementos sobre blanco/negro/magenta para detectar halos).

## Estructura
```
assets/
  ASSETS.md                  este documento
  preview-composicion.png    composición de prueba
  textures/                  3 texturas JPG teselables (CC0) + mosaico de comprobación
  frames/                    marcos, paneles, botones, divisores (PNG @1x/@2x + SVG) + nineslice.json
  icons/app/                 icono de la app (SVG, PNG 512–16, ICO)
  icons/sellos/              5 sellos de estado (SVG, PNG 64/128)
  icons/ui/ , icons/ui-tinta/  23 iconos de interfaz SVG (color hueso / color tinta)
  fonts/                     TTF + OFL.txt por familia + FONTS.md
```

## Convenciones
- **@1x / @2x**: `nombre.png` y `nombre@2x.png` (exactamente el doble de píxeles; se renderizan de forma nativa a cada escala, no se reescalan, para evitar halos).
- **Insets de 9-slice** se dan como **izquierda, arriba, derecha, abajo** en píxeles del PNG @1x (en @2x multiplicar por 2). También en `frames/nineslice.json` (legible por máquina).
- Avalonia no trae 9-slice nativo para imágenes (no probado aquí): hará falta un control propio o `DrawingContext.DrawImage` con rectángulos origen/destino por cada una de las 9 zonas. Los insets están pensados para eso.
- **SVG de `frames/`**: versión vectorial **plana** (sin textura, sin sombras ni filtros) para quien prefiera vectores; el aspecto completo (textura, sombra, sombra interior) está solo en los PNG.
- Licencia de los dibujos (SVG/PNG de frames e icons): creados desde cero para este proyecto, sin material de terceros salvo las texturas CC0 horneadas en los PNG (ver licencias). Sin assets de Blizzard.

## 1. textures/ (CC0 1.0, ambientCG)

| Archivo | Píxeles | Peso | Teselable | Origen / licencia |
|---|---|---|---|---|
| `textures/tex-pergamino.jpg` | 1024×1024 | 44.8 KB | sí; relación salto/vecino de gradiente 2.0× pero de solo 0.65 niveles de gris; sin costura visible en mosaico 2×2 | Paper003 (ambientCG) · CC0 1.0 |
| `textures/tex-cuero.jpg` | 1024×1024 | 229.9 KB | sí; relación 1.2×, sin costura visible en mosaico 2×2 | Leather037 (ambientCG) · CC0 1.0 |
| `textures/tex-bronce.jpg` | 512×512 | 67.3 KB | sí; relación 1.2–1.3×, sin costura visible en mosaico 2×2 | Metal008 (ambientCG) · CC0 1.0 |
| `textures/preview-mosaico-2x2.png` | 1840×600 | 889.2 KB | — | comprobación visual (cuero, pergamino y bronce en mosaico 2×2) |

Notas: las tres texturas ya eran teselables en origen (ambientCG las publica así) y no se han modificado respecto a los originales de ambientCG (solo recodificadas a JPEG q90). Se verificó con mosaico 2×2 (con contraste ×2.2 en el cruce) y con la métrica de salto en el borde frente al gradiente interior. En las maquetas se usan con opacidad parcial sobre el color base del token: pergamino 50 % sobre `#D8C7A0` (tile de 512 px css), cuero 34–40 % (tile 512 px css), bronce 35 % en Figma y 45 % en el aro de los PNG del icono. El pergamino tiene manchas grandes reconocibles: al repetirlo 2×2 se nota el patrón; en paneles de hasta ~700 px no se repite.

## 2. frames/

Insets en px @1x (izq, arriba, der, abajo). «Canvas» = tamaño del PNG @1x.

| Archivo (PNG @1x + @2x, SVG plano) | Canvas @1x | Insets 9-slice | Qué es / notas |
|---|---|---|---|
| `panel-hoja-ancho` | 724×134 | 36, 36, 36, 36 | sombra incluida en el PNG (12 px de margen); el SVG es plano, sin sombra ni textura |
| `panel-hoja-medio` | 384×284 | 36, 36, 36, 36 | sombra incluida en el PNG (12 px de margen); el SVG es plano, sin sombra ni textura |
| `panel-activa-ancho` | 724×134 | 36, 36, 36, 36 | sombra incluida en el PNG (12 px de margen); el SVG es plano, sin sombra ni textura |
| `panel-activa-medio` | 384×284 | 36, 36, 36, 36 | sombra incluida en el PNG (12 px de margen); el SVG es plano, sin sombra ni textura |
| `esquina-bronce-*` | 18×18 | — | sin 9-slice; colocar en cada esquina del panel a 0,0 (sin margen) |
| `boton-primario-normal` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-primario-hover` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-primario-pulsado` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-primario-desactivado` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-secundario-normal` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-secundario-hover` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-secundario-pulsado` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `boton-secundario-desactivado` | 160×40 | 14, 14, 14, 14 | fondo de cuero con textura horneada, borde de bronce 1.5 px, costura punteada y 2 remaches; el centro se estira (la costura se estira ~1.3x a 200 px de ancho) |
| `herraje-bronce-remaches` | 160×64 | 16, 16, 16, 16 | centro transparente; usar como borde de tarjeta/botón sobre cualquier fondo; remaches en las 4 esquinas |
| `anillo-foco-cuero` | 176×56 | 14, 14, 14, 14 | anillo de foco (doble en cuero: contorno oscuro + foco claro; simple en pergamino); elemento de 160x40 centrado con 8 px de margen |
| `anillo-foco-pergamino` | 176×56 | 14, 14, 14, 14 | anillo de foco (doble en cuero: contorno oscuro + foco claro; simple en pergamino); elemento de 160x40 centrado con 8 px de margen |
| `ventana-marco` | 960×600 | 14, 40, 14, 28 | cuero opaco con barra de título (40 px) y barra de estado (28 px) horneadas, filete de bronce y 4 remaches; el centro es cuero teselado (no se ven costuras al estirar) |
| `divisor-hilo-medallon` | 712×14 | — | 3-slice horizontal: hilo izquierdo (estirable, x 0-326), medallón fijo (44 px centrales), hilo derecho (estirable); en Avalonia, mejor 2 hilos con tile + divisor-medallon.png centrado |
| `costura-h-tile` | 10×4 | — | tile repetible (periodo 10 px: 6 de hilo + 4 de hueco), color #8A6C4A |
| `costura-v-tile` | 4×10 | — | tile repetible (periodo 10 px: 6 de hilo + 4 de hueco), color #8A6C4A |
| `pestana-pergamino` | 228×64 | 18, 18, 18, 18 | pestaña activa de la barra lateral: pega al borde izquierdo (x=10 del PNG = borde de la ventana); marca de lacre de 5 px a la izquierda |
| `divisor-medallon` | 44×14 | — | medallón de bronce suelto (para centrarlo sobre dos hilos con tile) |

Detalles por pieza:

- **Paneles de pergamino** (`panel-hoja-*` y `panel-activa-*`): formas *Ancho* (cuerpo 700×110) y *Medio* (cuerpo 360×260) como en la maqueta; incluyen 12 px de margen transparente para la sombra, borde irregular (±1.4 px, generado por `paperP`), sombra interior y filete interior de bronce oscuro a 6 px. `activa` = hoja + 4 esquineros de bronce ya horneados. Insets 36 (= 12 margen + 24 de zona decorada) en los cuatro lados. Al estirar, el borde irregular de las zonas centrales se estira con el panel (ondulación más suave): *Ancho* está pensado para formas apaisadas (≥ 200×60) y *Medio* para más cuadradas; con menos de 72 px de lado se solapan los insets.
- **Esquineros de bronce**: 4 PNG separados (`-tl,-tr,-bl,-br`, 18×18) y `esquina-bronce.svg` (TL, rotable 90/180/270). Se colocan en la esquina del panel a (0,0) del cuerpo (no del canvas con sombra: sumar 12 px en los paneles).
- **Botones**: 160×40; fondo de cuero con textura horneada (tinte lacre = primario, madera = secundario), borde de bronce 1.5 px, costura punteada interior (3/3) y 2 remaches. Estados normal/hover/pulsado/desactivado (desactivado sin costura ni remaches, como en la maqueta). El texto y el icono NO están horneados. La costura se estira con el 9-slice (≈1.3× a 200 px); si molesta, usar el SVG plano o dibujar la costura en código. El «foco» es una pieza aparte (`anillo-foco-*`, 8 px de margen alrededor del elemento).
- **`herraje-bronce-remaches`**: marco con centro transparente (borde de bronce, costura, 4 remaches) para tarjetas o botones sobre cualquier fondo. En la maqueta las tarjetas son pergamino con esquineros; este herraje es una extensión coherente con el look, no una pieza literal de la maqueta.
- **`ventana-marco`**: 960×600 opaco (cuero teselado), barra de título de 40 px y barra de estado de 28 px horneadas (sin texto ni botones), filete de bronce oscuro de 2 px, 4 remaches y esquinas redondeadas de 8 px (transparentes). Insets 14, 40, 14, 28. Ojo: el filete de 2 px y los remaches quedan dentro de los 14 px de las esquinas; las esquinas redondeadas exigen mantener esos 14 px sin estirar.
- **Divisor**: `divisor-hilo-medallon` 712×14 es 3-slice horizontal (hilo izq. 0–326, medallón fijo de 44 px en 334–378, hilo der. 386–712). Para anchos arbitrarios usar `costura-h-tile` repetido a cada lado del `divisor-medallon`.
- **Costura punteada**: tiles repetibles de 10×4 (horizontal) y 4×10 (vertical); periodo 10 px (6 hilo + 4 hueco), color `#8A6C4A`. A @1x el trazo de 1.5 px queda en medio píxel (alfa máx. 192): es lo esperado, a @2x queda nítido.
- **`pestana-pergamino`**: pestaña activa de la barra lateral, cuerpo 208×44 con 10 px de margen de sombra, esquinas derechas redondeadas 6 px, marca de lacre de 5 px a la izquierda; insets 18. Encaja pegada al borde izquierdo (x=10 del PNG = borde de la ventana).

Listado completo de `frames/`:

| Archivo | Píxeles | Peso |
|---|---|---|
| `frames/anillo-foco-cuero.png` | 176×56 | 0.9 KB |
| `frames/anillo-foco-cuero.svg` | SVG | 0.3 KB |
| `frames/anillo-foco-cuero@2x.png` | 352×112 | 2.0 KB |
| `frames/anillo-foco-pergamino.png` | 176×56 | 0.6 KB |
| `frames/anillo-foco-pergamino.svg` | SVG | 0.2 KB |
| `frames/anillo-foco-pergamino@2x.png` | 352×112 | 1.2 KB |
| `frames/boton-primario-desactivado.png` | 160×40 | 8.4 KB |
| `frames/boton-primario-desactivado@2x.png` | 320×80 | 26.2 KB |
| `frames/boton-primario-hover.png` | 160×40 | 9.6 KB |
| `frames/boton-primario-hover@2x.png` | 320×80 | 27.8 KB |
| `frames/boton-primario-normal.png` | 160×40 | 9.7 KB |
| `frames/boton-primario-normal.svg` | SVG | 0.5 KB |
| `frames/boton-primario-normal@2x.png` | 320×80 | 28.5 KB |
| `frames/boton-primario-pulsado.png` | 160×40 | 9.5 KB |
| `frames/boton-primario-pulsado@2x.png` | 320×80 | 27.9 KB |
| `frames/boton-secundario-desactivado.png` | 160×40 | 8.4 KB |
| `frames/boton-secundario-desactivado@2x.png` | 320×80 | 26.2 KB |
| `frames/boton-secundario-hover.png` | 160×40 | 9.7 KB |
| `frames/boton-secundario-hover@2x.png` | 320×80 | 28.1 KB |
| `frames/boton-secundario-normal.png` | 160×40 | 9.7 KB |
| `frames/boton-secundario-normal.svg` | SVG | 0.5 KB |
| `frames/boton-secundario-normal@2x.png` | 320×80 | 28.3 KB |
| `frames/boton-secundario-pulsado.png` | 160×40 | 9.3 KB |
| `frames/boton-secundario-pulsado@2x.png` | 320×80 | 27.9 KB |
| `frames/costura-h-tile.png` | 10×4 | 0.1 KB |
| `frames/costura-h-tile.svg` | SVG | 0.1 KB |
| `frames/costura-h-tile@2x.png` | 20×8 | 0.1 KB |
| `frames/costura-v-tile.png` | 4×10 | 0.1 KB |
| `frames/costura-v-tile.svg` | SVG | 0.1 KB |
| `frames/costura-v-tile@2x.png` | 8×20 | 0.1 KB |
| `frames/divisor-hilo-medallon.png` | 712×14 | 0.8 KB |
| `frames/divisor-hilo-medallon.svg` | SVG | 0.5 KB |
| `frames/divisor-hilo-medallon@2x.png` | 1424×28 | 1.9 KB |
| `frames/divisor-medallon.png` | 44×14 | 0.6 KB |
| `frames/divisor-medallon.svg` | SVG | 0.3 KB |
| `frames/divisor-medallon@2x.png` | 88×28 | 1.3 KB |
| `frames/esquina-bronce-bl.png` | 18×18 | 0.2 KB |
| `frames/esquina-bronce-bl.svg` | SVG | 0.3 KB |
| `frames/esquina-bronce-bl@2x.png` | 36×36 | 0.2 KB |
| `frames/esquina-bronce-br.png` | 18×18 | 0.2 KB |
| `frames/esquina-bronce-br.svg` | SVG | 0.3 KB |
| `frames/esquina-bronce-br@2x.png` | 36×36 | 0.2 KB |
| `frames/esquina-bronce-tl.png` | 18×18 | 0.2 KB |
| `frames/esquina-bronce-tl.svg` | SVG | 0.3 KB |
| `frames/esquina-bronce-tl@2x.png` | 36×36 | 0.2 KB |
| `frames/esquina-bronce-tr.png` | 18×18 | 0.2 KB |
| `frames/esquina-bronce-tr.svg` | SVG | 0.3 KB |
| `frames/esquina-bronce-tr@2x.png` | 36×36 | 0.2 KB |
| `frames/esquina-bronce.svg` | SVG | 0.3 KB |
| `frames/herraje-bronce-remaches.png` | 160×64 | 1.1 KB |
| `frames/herraje-bronce-remaches.svg` | SVG | 0.6 KB |
| `frames/herraje-bronce-remaches@2x.png` | 320×128 | 2.7 KB |
| `frames/nineslice.json` | JSON | 5.1 KB |
| `frames/panel-activa-ancho.png` | 724×134 | 39.4 KB |
| `frames/panel-activa-ancho.svg` | SVG | 2.5 KB |
| `frames/panel-activa-ancho@2x.png` | 1448×268 | 97.6 KB |
| `frames/panel-activa-medio.png` | 384×284 | 48.0 KB |
| `frames/panel-activa-medio.svg` | SVG | 2.1 KB |
| `frames/panel-activa-medio@2x.png` | 768×568 | 115.8 KB |
| `frames/panel-hoja-ancho.png` | 724×134 | 39.3 KB |
| `frames/panel-hoja-ancho.svg` | SVG | 1.7 KB |
| `frames/panel-hoja-ancho@2x.png` | 1448×268 | 98.0 KB |
| `frames/panel-hoja-medio.png` | 384×284 | 48.3 KB |
| `frames/panel-hoja-medio.svg` | SVG | 1.4 KB |
| `frames/panel-hoja-medio@2x.png` | 768×568 | 117.0 KB |
| `frames/pestana-pergamino.png` | 228×64 | 6.5 KB |
| `frames/pestana-pergamino.svg` | SVG | 0.2 KB |
| `frames/pestana-pergamino@2x.png` | 456×128 | 14.7 KB |
| `frames/ventana-marco.png` | 960×600 | 409.7 KB |
| `frames/ventana-marco.svg` | SVG | 0.7 KB |
| `frames/ventana-marco@2x.png` | 1920×1200 | 1.43 MB |

## 3. icons/

### icons/app — icono de la aplicación (sello de lacre con arco-portal)

| Archivo | Píxeles | Peso | Notas |
|---|---|---|---|
| `icons/app/app-icon.svg` | SVG | 4.0 KB | vector plano 128×128 (aro de bronce sin textura) |
| `icons/app/app-icon-simplificado.svg` | SVG | 3.4 KB | versión simplificada (trazos gruesos) para ≤32 px |
| `icons/app/app-icon-512.png` | 512×512 | 68.5 KB | aro de bronce con textura Metal008 (CC0) en ≥64 px |
| `icons/app/app-icon-256.png` | 256×256 | 27.9 KB | aro de bronce con textura Metal008 (CC0) en ≥64 px |
| `icons/app/app-icon-128.png` | 128×128 | 11.7 KB | aro de bronce con textura Metal008 (CC0) en ≥64 px |
| `icons/app/app-icon-64.png` | 64×64 | 5.0 KB | aro de bronce con textura Metal008 (CC0) en ≥64 px |
| `icons/app/app-icon-48.png` | 48×48 | 3.3 KB | aro de bronce con textura Metal008 (CC0) en ≥64 px |
| `icons/app/app-icon-simple-32.png` | 32×32 | 1.3 KB | simplificado, aro plano |
| `icons/app/app-icon-simple-16.png` | 16×16 | 0.7 KB | simplificado, aro plano |
| `icons/app/app-icon.ico` | ICO | 125.7 KB | ICO multi-tamaño: 256, 128, 64, 48, 32 (simplificado), 16 (simplificado) |

Las versiones de 48 px o más usan el diseño completo; 32 y 16 usan la simplificada (sello y arco con trazo grueso, sin costura). Las SVG tienen el aro de bronce plano (`#A8743F`); la textura de bronce solo está en los PNG ≥64.

### icons/sellos — sellos de estado (SVG 56×56, PNG 64 y 128)

| Estado | SVG | PNG | Color / glifo |
|---|---|---|---|
| activo | `icons/sellos/sello-activo.svg` | `sello-activo-64.png`, `sello-activo-128.png` | lacre · arco-portal |
| listo | `icons/sellos/sello-listo.svg` | `sello-listo-64.png`, `sello-listo-128.png` | bosque · marca de verificación |
| incompatible | `icons/sellos/sello-incompatible.svg` | `sello-incompatible-64.png`, `sello-incompatible-128.png` | ámbar · triángulo de aviso |
| sin-respuesta | `icons/sellos/sello-sin-respuesta.svg` | `sello-sin-respuesta-64.png`, `sello-sin-respuesta-128.png` | apagado · cruz |
| inactivo | `icons/sellos/sello-inactivo.svg` | `sello-inactivo-64.png`, `sello-inactivo-128.png` | apagado · arco-portal en hueso |

El estado nunca va solo por color: cada sello lleva su glifo. «Incompatible» corresponde al sello llamado «Aviso» en Figma.

### icons/ui — 23 iconos de interfaz (SVG 24×24)

Trazo de 1.6–2 px, extremos cuadrados; sin relleno salvo `jugar` y `rombo`. `icons/ui/` en color hueso `#EFE5CD` (sobre cuero) e `icons/ui-tinta/` en `#2B2118` (sobre pergamino). Nombre del archivo → nombre del componente en Figma:

| Archivo | Figma | Archivo | Figma | Archivo | Figma |
|---|---|---|---|---|---|
| `servidores.svg` | Icono/servers | `carpeta.svg` | Icono/folder | `engranaje.svg` | Icono/gear |
| `buscar.svg` | Icono/search | `mas.svg` | Icono/plus | `chevron-abajo.svg` | Icono/chev |
| `aviso.svg` | Icono/warn | `info.svg` | Icono/info | `ok.svg` | Icono/check |
| `error.svg` | Icono/x | `jugar.svg` | Icono/play | `rombo.svg` | Icono/diamond |
| `candado.svg` | Icono/lock | `deshacer.svg` | Icono/undo | `minimizar.svg` | Icono/min |
| `maximizar.svg` | Icono/max | `cerrar.svg` | Icono/close | `globo.svg` | Icono/globe |
| `reloj.svg` | Icono/clock | `actualizar.svg` | Icono/refresh | `lapiz.svg` | Icono/pencil |
| `copiar.svg` | Icono/copy | `lista.svg` | Icono/list |  |  |

**Procedencia**: dibujados a mano para este proyecto con un script del proyecto (no incluido en este repositorio) (no proceden del conector de Figma ni de Lucide/ISC; no hay material de terceros). Para cambiar de color, editar el atributo `stroke`/`fill` (Avalonia.Svg no garantiza `currentColor`).

## 4. fonts/

Ver `fonts/FONTS.md` (licencia OFL 1.1, URL de origen, tipos de archivo y comprobación de glifos á é í ó ú ñ ¿ ¡ ü).

## Limitaciones honestas

- Nada se ha probado dentro de Avalonia; la verificación es visual con Pillow/cairosvg (9-slice implementado en Python con los mismos insets). Cualquier diferencia de render (suavizado, DPI) se descubrirá en el spike.
- Los SVG de `frames/` son planos (sin textura, sombras ni sombra interior): los efectos solo están en los PNG, porque cairosvg/Avalonia.Svg no garantizan `feGaussianBlur`/`<pattern>`. Hay que aceptar «dos looks» o usar los PNG.
- El borde irregular de pergamino es una forma fija: al estirar el 9-slice se estira también la ondulación, y la zona decorada exige ≥72 px de lado. Los paneles de más de ~1000 px de ancho se verán con ondulación muy suave (no repetible por baldosa).
- La costura punteada de botones y herraje se estira en el centro del 9-slice (no mantiene el periodo de 6 px).
- El aro de bronce del icono con textura solo existe en PNG ≥64 px; la SVG y los tamaños 32/16 llevan bronce plano.
- Los sellos y el icono de app no están ajustados al píxel en 16 px (el 16×16 es una reducción del diseño simplificado; legible como sello rojo con arco, pero sin detalle).
- JetBrains Mono no incluye el glifo ✓ (U+2713); no usarlo en la UI de código (usar «OK» o un icono SVG).
- No hay variante @2x de los iconos de interfaz ni de los sellos: son vectoriales; los PNG de sellos se dan a 64 y 128.
- Las texturas se repiten visiblemente en pantallas muy grandes si se mosaican sin variación (el pergamino más que el cuero).
