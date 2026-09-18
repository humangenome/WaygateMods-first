# Dark Nights

In Dimraeth a night is no more dangerous than a day, and there is no blood moon. Dark Nights is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that makes nights harder and turns every Nth night into a blood moon. Everything is computed on the server, so players install nothing.

## What changes

**Every night**, from dusk to dawn: monsters deal more damage, their special attacks come back faster, more of them spawn empowered, and kills pay more XP, gold and drops. A chat line marks dusk and dawn.

**A blood moon** (every 7th night by default): a chat warning one game hour before dusk; at dusk the dials go up again, storm weather sets in everywhere, every dead monster returns at once, monsters that spawn that night are larger and start with a shield, and a player who lies down to sleep is told no. At dawn the weather that was there before comes back.

Monster health and movement speed are not changed. The game re-reads its health dial every second on the server and on every player's PC, so a dial that flips at dusk would re-scale living monsters and a health bar could disagree with the server.

## Settings

`BepInEx\config\com.humangenome.waygatemods.darknights.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed. A multiplier of 1 leaves the game's own value.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Night` | `Damage` | Damage monsters deal at night, 1 to 5 | 1.5 |
| `Night` | `Cooldowns` | How fast monster special attacks come back, 1 to 3 | 1.25 |
| `Night` | `Elites` | How much more often a monster spawns empowered, 1 to 10 | 2 |
| `Night` | `XP` | XP from kills, 1 to 5 | 1.25 |
| `Night` | `Loot` | Drop chance and gold, 1 to 5 | 1.25 |
| `Night` | `Announce` | The dusk and dawn chat lines | true |
| `BloodMoon` | `EveryNights` | Every Nth night is a blood moon. 0 = never | 7 |
| `BloodMoon` | `Damage`, `Cooldowns`, `Elites`, `XP`, `Loot` | The same five dials under a blood moon | 2, 1.5, 4, 2, 2 |
| `BloodMoon` | `Storm` | Storm weather everywhere until dawn | true |
| `BloodMoon` | `RespawnAll` | Every dead monster returns when it rises | true |
| `BloodMoon` | `Giants`, `GiantSize` | Monsters that spawn that night are larger, 1.1 to 2 | true, 1.4 |
| `BloodMoon` | `Shield` | Monsters that spawn that night start with a shield | true |
| `BloodMoon` | `BlockSleep` | Nobody can sleep the blood moon away | true |
| `BloodMoon` | `WarnGameMinutes` | Warning before it rises, in game minutes. 0 = none | 60 |
| `Messages` | `Dusk`, `Dawn`, `BloodMoonWarning`, `BloodMoonRises`, `BloodMoonPasses`, `NoSleep` | The chat lines. Empty = say nothing | see the file |

## What it remembers

`BepInEx\config\HumanGenome-DarkNights\nights.txt` holds the number of nights the server has seen and whether it stopped in the middle of one, so the count to the next blood moon survives a restart and a server that restarts under a blood moon is still under it. Nothing is written to the world save or to a character. With the mod removed the game reads its own dials again.

## With other mods

[Server Multipliers](../ServerMultipliers/) scales some of the same dials. Both apply: XP 2 there and XP 1.25 here is 2.5 at night.

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-DarkNights\` under the server's game folder, so it holds `DarkNights.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

The game keeps its difficulty dials in `DifficultyManager` and reads them on the server at the moment they matter: `GetMonsterDamageDealtMultiplier` and `GetCooldownRecoveryMultiplier` in combat, `GetEmpowermentChanceMultiplier` when a monster spawns or respawns, `GetXPMultiplier`, `GetGoldMultiplier` and `GetLootChanceMultiplier` when a kill is paid out, `GetExtraStartingEffects` when a monster is set up. The mod multiplies those results while `TimeManager.IsNight` is true and adds `LargeSize` and `Shield` starting effects under a blood moon (in a new list; the game's own list is never edited). The blood moon calls the game's own `MonsterManager.RespawnAllDeadMonsters` and `WeatherManager.ChangeWeather`, and a prefix on `SleepManager.RegisterSleepServerRpc` skips the registration. Chat lines go out as server messages.

If a game update removes a method the mod depends on, the mod switches the affected feature off, or itself off, and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.7.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
