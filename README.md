# Block Sound Replacer

Replaces the Staff of Protection barrier hit sound and the parry (perfect block) sound in Valheim with a custom clip (`hitsound.wav`). Client-side only.

## Requirements

- Valheim with [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

## Install

Install through your mod manager, or copy `BlockSoundReplacer.dll` and `hitsound.wav` together into `BepInEx/plugins/BlockSoundReplacer/`.

## Config

`BepInEx/config/local.blocksoundreplacer.cfg` (created on first launch)

| Setting | Default | What it does |
|---|---|---|
| ReplaceBarrierHit | true | Replace the Staff of Protection barrier hit sound |
| ReplaceParry | true | Replace the parry / perfect-block sound |
| ReplaceBlock | false | Replace the normal block sound |
| DirectPlayback | true | Play the clip through its own audio source for parry/block effects |
| FileName | hitsound.wav | Audio file next to the DLL (WAV or OGG) |

To use your own sound, replace `hitsound.wav` in the plugin folder or point `FileName` at another file.

## Building from source

1. Install the .NET SDK and BepInEx.
2. Double-click `build.bat`. It finds Valheim and BepInEx (game folder, r2modman or Thunderstore profile), builds, and copies the DLL and `hitsound.wav` into `BepInEx/plugins/BlockSoundReplacer/`.
3. To build by hand: `dotnet build -c Release -p:ValheimDir="<Valheim folder>" -p:BepInExCore="<BepInEx\core folder>"`

## Troubleshooting

Search `BepInEx/LogOutput.log` for `Block Sound Replacer`. It lists the effects found and the prefabs changed. After a parry, lines starting with `[probe]` show what the parry effect looked like when it spawned.
