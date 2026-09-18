# Chronicle

Dimraeth keeps no record of what players do on a server: there are no kill counts, no death counts and no leaderboard. Chronicle is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that adds them. It counts monsters slain, elite and boss kills, deaths, bounties and time played for every character, answers `/stats` and `/top` in chat, and posts one line at dawn about the day that ended. Everything is read on the server, so players install nothing.

## What players type

| Command | Answer |
|---|---|
| `/stats` | Your own numbers: `Wren: 412 kills (31 elite, 2 boss), 3 deaths, 5 bounties, 6h 12m played.` |
| `/stats <name>` | The same line for another character on record |
| `/top` or `/top kills` | The top players by kills |
| `/top elites`, `/top bosses`, `/top deaths`, `/top bounties`, `/top time` | The same list by another number |

An answer is shown only to the player who asked. A line the mod answers is not relayed to other players. `/party` and `/reply` stay the game's own.

## What the server says

- At dawn, when anything happened: `Yesterday: 412 monsters slain (31 elite), 2 bosses felled, 3 deaths. Top hunter: Wren with 120.`
- A player's 100th, 500th, 1,000th, 2,500th, 5,000th (and onward) kill, and their first boss kill.

## Settings

`BepInEx\config\com.humangenome.waygatemods.chronicle.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Chat` | `Commands` | `/stats` and `/top` | true |
| `Chat` | `PrivateReplies` | Only the player who asked sees the answer. false = everyone sees it | true |
| `Chat` | `TopCount` | Players listed by `/top`, 3 to 10 | 5 |
| `Announcements` | `DawnDigest` | The line at dawn | true |
| `Announcements` | `Milestones` | Kill milestones and first boss kills | true |
| `File` | `WriteEverySeconds` | How often the numbers are written while they change, 15 to 600 | 60 |

## The file

`BepInEx\config\HumanGenome-Chronicle\chronicle.json` holds every character's numbers, the server totals and the running day. It is rewritten while numbers change and read once at start, so a restart keeps everything. Characters are keyed by the game's own character id and shown by name. A file that cannot be read is kept under another name and a new record is started.

```json
{
  "schema": 1,
  "totals": {"kills": 4, "elites": 0, "bosses": 0, "deaths": 1, "bounties": 0},
  "today": {"kills": 4, "elites": 0, "bosses": 0, "deaths": 1, "hunters": {"<character id>": 3}},
  "players": [
    {"hash": "<character id>", "name": "Wren", "kills": 3, "elites": 0, "bosses": 0, "deaths": 0, "bounties": 0,
     "seconds": 181, "first_seen_utc": "2026-09-18T23:01:24Z", "last_seen_utc": "2026-09-18T23:04:25Z", "by_type": {"Drooplet": 3}}
  ]
}
```

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-Chronicle\` under the server's game folder, so it holds `Chronicle.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

The game's server death path names the player it credits with a kill when it calls `QuestManager.RegisterMonsterDeath(killer, monster, event)`; the mod counts after that call. The credit is the game's: the attacker, or the closest player in the area when the killing blow came from a summon or an effect, and one player per kill. A monster the game marks as a boss or a mini-boss counts as a boss kill, an empowered monster as an elite kill. Deaths are counted after `Player.OnDeath`, bounties after `DeedManager.RecordDeedCompletionForQuest`. Chat commands are read in a prefix on `ChatSystem.SendMessageToServerServerRpc`, and an answer is a Direct chat message whose source and destination are both the asking player, which the game shows to that player only. Nothing is written to a character or to the world save.

If a game update removes a method the mod depends on, the mod switches the affected feature off, or itself off, and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.7.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
