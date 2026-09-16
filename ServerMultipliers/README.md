# Server Multipliers

A server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer). It scales five numbers the game normally fixes. Everything is computed on the server, so players install nothing.

| Key | What it scales | Default |
|---|---|---|
| `XP` | XP granted for a monster kill | 1 |
| `Gold` | Gold a monster drops | 1 |
| `LootChance` | Chance of item, rune and gold drops | 1 |
| `MonsterRespawnTime` | Time before a killed monster returns (0.5 = twice as fast) | 1 |
| `DayLength` | Length of a game day (2 = twice as long) | 1 |

Each value is a multiplier from 0.1 to 20. A value of 1 leaves the game untouched.

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-ServerMultipliers\` under the server's game folder, so it holds `ServerMultipliers.dll` and `waygate-mod.json`. Start the server once; it writes `BepInEx\config\com.humangenome.waygatemods.servermultipliers.cfg` with the keys above. Edit that file and restart.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it.

## How it works

The game keeps its difficulty dials in `DifficultyManager` and reads `GetXPMultiplier`, `GetGoldMultiplier`, `GetLootChanceMultiplier` and `GetRespawnTimeMultiplier` on the server at the moment a kill or drop is resolved. The mod multiplies those results. Day length scales `GameConfig.TimeSpeed`, the seconds the world clock waits between game minutes, once when the server's clock starts. With `LogApplied = true` (the default) every scaled XP and gold value is written to `BepInEx\LogOutput.log`.

Tested on game build 25335390.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository.

## License

MIT.
