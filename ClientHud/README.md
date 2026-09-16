# Client HUD

A client-side mod for Dimraeth. While you are connected to a server it draws the server's name and the number of players online in a corner of the screen. It reads replicated state and changes nothing in the game.

| Key | What it does | Default |
|---|---|---|
| `Show` | Draw the overlay while connected | true |
| `Corner` | 0 = top left, 1 = top right | 0 |

## Install

Players do not install this by hand. A server that lists it in its mod set has it installed by the [Waygate](https://github.com/HumanGenome/Waygate) app on Connect, into that server's own mod folder, after asking once. Plain Steam launches are unaffected.

For a test client or a hand install: copy the folder from the release zip into `BepInEx\plugins\mods\HumanGenome-ClientHud\` under the game folder the client runs from.

## How it works

The plugin ticks off the game's menu manager, checks that this process is a connected client and not a server, reads the player count from the server list the game replicates, and draws with Unity's IMGUI. Every 30 seconds it writes one line to `BepInEx\LogOutput.log`, which is how a headless test client proves it ran. On a server it does nothing.

Tested on game build 25335390.

## Build

Set `WAYGATE_PACK` to the `BepInEx` folder of an unpacked WaygateServer package, then `dotnet build -c Release`.

## License

MIT.
