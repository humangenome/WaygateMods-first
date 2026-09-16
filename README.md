# WaygateMods-first

The first three mods for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer), each its own project with its own README and MIT license. They are listed in the [WaygateMods](https://github.com/HumanGenome/WaygateMods) registry, which is where the Waygate app and hosting panels read them from.

| Mod | Side | What it does |
|---|---|---|
| [ServerMultipliers](ServerMultipliers/) | server | XP, gold, loot chance, monster respawn time and day length multipliers |
| [MotdAnnounce](MotdAnnounce/) | server | A welcome line in chat on join, and a repeating announcement |
| [ClientHud](ClientHud/) | client | Server name and player count in the corner of the screen |

## Build and package

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (the interop assemblies are referenced from there and never copied here), then:

    python3 tools/package.py

That builds every mod, writes each dll's sha256 into its `waygate-mod.json`, zips each mod into `dist/` (reproducible: fixed timestamps, sorted entries) and writes `dist/entries.json`, the registry entries for the built zips. A release of a mod is the GitHub release `<id>-v<version>` on this repository carrying that zip; the registry entry names the zip's URL and sha256.

## Mod package layout

A mod zip holds, at its root: `waygate-mod.json`, the plugin dll the manifest names, `README.md` and `LICENSE`. It is installed by extracting it into `BepInEx\plugins\mods\<id>\`. The manifest fields are documented in the registry's [CONTRIBUTING](https://github.com/HumanGenome/WaygateMods/blob/main/CONTRIBUTING.md).
