## MusicBee-HQPlayer Plugin ##


MusicBee-HQPlayer is a MusicBee plugin to enable playing music stream from [**MusicBee**](http://www.getMusicBee.com/) to HQPlayer.

Thank Steven for prividing the source code of [uPnP/DLNA plugin](http://getMusicBee.com/forum/index.php?topic=14277.0) for reference.

The musical file in FLAC/WAV(PCM)/DSF/DFF format will be directly streamed, others will be decoded/encoded as PCM without sound effects.

- [Installation](#Installation)  

- [Usage](#Usage)   

- [Knows Issues](#Knows-Issues)

- [Thanks](#Thanks)

- [Contact](#Contact)


## Installation ##


Get the latest plugin from the [release](https://github.com/tracemouse/MusicBee-HQPlayer/releases) page,
unzip the zip file and copy the file `mb_HQPlayer.dll` into the MusicBee plugin folder(MusicBee\Plugins).
Please remember to close the MusicBee main program before you copy the dll file, the plugin will be effective when you re-open the MusicBee program. 

![plugin-1](https://tracemouse.github.io/MusicBee-HQPlayer/docs/plugin-1.png)


## Usage ##

The plugin setting dialog could be opened from `"MusicBee perferences" -> "Plugins" -> "stream to hqplayer"`. 


- **IP address & Port**  
MusicBee-HQPlayer plugin will use this setting to create a http stream server for HQPlayer, the port should be not used by other application. 
If your HQPlayer device is not located at the same computer, pleae make sure this port is added in the whitelist of windows firewall.

- **HQPlayer devices**  
HQPlayer devices could be located at any computer under your local network, the ip address must be inputted correctly, you may use the "Test" button to test the connection.
HQPlayer device name should be unique, and it will be showed in the pulldown list of redender(output) devices.
 
- **Activate HQPlayer as a render device**  
Select HQPlayer device from `"MusicBee perferences" -> "Player" -> "output"` to activate it. HQPlayer must be running with enabling network control before it's activated as a render device by MusicBee.

![plugin-2](https://tracemouse.github.io/MusicBee-HQPlayer/docs/plugin-2.png)
![plugin-2](https://tracemouse.github.io/MusicBee-HQPlayer/docs/plugin-3.png)
![plugin-2](https://tracemouse.github.io/MusicBee-HQPlayer/docs/plugin-4.png)

## Known Issues ##

- **Cannot work with MusicBee uPNP/DLNA plugin**  
- **Seek position issue**  
- **HQPlayer must be running before you activate it as render device**
- **Cannot stop playback on HQPlayer**


## Thanks ##

- [MusicBee](http://www.getMusicBee.com/) 
- [HQPlayer](https://www.signalyst.com/) 


## Contact ##

<tracemouse@163.com>
