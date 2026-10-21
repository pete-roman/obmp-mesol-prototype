# Third-party notices

MESOL's own code, page and lore list are under the MIT License (see `LICENSE`). These parts are not, and keep their own terms. The page build (`web-src/build.mjs`) copies the same notices into the mod's `client/web/fonts/` and `client/web/licenses/`, so they travel with the mod.

| Part | Where | Author | Licence |
|---|---|---|---|
| Cinzel Bold | `web-src/assets/fonts/Cinzel-Bold.ttf` | The Cinzel Project Authors (Natanael Gama) | SIL Open Font License 1.1, `web-src/assets/fonts/OFL.txt` |
| Kingthings Petrock, Kingthings Petrock Light | `web-src/assets/fonts/Kingthings Petrock*.ttf` | Kevin King, kingthingsfonts.co.uk | Free for any use, files must stay unchanged, `web-src/assets/fonts/kingthingsEULA.txt` |

The Kingthings font files are the author's originals, byte for byte, as his EULA asks. Don't convert or subset them.

**The lore list** (`catalog/mesol-lore.json`): the summaries are original text, written for MESOL from research on the Unofficial Elder Scrolls Pages (UESP, https://en.uesp.net/). No UESP text is copied. Each entry's `sources` field links the pages it was researched from. The names of races, places, factions, gods and the rest come from The Elder Scrolls games and belong to ZeniMax Media Inc.

**RpRoll** (https://github.com/pete-roman/obmp-rproll, MIT) is a separate mod by the same author. MESOL builds against its source and needs it on the server, but includes none of its files.

Build and test tools (esbuild, puppeteer-core, xUnit, the OB:MP SDK and chat packages from NuGet) are used to build or test MESOL and are not part of this repository. The OB:MP SDK and chat packages are ReadyM's, under ReadyM's terms.
