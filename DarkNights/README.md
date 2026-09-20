# Dark Nights

In Dimraeth a night is no more dangerous than a day, and there is no blood moon. Dark Nights is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that makes nights harder and turns every Nth night into a blood moon. Everything is computed on the server, so players install nothing.

## What changes

**Every night**, from dusk to dawn: monsters deal more damage, the cooldown clocks of their special attacks run faster, more of the monsters the game can empower spawn empowered, and kills pay more XP, gold and drops. A chat line marks dusk and dawn.

**A blood moon** (every 7th night by default): a chat warning one game hour before dusk; at dusk the dials go up again, storm weather sets in everywhere, every dead monster returns at once, monsters that spawn or return that night are larger and carry a shield until dawn, and a player who lies down to sleep is told no. At dawn the weather that was there before comes back.

Monster health and movement speed are not changed. The game re-reads its health dial every second on the server and on every player's PC, so a dial that flips at dusk would re-scale living monsters and a health bar could disagree with the server.

## Settings

`BepInEx\config\com.humangenome.waygatemods.darknights.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed. A multiplier of 1 leaves the game's own value.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Night` | `Damage` | Damage monsters deal at night, 1 to 5 | 1.5 |
| `Night` | `Cooldowns` | How fast monster special attacks come back, 1 to 3 | 1.25 |
| `Night` | `Elites` | How much more often a monster spawns empowered, 1 to 3. Only the kinds the game can empower (see below) | 2 |
| `Night` | `XP` | XP from kills, 1 to 5 | 1.25 |
| `Night` | `Loot` | Drop chance and gold, 1 to 5 | 1.25 |
| `Night` | `Announce` | The dusk and dawn chat lines | true |
| `BloodMoon` | `EveryNights` | Every Nth night is a blood moon. 0 = never | 7 |
| `BloodMoon` | `Damage`, `Cooldowns`, `Elites`, `XP`, `Loot` | The same five dials under a blood moon | 2, 1.5, 3, 2, 2 |
| `BloodMoon` | `Storm` | Storm weather everywhere until dawn | true |
| `BloodMoon` | `RespawnAll` | Every dead monster returns when it rises | true |
| `BloodMoon` | `Giants` | Monsters that spawn or return that night are larger until dawn. The game draws them at one and a half times their size | true |
| `BloodMoon` | `Shield`, `ShieldPercent` | They carry a shield until dawn, this strong as a share of their health, 10 to 200. It runs down through the night | true, 50 |
| `BloodMoon` | `BlockSleep` | Nobody can sleep the blood moon away | true |
| `BloodMoon` | `WarnGameMinutes` | Warning before it rises, in game minutes. 0 = none | 60 |
| `Messages` | `Dusk`, `Dawn`, `BloodMoonWarning`, `BloodMoonRises`, `BloodMoonPasses`, `NoSleep` | The chat lines. Empty = say nothing | see the file |

## What the dials can and cannot do

- **Empowered monsters.** The game only ever empowers certain monster kinds (the ones whose kind name ends in Empowered), each with a chance of 33%. They stand at about one spawn point in ten and at none in the starting areas. The dial multiplies that chance, so 2 makes about two in three of them empowered and 3 makes all of them; ordinary kinds stay ordinary. A monster rolls when it spawns and when it returns from death, so the dial shows on monsters that arrive during the night.
- **Special attacks.** The dial makes the cooldown clocks of monster special attacks run that many times as fast (measured: 1.0, 1.5 and 3.0 seconds of cooldown per second at 1, 1.5 and 3). How often a monster actually attacks also follows the game's own combat pace: a mini-boss whose cooldowns ran three times as fast hit a player 15 times in 75 seconds instead of 13.
- **Larger monsters.** The game draws a monster that carries its large-size effect at one and a half times its size. That size is fixed in the game; it cannot be chosen.
- **Shields.** The shield is the one the game's own shield spells give: it absorbs damage before health does and runs down to nothing over its time, which here is the time left until dawn. At dawn the mod takes the size and what is left of the shield off again, also when the night was slept away.
- **Gold and drops.** The gold dial multiplies the gold a kill drops; a drop chance is multiplied and stops at 100%. Many kinds in the starting areas drop no gold at all.

## What it remembers

`BepInEx\config\HumanGenome-DarkNights\nights.txt` holds the number of nights the server has seen and whether it stopped in the middle of one, so the count to the next blood moon survives a restart and a server that restarts under a blood moon is still under it. Nothing is written to the world save or to a character. With the mod removed the game reads its own dials again.

## With other mods

[Server Multipliers](../ServerMultipliers/) scales some of the same dials. Both apply: XP 2 there and XP 1.25 here is 2.5 at night.

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-DarkNights\` under the server's game folder, so it holds `DarkNights.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

The game keeps its difficulty dials in `DifficultyManager` and reads them on the server at the moment they matter: `GetMonsterDamageDealtMultiplier` and `GetCooldownRecoveryMultiplier` in combat, `GetXPMultiplier`, `GetGoldMultiplier` and `GetLootChanceMultiplier` when a kill is paid out. The mod multiplies those results while `TimeManager.IsNight` is true. The empowerment roll (`EmpowermentManager.TryRollEmpowerment`) does not go through a getter: it reads its chance dial straight off the object `DifficultyManager.DialsFor` returns, which the game shares between all monsters of a kind. That object is never edited; at night a postfix on `DialsFor` hands the caller a copy with that one value scaled. Under a blood moon a prefix on `Monster.ClearEmpowerment`, which the game calls right before every empowerment roll (at set-up and on the return from death), notes the monster, and a second later the mod adds the `LargeSize` effect and the `ShieldDownOverTime` effect through `Effects.AddEffectToObject`, the way the game's own shield spells do, with the time left until dawn as their time. The blood moon calls the game's own `MonsterManager.RespawnAllDeadMonsters` and `WeatherManager.ChangeWeather`, and a prefix on `SleepManager.RegisterSleepServerRpc` skips the registration. Chat lines go out as server messages.

If a game update removes a method the mod depends on, the mod switches the affected feature off, or itself off, and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.10.

## Changes

**1.0.1**
- The two empowered-monster dials did nothing in 1.0.0: they scaled a game value that the game never reads. They now scale the chance the game's roll uses. Their range is 1 to 3, because 3 already empowers every monster the game can empower.
- The blood moon's shield set no shield in 1.0.0. It is now the game's own spell shield, sized by the new `ShieldPercent`, and it lasts until dawn.
- `GiantSize` is gone: the game draws every larger monster at one and a half times its size whatever number is given.
- Monsters that RETURN from death under a blood moon are made larger and shielded as well, and both end at dawn.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
