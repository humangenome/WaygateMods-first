# Corrupted Realm

In Dimraeth an empowered monster is a rare sight, a kill of one passes without a word, and a full server meets the same monsters as a lone player. Corrupted Realm is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that turns the world into an elite hunt: empowered monsters are several times as common, stronger and better paid, a kill is announced in chat, the server can send a named champion after a random player, and monster damage can rise with the number of players connected. Everything is computed on the server, so players install nothing.

## What changes

**Empowered monsters** are the game's own elites: a monster that carries one of the game's empowerments (Brutal, Venomous, Swift, Frostbound, Flamecaller, Storm Aura, Spiteful, Arcane Shielded, Blighted, Mortar, Unyielding) and more health with it (a Wildwood Wolf's doubles). The game lets only some monster types be empowered (about one spawn point in ten across the world, none in the starting areas) and gives each of those a 33% chance. The mod raises that chance threefold by default, which empowers nearly every one of them, and gives every other hostile monster a 5% chance of its own. Bosses, minibosses, monsters with an event of the world's story on their death or with boss stages, harmless animals and things that do not move are left out, and by default so is the area new characters start in. A boss or miniboss is outside the mod altogether: never rolled, never levelled, never paid more, even the two minibosses the game itself can empower.

Each empowered monster gains 3 levels, attacks 1.2 times as fast and carries the game's armor-up effect at 1.25, and its kill pays 1.5 times the XP, twice the gold and twice the drop chance, on top of what the game already adds for an empowered kill. When the empowerment ends (at death) the levels and the effects leave with it, so a monster that returns as an ordinary one is ordinary.

**Kill lines.** `Wren slew a Brutal Drooplet.` goes to everyone when a player kills an empowered monster.

**Champions** (off until `Champion/EveryMinutes` is set). Every N minutes the server picks a connected player who is alive and not in a safe area, and spawns an empowered monster of one of the configured types a few steps away, already hunting them: `A champion has come for Wren in The Lost Caverns: Morgath the Brutal, a Wildwood Wolf Alpha.` Its death is announced and counted. A champion nobody is fighting leaves after 20 minutes. Champions are not saved with the world. A champion is a monster the mod adds, so it stays out of the world's story: a boss, a miniboss, a monster with a story event on its death or with boss stages cannot be one (the game ties those to the kind of monster, not to the one it placed), and while the game runs a champion's death or one of its reactions, its call that starts a story event is skipped. A kill of a champion counts for "slay N of X" quest steps like any other monster of its kind. The clock runs only while somebody is connected and carries over a restart.

**Drops.** The game removes a drop from the ground after 30 minutes, and every drop left lying costs the server CPU. Because the mod raises what empowered monsters drop, it notes the drops that appear in an empowered monster's own drop checks and removes what nobody picked up after 10 minutes (`Cleanup/DropsAfterMinutes`). Drops of other monsters are never touched.

**Player count.** Each connected player beyond the first adds 10% to the damage hostile monsters deal (four players: 1.3 times). Set `Scaling/PerPlayer` to 0 to switch it off.

**Chat commands.** `/realm` tells the player who typed it what the server's settings are, how many empowered monsters and champions have been slain on it, and when the next champion is due. `/champion` or `/champion <MonsterType>` sends a champion after the caller; it works only for the character names listed in `Chat/AdminNames`. Answers reach only the player who typed the command. A character name is whatever a player called their character, so list names only your own group uses.

## Settings

`BepInEx\config\com.humangenome.waygatemods.corruptedrealm.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed. A changed level or effect setting applies to monsters empowered after the change. A multiplier of 1 leaves the game's own value.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Elites` | `Chance` | How much more often a monster the game can empower spawns empowered, 1 to 10, never past 100% | 3 |
| `Elites` | `WildChance` | Chance in percent that any other hostile monster spawns empowered, 0 to 25. 0 = never | 5 |
| `Elites` | `WildSkipScenes` | The game's scene names where those other monsters stay ordinary | `TheLostCaverns` |
| `Elites` | `BonusLevel` | Levels an empowered monster gains, 0 to 20 | 3 |
| `Elites` | `AttackSpeed` | The game's attack-speed-up effect on an empowered monster, 1 to 2. 1 = none | 1.2 |
| `Elites` | `Armor` | The game's armor-up effect on an empowered monster, 1 to 2. 1 = none | 1.25 |
| `Elites` | `ExtraEffects` | More effects, as `Effect:multiplier` or `Effect:multiplier:additive` entries separated by commas (`ResistanceUp:1.25,SpeedUp:1.1`). Names are the game's `Effect` names. `Invincibility`, `Unkillable`, `Invisibility`, `Shield` and every `...OverTime` effect are refused | empty |
| `Elites` | `XP`, `Gold`, `Loot` | Pay for an empowered kill, 1 to 5 each | 1.5, 2, 2 |
| `Champion` | `EveryMinutes` | A champion this often, 0 to 240. 0 = never | 0 |
| `Champion` | `Types` | The game's `MonsterType` names a champion can be. Boss and story kinds are refused | `GoblinBasher,GoblinBasherT2,GoblinRipperT2,CorruptedWildwoodWolf` |
| `Champion` | `MaxAlive` | Champions out at the same time, 1 to 5 | 2 |
| `Champion` | `LeaveAfterMinutes` | An unfought champion leaves after this long, 5 to 120 | 20 |
| `Champion` | `SafeScenes` | The game's scene names where no champion comes for a player | `EarlwoodVillage,GuildHall,PlayerBase` |
| `Scaling` | `PerPlayer` | Monster damage added per connected player beyond the first, 0 to 0.5 | 0.1 |
| `Cleanup` | `DropsAfterMinutes` | Unclaimed drops of empowered monsters are removed after this long, 0 to 30. 0 = leave it to the game (30 minutes) | 10 |
| `Announce` | `EliteKills`, `Champions` | The chat lines | true, true |
| `Chat` | `Commands` | `/realm` and `/champion` | true |
| `Chat` | `AdminNames` | Character names that may type `/champion`, separated by commas | empty |
| `Messages` | `EliteKill`, `ChampionAppears`, `ChampionSlain`, `ChampionLeaves` | The chat lines, with `{player}`, `{elite}`, `{champion}`, `{monster}` and `{area}` filled in. Empty = say nothing | see the file |

## What it remembers

`BepInEx\config\HumanGenome-CorruptedRealm\realm.txt` holds the number of empowered monsters and champions slain, the number of champions sent and the seconds left until the next one. Nothing is written to the world save or to a character. With the mod removed the game reads its own dials again and no monster keeps anything of the mod's: levels and effects live only on a monster that is empowered at that moment, in memory.

## With other mods

[Dark Nights](../DarkNights/) and [Server Multipliers](../ServerMultipliers/) scale some of the same pay dials. All apply: XP 2 there and XP 1.5 here is 3 for an empowered kill. The second roll is sized from `GetEmpowermentChanceMultiplier`, so another mod's share of the elite chance counts in it. [Chronicle](../Chronicle/) counts the same empowered kills in its own record.

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-CorruptedRealm\` under the server's game folder, so it holds `CorruptedRealm.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

The game rolls for an empowerment when a monster is set up and again each time it returns from death (`EmpowermentManager.TryRollEmpowerment`). That roll reads its chance dial directly, not through `DifficultyManager.GetEmpowermentChanceMultiplier`, so a patch on the getter never reaches it (read in the compiled game: the getter has no caller). The mod leaves the game's roll alone and rolls once more, about a second later, for a monster that stayed ordinary, is not in a fight, and whose settings allow an empowerment (`CanBeEmpowered`, an `EmpowermentChance` above zero, a non-empty `EmpowermentPool`). The second chance is sized so that both rolls together come to the game's chance times `Elites/Chance`, capped at 100%; the empowerment is drawn from the monster's own pool (production entries only) and applied with the game's own `Monster.ApplyEmpowerment`, which is also what sends it to players. A monster the game never rolls for gets the `WildChance` roll instead, drawn from every empowerment the game has in production. The cue for the second roll is `Monster.ClearEmpowerment`, which the game calls right before each of its rolls.

After any `Monster.ApplyEmpowerment`, the game's or the mod's, the mod raises the monster's `Level` and adds its effects through the monster's own `Effects.AddEffectToObject`, recording them in the monster's list of empowerment effects, which is the list `Monster.ClearEmpowerment` removes. A prefix on `ClearEmpowerment` takes the levels back. (The monster types the game can empower are the `MonsterType` names that end in `Empowered`; they are ordinary monsters until a roll succeeds.)

The pay dials (`GetXPMultiplier`, `GetGoldMultiplier`, `GetLootChanceMultiplier`) are handed a monster's settings, not the monster, so the mod notes which monster is being paid for around `XP.CalculateXPGained`, `MonsterUtils.GoldDropCheck` and `MonsterUtils.ApplyDropChanceModifiers` and multiplies only when it is an empowered one. XP moves only through the game's own reward path. Kill credit comes from `QuestManager.RegisterMonsterDeath`, which the server calls for every monster death with the player who gets the credit. `GetMonsterDamageDealtMultiplier` carries the player-count scaling; monsters allied to a player are left alone.

A kind's ties to the story are read from the game's own prefab for it (`NetworkPrefabManager.MonsterPrefabs`: `MonsterConfiguration.EventOnDeath`, `DangerLevel`, `MonsterRank`, a `BossInvulnerability` component) before anything is spawned, and asked again of the spawned monster. The game starts a story event on behalf of one monster in `Monster.OnDeath`, in `MonsterBehaviour.ActivateReaction` and in a boss's stages; for a champion the first two are bracketed and `EventsManager.ActivateEventServerRpc` is skipped inside them (kinds with boss stages are never champions). If those patches cannot be applied on a game version, champions are switched off. Drops are told apart by listing the game's three sets of ground objects (`SimpleObject`, `RuneObject`, `GoldDropPickup`) before and after an empowered monster's `GoldDropCheck`, `RuneDropCheck` and `ItemDropCheck`; a noted drop is removed the way the game removes one, by destroying its object on the server.

A champion is `SceneHandler.SpawnMonsterInstance` (the game's runtime spawner) with auto-aggro and the transient flag, at a walkable point the game's own picker finds about seven units from the player, followed by `Monster.ApplyEmpowerment` with one of the game's production empowerments once the game has finished setting the monster up. The name a champion carries exists in the chat lines; the game does not send a monster's object name to players.

If a game update removes a method the mod depends on, the mod switches the affected feature off, or itself off, and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.9.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
