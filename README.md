# RPMusicPlayer

An audio player plugin for [RasterPropMonitor](https://github.com/FirstPersonKSP/RasterPropMonitor) for KSP(1).

## Plugin Goal

The plugin is considered v1.0 once the following requirements are met:
- There is a folder where users can place music (not sure what's better, predefined or letting the user decide the location)
- The plugin adds a music player item to raster prop UI/menus
- Once activated, a browser UI shows all songs in the music folder browsable (and sortable) by ID3 tags (Artist, Album, Genre, Title), or Filename
- Once a song is selected, it starts playing with the other songs in the current view following after it is done
- Players can also activate/switch to a player UI with playback controls (play/pause/next/previous/shuffle/repeat) as well as information about the current song (title, artist, album, time/totel)
- Music is paused automatically (current state saved) once the player switches to a different vessel, space center/tracking station, or goes into eva
- Each vessel has its own player state (but sharing the same music folder on the PC)
- The plugin should work cross platform (win/mac/linux)
