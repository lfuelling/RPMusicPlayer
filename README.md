# RPMusicPlayer

A music player for [RasterPropMonitor](https://github.com/FirstPersonKSP/RasterPropMonitor) in Kerbal Space Program 1.

## What it does

- Reads your music from a folder you choose and shows it on the pod's RasterPropMonitor screens.
- The browser lists every song, sorted and filtered by ID3 tags (artist, album, genre, title) or file name.
- Picking a song starts it, with the rest of the current view queued up behind it.
- A player page shows the track's details, position and the transport controls.
- Each vessel keeps its own queue, volume and browsing settings.
- Music pauses when you switch vessel, go to the space centre or a tracking station, or step outside the pod. Coming back from an EVA leaves it paused until you press play.

Everything is driven by the monitor's own buttons: up, down, left, right, select and back. The
on-screen legend is generated from whatever buttons your IVA actually has, so it reads correctly
on every cockpit without configuration.

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
└── Music/            <- put your music here
```

Supported formats are mp3, ogg, wav, aiff, m4a and flac. Sub folders are scanned too.

## Using it

1. Open a pod that has a RasterPropMonitor screen and go into IVA.
2. Press the entry button to open the music library. By default the plugin adds its pages to a
   button that currently drives a single screen, so the music player is the second mode on it:
   press that button a second time to reach it. Point `ENTRYPAGE` at a page name to choose which
   one, `ENTRYBUTTON` at a button name, or use `screen` to click the screen itself.
3. Up and down move through the list, select plays a song, left and right change the sort order,
   and back leaves the music pages and returns you to the pod's own screen.

| Button | Library | Player |
| --- | --- | --- |
| Up / Down | Move the cursor | Move the cursor |
| Select | Play the selected song | Activate the highlighted control |
| Left / Right | Change sort field / direction | — |
| Back | Back to the pod's screen | Back to the library |

The player controls are a selectable list, the same shape as the browser:

```
<<  Previous
Pause
Next  >>
Shuffle: off
Repeat:  off
Volume:  70%   (-)
Volume:  70%   (+)
>>  LIBRARY  >>
```

On/off settings are shown in green when active. Repeat cycles `off` → `ALL` → `ONE`.

## Configuration

`GameData/RPMusicPlayer/RPMusicPlayer.cfg`:

| Setting | Default | Meaning |
| --- | --- | --- |
| `MUSICPATH` | `GameData/RPMusicPlayer/Music` | Folder to scan. Absolute, or relative to the KSP folder. |
| `VOLUME` | `0.7` | Music volume, on top of KSP's master volume. |
| `SCANSUBFOLDERS` | `true` | Scan sub folders as well. |
| `SCANONSTART` | `true` | Scan at game start. Otherwise use *Rescan* in the browser. |
| `EXTENSIONS` | `mp3,ogg,wav,aiff,aif,m4a,mp4,flac` | Which files count as playable. |
| `ENTRYBUTTON` | `auto` | Which button opens the player. `auto`, `screen`, `none`, or a button name. |
| `ENTRYPAGE` | `shipinfo` | Page name whose button should open the player, making it a second mode on that button. Overrides `ENTRYBUTTON`. On the stock MFD `shipinfo` is the button labelled DATA. |

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

## Notes on going EVA

RasterPropMonitor only updates a screen while its crew are inside the pod, and while a kerbal is
outside the vessel the pod's internal model is hidden and has nothing to click. The plugin
therefore only looks for monitors while the player is inside a vessel, and pausing is driven off
the same condition.

## Development

```powershell
dotnet build                 # the plugin
dotnet run --project Tests   # ID3 reader and formatting checks
```

The KSP folder is a property, so you can point the build anywhere:

```powershell
dotnet build /p:KSPDir="D:\Games\KSP"
```

`tools/dump-types.ps1` and `tools/find-members.ps1` dump types and members from the KSP and
RasterPropMonitor assemblies, which is handy when working out which API is actually available.