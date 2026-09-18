# Waygate Travel

Dimraeth has no command to teleport to a friend, to pull a co-op party together, or to go back to where you were. Waygate Travel is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that adds typed teleport commands for co-op groups. Everything runs on the server, so players install nothing.

## What players type

| Command | What happens |
|---|---|
| `/summon` | Every member of your party is asked to come to you |
| `/tpa <name>` | One player is asked if you may come to them |
| `/tpaccept`, `/tpdeny` | Answer the request you were sent. A request waits 60 seconds |
| `/home` | You go to your respawn point |
| `/back` | You go back to where you were before your last trip |
| `/where <name>` | Which area a player is in |
| `/travel` | The list of commands |

An answer is shown only to the player it is for, and a line the mod answers is not relayed to other players. `/party` and `/reply` stay the game's own.

## Rules

- Each command has a cooldown per character (300, 120, 300 and 60 seconds for `/summon`, `/tpa`, `/home` and `/back`).
- No travel while dead, while already travelling, or out of a fight. No travel to a player who is dead or in a fight.
- No travel into an area that a bounty in progress has locked.
- `/back` remembers one place per character until the server restarts.

## Settings

`BepInEx\config\com.humangenome.waygatemods.waygatetravel.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Commands` | `Summon`, `Tpa`, `Home`, `Back`, `Where` | Switch a command on or off | true |
| `Cooldowns` | `SummonSeconds`, `TpaSeconds`, `HomeSeconds`, `BackSeconds` | Seconds before the same character can use it again, 0 to 3600 | 300, 120, 300, 60 |
| `Rules` | `RequestTimeoutSeconds` | How long a request waits, 15 to 300 | 60 |
| `Rules` | `RefuseInCombat` | No travel out of a fight or into one | true |
| `Rules` | `RefuseLockedAreas` | No travel into an area a bounty has locked | true |
| `Rules` | `NoCooldownNames` | Character names that skip cooldowns, separated by commas | empty |
| `Rules` | `GamePrompt` | `/summon` also shows the game's own accept or decline banner to party members | false |

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-WaygateTravel\` under the server's game folder, so it holds `WaygateTravel.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

Commands are read in a prefix on `ChatSystem.SendMessageToServerServerRpc`. A move is `SpawnManager.LoadPlayerLocationClientRpc(scene, position, clientId)`, the call the game itself uses to place a player when they join or respawn; it is addressed to one player, whose game loads the area and places them. `/home` reads the respawn point the world keeps for the character (`ServerPlayerData.SpawnArea` and `SpawnLocation`). "In a fight" is the flag the game raises on a player for its battle music. Nothing is written to a character; the world saves positions as it always does. The mod keeps requests, cooldowns and `/back` places in memory only.

If a game update removes a method the mod depends on, the mod switches itself off and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.7.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
