# Message of the Day

A server-side mod for Dimraeth servers run with [WaygateServer](https://github.com/HumanGenome/WaygateServer). It sends a `[Server]` chat line when a player joins and, if you want, a repeating announcement. Players install nothing.

| Key | What it does | Default |
|---|---|---|
| `Motd` | Sent to chat a few seconds after a player joins. `{player}` becomes the character's name. Empty turns it off. | `Welcome, {player}. Have fun and play fair.` |
| `JoinDelaySeconds` | Wait after the join before sending, so the player's chat is open. 3 to 60. | 8 |
| `Announcement` | Sent to everyone on a timer. Empty turns it off. | (empty) |
| `AnnounceEveryMinutes` | Minutes between announcements. 1 to 1440. | 30 |

## Install

Copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-MotdAnnounce\` under the server's game folder, so it holds `MotdAnnounce.dll` and `waygate-mod.json`. Start the server once; it writes `BepInEx\config\com.humangenome.waygatemods.motdannounce.cfg` with the keys above. Edit that file and restart.

On a server rented through a hosting panel that lists this mod, pick it there and the panel installs it.

## How it works

The game relays server notices (join, leave) through `ChatSystem.CreateServerNetworkMessage`, which builds a `[Server]` message and sends it to every connected client. The mod calls that on a connected player's chat component: once per join after the delay, and on the timer for the announcement. Every line sent is also written to `BepInEx\LogOutput.log`.

Tested on game build 25335390.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package, then `dotnet build -c Release`.

## License

MIT.
