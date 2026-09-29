# Changelog

## 0.5.0 - 2026-09-29
- Added a `Volume` setting (0 to 1, shown as a slider in a configuration manager). It applies to the barrier hit sound and the parry sound.
- Volume changes apply while the game is running.

## 0.4.0 - 2026-09-28
- Renamed from Staff Shield Hit Sound to Block Sound Replacer. The plugin ID, config file (`local.blocksoundreplacer.cfg`), DLL and install folder all changed. `build.bat` removes the old `StaffShieldHitSound` plugin folder so the two don't load together.
- Parry (perfect block) sounds are now played through the mod's own audio source and the original sound is muted (`DirectPlayback`, on by default).
- Added `[probe]` log lines that report what a replaced effect looked like when it spawned, for debugging the parry sound.
- Barrier hit replacement is on by default, and uses the game's own sound component as before.
- Removed the F9 test key.
- The bundled clip is now the louder, compressed version, with its full decay kept.
- Added `manifest.json`, `README.md` and an icon for Thunderstore.

## 0.3.0 - 2026-09-28
- Added replacement of parry and normal block sounds, with `ReplaceParry`, `ReplaceBlock` and `ReplaceBarrierHit` settings.
- Dodge sounds are excluded from replacement.
- Added an F9 key that plays the clip directly, for testing (removed in 0.4.0).
- Removed the old `Volume` setting, because the game overrode it.

## 0.2.0 - 2026-09-28
- Barrier hit replacement only. The plugin now checks for the game's item database every 2 seconds instead of patching the game with Harmony, which failed to build.
- The clip starts at the hit instead of after about one second of silence.

## 0.1.0 - 2026-09-28
- First version: replaced the Staff of Protection barrier hit sound using a Harmony patch. It did not compile against the game and was replaced by 0.2.0.
