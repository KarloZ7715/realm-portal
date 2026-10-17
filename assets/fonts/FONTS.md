# Realm Portal typefaces (direction B)

Downloaded on 2026-10-17 from the official **google/fonts** repository (`ofl/` folder). All are licensed under the **SIL Open Font License 1.1** (https://openfontlicense.org); each family's `OFL.txt` (copied from the repository) sits next to its TTFs. They may be used, embedded and redistributed with the app; they may not be sold on their own, and modified versions may not use the "Reserved Font Name" when one exists.

| Family | Included TTF files | Copyright (line 1 of OFL.txt) | Reserved Font Name | Source URL |
|---|---|---|---|---|
| Almendra | `Almendra-Bold.ttf` (35.8 KB) | © 2011-2012 Ana Sanfelippo | "Almendra" | https://github.com/google/fonts/tree/main/ofl/almendra |
| Marcellus SC | `MarcellusSC-Regular.ttf` (46.8 KB) | © 2012 Brian J. Bonislawsky DBA Astigmatic (AOETI) | "Marcellus" | https://github.com/google/fonts/tree/main/ofl/marcellussc |
| Alegreya | `Alegreya-Bold.ttf` (262.1 KB), `Alegreya-Medium.ttf` (262.3 KB), `Alegreya-Regular.ttf` (261.7 KB), `Alegreya[wght].ttf` (415.3 KB) | © 2011 The Alegreya Project Authors | none | https://github.com/google/fonts/tree/main/ofl/alegreya |
| Alegreya Sans | `AlegreyaSans-Bold.ttf` (260.6 KB), `AlegreyaSans-Medium.ttf` (258.4 KB), `AlegreyaSans-Regular.ttf` (257.6 KB) | © 2013 The Alegreya Sans Project Authors | none | https://github.com/google/fonts/tree/main/ofl/alegreyasans |
| JetBrains Mono | `JetBrainsMono-Regular.ttf` (112.5 KB), `JetBrainsMono[wght].ttf` (182.8 KB) | © 2020 The JetBrains Mono Project Authors | none | https://github.com/google/fonts/tree/main/ofl/jetbrainsmono |

Direct source files: `https://github.com/google/fonts/raw/main/ofl/<folder>/<file>`.

## Source and modification notes
- **Almendra Bold** and **Marcellus SC Regular**: unmodified, exactly as in the repository (they have a Reserved Font Name; do not rename or edit).
- **Alegreya** and **JetBrains Mono**: in `google/fonts` they exist only as variable fonts (`Alegreya[wght].ttf`, `JetBrainsMono[wght].ttf`). The originals are kept and, to make them easier to use in Avalonia (which works better with static weights), static instances were generated with fontTools `varLib.instancer` (Alegreya 400/500/700 → `Alegreya-Regular/Medium/Bold.ttf`; JetBrains Mono 400 → `JetBrainsMono-Regular.ttf`). They are derivative works permitted by the OFL because these families declare no Reserved Font Name; their OFL.txt accompanies the files. Warning: the internal family name of `Alegreya-Medium.ttf` is "Alegreya Medium" (Regular style); if Avalonia does not resolve weight 500, reference it by that name.
- **Alegreya Sans** (Regular/Medium/Bold): original static files from the repository, unmodified.

## Intended use in the design
Almendra Bold = titles; Marcellus SC = labels/buttons; Alegreya = reading text; Alegreya Sans = small interface text and states; JetBrains Mono = addresses, paths and code.

## Glyph check
Verified with fontTools (cmap) in **every TTF** in this folder: `á é í ó ú ñ ¿ ¡ ü` are present, as well as `Á É Í Ó Ú Ñ Ü « » — ·` (needed for the Spanish UI). Exception: JetBrains Mono has no `✓` (U+2713).
