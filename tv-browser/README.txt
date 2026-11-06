TV Browser: a full screen, locked down browser for your TV launcher.

BUILD
1. Install the .NET 10 SDK on the computer you build on:   winget install Microsoft.DotNet.SDK.10
2. Double-click build.bat
3. Copy the whole publish folder to the TV computer and run publish\TVBrowser.exe
   (the TV computer needs the Microsoft Edge WebView2 Runtime, which Windows 10/11 normally has)

LAUNCHER PAGE
The launcher (index.html) is loaded from the address in config.json ("homeUrl").
When you change index.html, upload the new copy there too.

CONFIG  (publish\config.json)
  homeUrl         address of the launcher
  blockFullscreen true stops sites going fullscreen (videos stay windowed, Home button always visible).
                  false (default) lets videos go fullscreen; the Home button follows them in and fades when idle.
  allowedDomains  sites that may be opened. A domain also allows its subdomains.
                  Use a longer name such as accounts.google.com to allow just that part of a site.
If config.json has a mistake, the app tells you and uses its built-in settings.
If you add an app on the launcher for a site that isn't allowed, the launcher says so.

KEYS
  Ctrl+H or Alt+Home                   = back to the launcher (from any app)
  Alt+F4 or Ctrl+Alt+Q                 = quit

TROUBLESHOOTING
  If Ctrl+H does nothing, send the file  %LOCALAPPDATA%\TVBrowser\log.txt  (it records which shortcut path is working).

SELF-HEALING
  - If the web page crashes it reloads (after 3 crashes in a minute it goes back to the launcher).
  - If the whole web engine dies the app restarts itself.
  - With no internet it shows "No connection" and keeps trying.
