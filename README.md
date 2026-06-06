# VideoSkip-Jellyfin
A videoskip plugin for Jellyfin Servers

For full disclosure, yes, this code was all created using Claude Sonnet 4.6.

## How To Use
Note: This plugin assumes you are on server version 10.11.8
1. Begin by installing the following plugin to your jellyfin server: https://github.com/n00bcodr/Jellyfin-JavaScript-Injector
2. Once it is working properly, you should see "JS Injector" beneath Plugins when navigating to "Administration/Dashboard"
3. Go to "JS Injector", select "Add Script", name it something like "Video Skip", and then paste the text from "videoskip-jellyfin.js"
4. Navigate to your local server installation of Jellyfin. Once there, go to "plugins", create a folder named "VideoSkip", and add the "Jellyfin.Plugin.VideoSkip.dll" to it.
5. Reset your server.

The plugin should now be functioning!

## How skp files are found
Skp files are found in the same way that Jellyfin finds subtitle files. In other words, place your skp file in the same directory w/ the same name as your video file.
Support for multiple versions of skp files is not included in this release nor is planned for future releases.

## How to make skp files
Please reference the following from VideoSkip since this entire project is based on their open-source code: https://videoskip.herokuapp.com/exchange/howto/edit/
