Home — private media server

Windows: run Install-Home.ps1 from this directory. It verifies this selected
package, installs to the current user's LocalAppData and creates a Home shortcut.
Parameters: -InstallRoot, -DataPath, -Port, -NoStart, -NoShortcut.

Linux: bash install-home.sh [installation-directory] [data-directory] [port].
Run the resulting start-home.sh; a user service may supervise this launcher.

Choose media folders in the browser setup/settings. Source folders are read-only.
Configure external FFmpeg/ffprobe and PAR2 as required, or set Home__FfmpegPath,
Home__FfprobePath, Home__Par2Path in the launcher environment.

Future updates: upload a signed Home package through Settings > Server > Updates.
The running installation's trusted key verifies updates; a key inside an uploaded
package is never accepted as its own proof of authenticity. Only obtain the first
installation and its trusted key through a distribution channel you have chosen.

Application data is stored outside the release directory. Rollback uses the
pre-update database/key/subtitle backup. Media payloads are not part of that backup.

Distribution license and public release signing identity are not yet finalized.
Dependency licenses accompany the application; FFmpeg/PAR2 remain external tools.
