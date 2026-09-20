# Bounty Board Plus

Dimraeth's bounty boards have no server settings: a finished bounty stays off the board for game days, the world's story progress caps every bounty's tier, and a board holds five postings plus its boss bounties. Bounty Board Plus is a server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer) that adds those settings. It posts new bounties on a real-time clock, switches bounty cooldowns off or shortens them, raises the tier ceiling, adds postings to each board, says in chat when a tier opens, a bounty is finished or a boss dies, and answers `/bounties` in chat. The server decides all of this, so players install nothing.

## What it changes

| | The game | With this mod (defaults) |
|---|---|---|
| New postings | Once per game day (about half an hour of real time) and after each finished bounty | The same, plus every 15 real minutes |
| Cooldown after finishing a bounty | The bounty's own, in game days (3 for most) | None |
| Tier ceiling | What the world's story progress allows, starting at 1 | The same plus 2 |
| Postings on a board | 5, plus every tiered boss bounty the world has unlocked | 7, plus the same |

A bounty still opens its tiers one by one, and the game never lets a bounty go past its own last tier; the ceiling only says how high a bounty may climb. A board can only post bounties the world has unlocked, so a new world shows fewer postings than the board has room for.

## What players type

| Command | Answer |
|---|---|
| `/bounties` | What is posted on each board, with each bounty's kind and open tier, and the minutes until the next postings: `Guild Hall board: 3 bounties. New postings in 9 min.` then `1. Thinning Out the Fields [Cull], 2. The Corrupted Beast Returns [Boss, tier 2 of 10]` |

`/bounty` is the same command. An answer is shown only to the player who asked, and a line the mod answers is not relayed to other players. A bounty that is cooling down for the asking player is marked `(cooling down for you)`.

## What the server says

- `New bounties are posted on the Guild Hall board.` when the mod's clock posts new bounties.
- `The Corrupted Beast Returns: tier 2 is now open.` when a bounty's next tier opens for the world.
- `Wren finished the bounty The Corrupted Beast Returns (tier 2).`
- `Wren felled the Corrupted Beast (tier 2 bounty).` when a boss dies; the tier is named when the fight belongs to a tiered bounty. Mini-bosses are left out unless `MiniBosses` is on.

Every line can be reworded or switched off.

## Settings

`BepInEx\config\com.humangenome.waygatemods.bountyboardplus.cfg`, written on the first start. The mod reads the file again within a few seconds of a change, so no restart is needed; a change to the postings or the cooldown posts the boards again at once.

| Section | Key | What it does | Default |
|---|---|---|---|
| `Board` | `RefreshEveryMinutes` | Real minutes between new postings, 5 to 1440. 0 = the game's own refresh only | 15 |
| `Board` | `ExtraSlots` | Extra postings per board, 0 to 7. A board never holds more than 10 | 2 |
| `Cooldowns` | `Factor` | 0 = no cooldown, 1 = the game's own, 0.5 = half the days, rounded up | 0 |
| `Tiers` | `CapBonus` | Added to the world's tier ceiling, 0 to 9 | 2 |
| `Announcements` | `Refresh`, `TierUnlocks`, `Bounties`, `BossKills` | The four chat lines | true |
| `Announcements` | `MiniBosses` | Mini-boss kills get the boss line too | false |
| `Chat` | `Commands` | `/bounties` | true |
| `Chat` | `PrivateReplies` | Only the player who asked sees the answer. false = everyone sees it | true |
| `Messages` | `Refresh`, `TierUnlock`, `BountyDone`, `BossKill` | The wording. `{board}`, `{bounty}`, `{tier}`, `{player}`, `{boss}` are filled in. Empty = say nothing | see above |

The clock is cut from real time in blocks of `RefreshEveryMinutes` (UTC), so a restart does not move the next posting time. The mod keeps no file of its own: tiers and finished bounties are the game's own records in the world save.

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-BountyBoardPlus\` under the server's game folder, so it holds `BountyBoardPlus.dll` and `waygate-mod.json`, then start the server.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it. [panel-settings.json](panel-settings.json) describes the settings a panel can show as a form.

## How it works

The game asks one question wherever a cooldown matters (filling a board, answering a player who opens one, finishing a quest): `DeedManager.IsDeedOnCooldownFor(board, bounty, day, player)`, which compares the days since the last completion with the bounty's cooldown days. A shorter cooldown is that question asked on a later day, so a prefix moves the day forward by the days taken off; no cooldown is a postfix that answers no. The list under "on cooldown" on a player's board is sent by `SendCooldownDataClientRpc`, and a prefix rewrites its days to match. The tier ceiling is a postfix on `DeedManager.GetWorldTierCap`, followed by the game's own `RefreshTierCapIfChanged`, which sends every player their tiers. Extra postings raise `DeedBoardDefinition.AvailableSlots` in memory before the game's own `RefreshBoard` fills the board; new postings on the clock are that same call.

Chat lines come after `DeedManager.UnlockNextWorldDeedTier` (when it reports a new tier), around `DeedManager.RecordDeedCompletionForQuest` and after `QuestManager.RegisterMonsterDeath`. `/bounties` is read in a prefix on `ChatSystem.SendMessageToServerServerRpc`, and an answer is a Direct chat message whose source and destination are both the asking player, which the game shows to that player only.

Nothing is written to a character or to the world save by the mod. Completions, cooldown days and tiers are recorded by the game exactly as without it, so removing the mod brings back the game's own cooldowns, ceiling and five postings at the next start. A tier the world earned while the ceiling was raised stays earned; players hold it again when the story raises the game's own ceiling that far.

If a game update removes a method the mod depends on, the mod switches the affected feature off, or itself off, and writes one line saying so. It never stops the server.

Tested on game build 25350646 with WaygateServer 0.3.8.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package (or of a game folder after one Waygate Connect), then `dotnet build -c Release`. The interop assemblies are referenced from there and are not part of this repository. `../Shared/Kit.cs` is compiled into the dll.

## License

MIT.
