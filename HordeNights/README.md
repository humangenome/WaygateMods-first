# Horde Nights

Dimraeth has no wave events: monsters stand where the area put them and wait. Horde Nights is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that adds them. Every Nth night, or when an admin types `/horde start`, a horde comes for the players: a warning in chat, then wave after wave of monsters that appear round the defenders and come straight at them, healing and a gold drop between waves, and a last wave with a leader. When it is over the mod removes every monster it made. Everything runs on the server, so players install nothing.

## What players see

- The warning: `A goblin warband is closing in on Wildwood Forest Part I. It arrives in 120 seconds.`
- Each wave: `Wave 2 of 5: goblin raiders.` The monsters appear with the game's own spawn effect, a short walk from a defender, already hunting.
- A cleared wave: `Wave 2 cleared. 40 gold for each defender.` Defenders are healed and a gold pile lands beside each of them.
- The end: `The horde is broken. Wildwood Forest Part I is safe. 250 gold for each defender. Top slayer: Wren with 14.`, or `The horde withdraws from ...` when its time runs out, or `The horde has overrun ...` when every defender is dead.
- A player who walks into the fight is told which wave is on.

The horde comes to the area with the most living players in it. Defenders are the players in that area. Areas on the `Exclude` list (the village, the guild hall and player bases by default) and areas locked by a bounty in progress never get one.

Horde monsters are ordinary monsters of the game: kills pay the game's own XP and, unless switched off, the game's own loot. Dial mods such as Dark Nights apply to them like to any other monster.

## What players type

| Command | Who | Answer |
|---|---|---|
| `/horde` | everyone | Whether a horde is on its way or under way, or when the next one is due |
| `/horde start` | admins | Calls a horde to the area the admin is standing in |
| `/horde start wolves` | admins | The same with a theme: `goblins`, `wolves`, `droops`, `corrupted` |
| `/horde stop` | admins | Calls the horde off and removes its monsters |

An answer is shown only to the player who asked, and a `/horde` line is not relayed to other players. Admins are the character names on the `Admins` setting. A character name is chosen by the player, so anyone who takes a listed name can use the commands; list a character id instead of a name for a strict check. With `Admins` empty nobody can call a horde from chat.

An owner with file access can call one without a character: write `start`, `start wolves`, `start wolves TheLostCaverns` or `stop` into `BepInEx\config\HumanGenome-HordeNights\command.txt`. The file is read within two seconds and deleted.

## Themes

| Theme | Waves, first to last |
|---|---|
| Droops | drooplets, droops, spitters, the swarm, the monstrous droop |
| Goblins | scouts, raiders, alchemists, veterans, the warband's champions |
| Wolves | lone wolves, the pack, aetherfangs, the aetherfang pack, the alpha |
| Corrupted | corrupted droops, corrupted wolves, rotting treants, the rot spreads, the corrupted treant |

The monsters are the game's own, at the game's own strength, whatever level the defenders are. Droops are the weakest (a drooplet has about 20 health, a droop about 65, the monstrous droop about 1,700). A goblin ripper has about 120, the later goblins about 1,000 to 3,000, a wolf about 320 and the alpha about 3,400. Pick the theme for your group; `Random` takes any of the four.

With fewer than five waves the mod keeps the first and the last row and spreads the rest; with more it repeats the last row, harder each time. `CustomWaves` replaces the theme with your own list, by the game's monster names: `GoblinRipper x4, GoblinFlaskrat x2 | GoblinBasher x3 | MonstrousDroop x1`. A name the game does not know is skipped with one line in the log. Monsters of different kinds may fight each other as they do in the game (a corrupted monster and a goblin do), and the game credits such a kill to the nearest player. A theme named on `/horde start` wins over `CustomWaves`.

## Settings

