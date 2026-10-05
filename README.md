# RPMusicPlayer

A music player for [RasterPropMonitor](https://github.com/FirstPersonKSP/RasterPropMonitor) in Kerbal Space Program 1.

## What it does

- Reads your music from a folder you can choose and shows it on the RasterPropMonitor screens.
- The browser lists every song, sorted and filtered by ID3 tags (artist, album, genre, title) or file name.
- Picking a song starts it, with the rest of the current view queued up behind it.
- A player page shows the track details, position and the transport controls.
- Each vessel keeps its own queue and browsing settings. Volume is shared by the whole plugin.
- Music pauses when you switch vessel or go to the space centre or a tracking station, and picks
  back up where it left off when you return. It keeps playing when you step out of the cockpit to
  look at your ship.

Everything is controlled by the screen buttons: up, down, left, right, next, prev, select and
back.

## Requirements

- Kerbal Space Program 1.12
- [RasterPropMonitor](https://github.com/FirstPersonKSP/RasterPropMonitor) 1.x

## Installing

Build and deploy straight into your KSP folder:

```powershell
./deploy.ps1
./deploy.ps1 -KspDir "D:\Games\KSP"
./deploy.ps1 -KspDir "D:\Games\KSP" -Configuration Debug
```

That creates this in your KSP folder, leaving any music you have already put there alone:

```
GameData/RPMusicPlayer/
├── Plugins/RPMusicPlayer.dll
├── RPMusicPlayer.cfg
├── LICENSE
└── Music/            <- put your music here
```

The library scans wav files: wave is the only format the game's audio engine is verified to decode.
Sub folders are scanned too. Other formats can be added with `EXTENSIONS`, but they are not
guaranteed — an ogg the engine refuses (one carrying a video stream, for example) is listed with a
`(video stream)` warning, and selecting it logs why it will not play and stops without breaking
anything.

## Using it

1. Open a pod that has a RasterPropMonitor screen and go into IVA.
2. Press DATA to open the music library. The plugin's pages are bound to that button,
   so pressing it again returns to the library.
3. Up and down move the cursor, select activates the highlighted row.
4. Choosing a song starts it and switches to the Now Playing view.
5. The next and previous buttons move between the two views from either one.
6. Back leaves the music pages and returns you to the pod's own screen.

| Button | Library | Player |
| --- | --- | --- |
| Up / Down | Move the cursor | Move the cursor |
| Select | Activate the highlighted row | Activate the highlighted row |
| Left / Right | Step through the highlighted row when it is Sort by, Order or Filter | Change the volume when the volume row is selected |
| Next / Prev | Switch to Now Playing | Switch to the library |
| Back | Back to the pod's screen | Back to the library |

Sort field and direction, the filter and the rescan are all menu rows in the
library. Left and right step backwards and forwards through the highlighted row
when it is one of the three settings, so you can go back to the previous sort
field or letter without cycling all the way round. They do nothing on a song row
or on the rescan row, and they never move between the two pages: that is what
the next and previous buttons are for.

On the Order row, left sorts ascending and right sorts descending.

The player controls are a selectable list, the same shape as the browser:

```
Play
Next Track
Previous Track
Shuffle: off
Repeat:  off
Volume:  70%
Queue:   3 of 42
```

On/off settings are shown in green when active. Repeat cycles `off` → `ALL` → `ONE`. The queue row
appears only once something is playing.

## Configuration

`GameData/RPMusicPlayer/RPMusicPlayer.cfg`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `MUSICPATH` | `GameData/RPMusicPlayer/Music` | Folder to scan. Absolute, or relative to the KSP folder. |
| `VOLUME` | `0.7` | Music volume, on top of KSP's master volume. |
| `STREAMABOVE` | `24` | Stream files larger than this many MB instead of decoding them into RAM. `0` never streams, `-1` always does. |
| `SCANSUBFOLDERS` | `true` | Scan sub folders as well. |
| `SCANONSTART` | `true` | Scan at game start. Otherwise use *Rescan* in the browser. |
| `PAUSEWHENOUTSIDEIVA` | `false` | Also pause when the player leaves the cockpit. See the EVA note. |
| `EXTENSIONS` | `wav` | Which files count as playable. Wave is the only format the engine is verified to decode; others may load as empty clips. |
| `ENTRYBUTTON` | `auto` | Which button opens the player. `auto`, `screen`, `none`, or a button name. |
| `ENTRYPAGE` | `shipinfo` | Page name whose button should open the player, making it a second mode on that button. Overrides `ENTRYBUTTON`. On the stock MFD `shipinfo` is the button labelled DATA. |

Tags are read from ID3v1 and ID3v2.2/2.3/2.4 in mp3 style files, from the `LIST`/`INFO` and
`ID3` chunks of wave files, and from the Vorbis comments of ogg (Vorbis and Opus) and flac
files. Anything else falls back to the file name.

## Memory use

Scanning the music folder only reads tag headers, so a library of any size costs a few MB of
strings. Audio itself is decoded one track at a time, but a long wave file decodes to several
times its own size, so files over `STREAMABOVE` megabytes are streamed from disk instead. A
streamed track keeps memory flat but cannot be resumed from its position, so it restarts from the
beginning.

## Reporting a problem

Everything the plugin does is logged with a `[RPMusicPlayer]` prefix, so `KSP.log` in your KSP
folder is the thing to look at first. It records the music folder it scanned, the file extensions
it accepted, which monitor it patched, which button opens the player, and why a track failed to
play. Attach the relevant lines when you report something.

## How it hooks into RasterPropMonitor

The pages are built in code and added to every monitor in the vessel, rather than shipped as
part config. That means the plugin works with any pod or IVA mod that has a monitor, including
ones released after this plugin, without needing a patch per pod and without shipping a 3D model.

To open the player, its pages are added to the page cycle of one of the monitor's own buttons,
which is how RasterPropMonitor menus work: the button cycles through the pages bound to it, so
the player appears as another mode alongside the pod's built in ones. Because a plugin cannot add
a new physical button to somebody else's cockpit model, `ENTRYBUTTON` picks an existing one.

This does use a little reflection on RPM's private `pages` list, so it is tied to RPM 1.x. If a
future RPM changes that, look at `Source/PageInjector.cs`; everything else goes through the
public page API.

All of the per frame work runs on one `DontDestroyOnLoad` host object rather than on a scene
scoped addon. A scene scoped addon stops as soon as the scene changes, which left the plugin
silently idle after leaving the main menu; the host keeps running in every scene, so pausing on
a scene change and reattaching to monitors both work without depending on which scenes happen to
have addons.

## Notes on EVA

Music does not pause for EVA. Kerbal Space Program 1.12 gives mods no way to tell "the kerbals are
outside on an EVA" apart from "the player switched to the chase camera": there is no
`FlightGlobals.ActiveCrewMember`, and `CameraManager.CameraMode` only has `Flight`, `Map`,
`External`, `IVA` and `Internal`, so both situations report `Flight`. Rather than guess and get
it wrong, stepping out of the cockpit does not pause anything.

Setting `PAUSEWHENOUTSIDEIVA = true` restores the older behaviour of pausing whenever the player
leaves the cockpit. That covers EVA, but it also pauses when you simply switch to the chase camera.

The active vessel during an EVA is still the vessel the kerbal belongs to, not the kerbal, so the
per vessel queue and playback position are unaffected by going outside.

## Development

```powershell
dotnet build                 # the plugin
dotnet run --project Tests   # tag reader and formatting checks
```

To see what the tag reader makes of a real music folder, outside the game:

```powershell
dotnet run --project Tests -- "C:\Games\KSP\GameData\RPMusicPlayer\Music"
```

The KSP folder is a property, so you can point the build anywhere:

```powershell
dotnet build /p:KSPDir="D:\Games\KSP"
```

`tools/dump-types.ps1` and `tools/find-members.ps1` dump types and members from the KSP and
RasterPropMonitor assemblies, which is handy when working out which API is actually available, and
`tools/dump-il.ps1` disassembles a single method when a signature is not enough. All three read
`Mono.Cecil.dll` from the KSP install; they take `-KspDir`, or fall back to the `KSP_DIR`
environment variable and then the default Steam install.

## License

Copyright (C) 2026 Lukas Fülling <lukas@k40s.net>

RPMusicPlayer is free software, licensed under the
[GNU General Public License v3.0 or later](LICENSE). You may use, study,
modify and redistribute it, and the people you give it to must be able to do
the same and get the source. There is no warranty of any kind.
The full text is in [`LICENSE`](LICENSE).

Kerbal Space Program is a trademark of Squad, and RasterPropMonitor is a
separate project by its own authors. Neither is part of this work, and
neither is endorsed by it; the plugin only talks to both through their
public APIs.
