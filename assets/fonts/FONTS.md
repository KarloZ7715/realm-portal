# Tipografías de Realm Portal (dirección B)

Descargadas el 2026-10-17 del repositorio oficial **google/fonts** (carpeta `ofl/`). Todas bajo **SIL Open Font License 1.1** (https://openfontlicense.org); el `OFL.txt` de cada familia (copiado del repo) está junto a sus TTF. Se pueden usar, incrustar y redistribuir con la app; no se pueden vender por separado, y las versiones modificadas no pueden usar el «Reserved Font Name» cuando existe.

| Familia | Archivos TTF incluidos | Copyright (línea 1 del OFL.txt) | Reserved Font Name | URL de origen |
|---|---|---|---|---|
| Almendra | `Almendra-Bold.ttf` (35.8 KB) | © 2011-2012 Ana Sanfelippo | «Almendra» | https://github.com/google/fonts/tree/main/ofl/almendra |
| Marcellus SC | `MarcellusSC-Regular.ttf` (46.8 KB) | © 2012 Brian J. Bonislawsky DBA Astigmatic (AOETI) | «Marcellus» | https://github.com/google/fonts/tree/main/ofl/marcellussc |
| Alegreya | `Alegreya-Bold.ttf` (262.1 KB), `Alegreya-Medium.ttf` (262.3 KB), `Alegreya-Regular.ttf` (261.7 KB), `Alegreya[wght].ttf` (415.3 KB) | © 2011 The Alegreya Project Authors | ninguno | https://github.com/google/fonts/tree/main/ofl/alegreya |
| Alegreya Sans | `AlegreyaSans-Bold.ttf` (260.6 KB), `AlegreyaSans-Medium.ttf` (258.4 KB), `AlegreyaSans-Regular.ttf` (257.6 KB) | © 2013 The Alegreya Sans Project Authors | ninguno | https://github.com/google/fonts/tree/main/ofl/alegreyasans |
| JetBrains Mono | `JetBrainsMono-Regular.ttf` (112.5 KB), `JetBrainsMono[wght].ttf` (182.8 KB) | © 2020 The JetBrains Mono Project Authors | ninguno | https://github.com/google/fonts/tree/main/ofl/jetbrainsmono |

Archivos fuente directos: `https://github.com/google/fonts/raw/main/ofl/<carpeta>/<archivo>`.

## Notas de origen y modificación
- **Almendra Bold** y **Marcellus SC Regular**: sin modificar, tal cual en el repo (tienen Reserved Font Name; no renombrar ni editar).
- **Alegreya** y **JetBrains Mono**: en `google/fonts` solo existen como fuentes variables (`Alegreya[wght].ttf`, `JetBrainsMono[wght].ttf`). Se conservan los originales y, para facilitar el uso en Avalonia (que trabaja mejor con pesos estáticos), se generaron instancias estáticas con fontTools `varLib.instancer` (Alegreya 400/500/700 → `Alegreya-Regular/Medium/Bold.ttf`; JetBrains Mono 400 → `JetBrainsMono-Regular.ttf`). Son obras derivadas permitidas por la OFL porque estas familias no declaran Reserved Font Name; su OFL.txt acompaña los archivos. Aviso: el nombre interno de familia de `Alegreya-Medium.ttf` es «Alegreya Medium» (estilo Regular); si Avalonia no resuelve el peso 500, referenciarla por ese nombre.
- **Alegreya Sans** (Regular/Medium/Bold): estáticas originales del repo, sin modificar.

## Uso en la maqueta (según `DESIGN-TOKENS-B.md`)
Almendra Bold = títulos; Marcellus SC = etiquetas/botones; Alegreya = texto de lectura; Alegreya Sans = texto de interfaz pequeño y estados; JetBrains Mono = código y rutas.

## Comprobación de glifos
Verificado con fontTools (cmap) en **cada TTF** de esta carpeta: presentes `á é í ó ú ñ ¿ ¡ ü` y también `Á É Í Ó Ú Ñ Ü « » — ·`. Prueba visual: `preview-composicion.png` renderiza «ñandú, árbol, cigüeña, ¿listo? ¡sí!» con Alegreya. Excepción: JetBrains Mono no tiene `✓` (U+2713).