`BepInEx\config\com.humangenome.waygatemods.hordenights.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed. A horde that is already under way keeps the waves it started with; the other settings apply from the next wave.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Schedule` | `EveryNights` | A horde every Nth night. 0 = only when an admin calls one | 3 |
| `Schedule` | `AtHour` | Game hour of the warning, 0 to 23. The game calls it night from 20 | 21 |
| `Schedule` | `WarnSeconds` | Seconds between the warning and the first wave, 0 to 600 | 120 |
| `Schedule` | `MinPlayers` | Players online for a scheduled horde, 1 to 16 | 1 |
| `Schedule` | `WithBloodMoon` | With Dark Nights installed: also on every blood moon | false |
| `Horde` | `Theme` | `Droops`, `Goblins`, `Wolves`, `Corrupted`, or `Random` for a different one each time | Droops |
| `Horde` | `CustomWaves` | Your own waves instead of a theme | empty |
| `Horde` | `Waves` | Waves per horde, 1 to 10 | 5 |
| `Horde` | `Size` | Monsters per wave as a multiple of the theme's numbers, 0.5 to 3 | 1 |
| `Horde` | `PerExtraPlayer` | Share of monsters added per defender after the first, 0 to 1 | 0.5 |
| `Horde` | `MaxAlive` | Most horde monsters alive at once, 4 to 40. The rest of a wave arrives as monsters die | 10 |
| `Horde` | `WaveSeconds` | The next wave arrives after this long even when the last still stands, 30 to 600 | 150 |
| `Horde` | `RestSeconds` | Pause after a cleared wave, 5 to 120 | 15 |
| `Horde` | `MaxMinutes` | The horde gives up after this long, 5 to 60 | 20 |
| `Horde` | `HealthPerWave` | Monster health added per wave after the first, 0 to 1 | 0.1 |
| `Horde` | `DamagePerWave` | Monster damage added per wave after the first, 0 to 1 | 0.1 |
| `Horde` | `SpawnDistance` | How far from a defender monsters appear, 3 to 25 | 12 |
| `Horde` | `HealBetweenWaves` | Defenders are healed when a wave is cleared | true |
| `Horde` | `MonsterLoot` | Horde monsters drop their normal loot | true |
| `Rewards` | `GoldPerWave` | Gold dropped beside each defender per cleared wave, 0 to 1000 | 40 |
| `Rewards` | `GoldOnVictory` | Gold dropped beside each defender for the last wave, 0 to 5000 | 250 |
| `Areas` | `Exclude` | Areas a horde never comes to, by the game's area names | EarlwoodVillage, GuildHall, PlayerBase |
| `Commands` | `Admins` | Character names or character ids allowed `/horde start` and `/horde stop` | empty |
| `Messages` | `Warning`, `Wave`, `WaveCleared`, `Victory`, `Withdraw`, `Overrun` | The chat lines. Empty = none. `{horde}`, `{area}`, `{seconds}`, `{wave}`, `{waves}`, `{name}` and `{gold}` are filled in | see the file |

A night is counted each time the game clock passes `AtHour`. When the clock jumps past the whole hour (the players slept, or an admin set the time) the night is counted and no horde starts.

## What it costs the server

Every living monster is simulated by the server, so a horde costs CPU while it lasts and nothing before it. Measured on a WaygateServer 0.3.9 host (game build 25350646, 30 frames a second, two players in the area, goblins that nobody was killing, 120 second samples):

| | CPU, share of one core | Memory |
|---|---|---|
| two players, no horde | 92% | 7.77 GB |
| 12 horde monsters alive and fighting | 129% | +0.12 GB |
| 24 | 170% | +0.16 GB |
| 40 | 201% | +0.26 GB |

That is about 3 percent of a core per living horde monster. `MaxAlive` is the hard cap on how many are alive at once, whatever the wave size and the number of defenders, and a whole horde never makes more than 300. The default of 10 adds about a third of a core for as long as the horde lasts. On a server that shares its machine with others, keep it near the default. In the minute after that horde the server still read 115% (35 corpses and their drops were on the ground); the memory is given back slowly.

## The file

`BepInEx\config\HumanGenome-HordeNights\horde.txt` holds two lines: how many horde hours the server has seen (the schedule counts from it) and whether a horde was under way. Horde monsters are spawned as transient, which the game never writes to the world save, so a server that stops in the middle of a horde starts again without them; the mod says so in one line and carries on with the schedule. On every start and at the end of every horde it also removes any monster that still carries its name.

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-HordeNights\` under the server's game folder, so it holds `HordeNights.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

The game ships a wave engine (`ArenaManager`) for one arena, but no manager object exists in the world, so the mod runs its own loop in that engine's shape. Monsters are made by `SceneHandler.SpawnMonsterInstance`, the runtime spawner the game's wave engine, summons and spawn spells call, with its spawn effect, its auto-aggro and its transient flag on; a wave's health and damage go through `Monster.ApplySpawnStatProfile` in the spawner's own configure step, as the engine does. Spawn points are sampled on the area's walk mesh in a ring round a living defender, and only a point with a clear walk line to that defender is used. Each monster is handed to the game's own aggro call (`SceneHandler.AggroMonster`). Some monsters of the game wait for their prey to come to them (wolves and droops do, goblins do not): a horde monster that stands still away from every defender is walked over with the game's escort behaviour (`MonsterBehaviour.SetEscortTarget`) and released into the fight when it arrives; one that still does not move is put down a few steps from the defender. A monster is counted dead when the game says it is, its corpse is removed after 25 seconds, and the end of a horde despawns whatever is left. Gold goes through the game's world-gold call (`ServerRPC.AddGoldToWorldServerRpc`), so whoever picks a pile up is paid by the game; healing sets health, concentration and stamina to their maximum. The top slayer is read after `QuestManager.RegisterMonsterDeath`, the game's own kill credit. Chat commands are read in a prefix on `ChatSystem.SendMessageToServerServerRpc`. Nothing is written to a character or to the world save, and no XP is granted outside the game's own kill path.

If a game update removes a method the mod depends on, the mod switches the affected feature off, or itself off, and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.9, with headless test clients: monsters of all four themes spawn and are seen by clients, monsters and players kill each other with the game's own XP and gold paid, the cap on living monsters holds, every ending removes every horde monster, and a server ended in the middle of a horde starts again without them. Not yet looked at on a real player's screen.

## With other mods

[Dark Nights](../DarkNights/) scales what monsters deal and pay at night, horde monsters included. With `WithBloodMoon` on, a horde also comes on each of its blood moons; the mod reads Dark Nights' own `nights.txt` and needs nothing else from it. [Chronicle](../Chronicle/) counts horde kills like any other kill.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
