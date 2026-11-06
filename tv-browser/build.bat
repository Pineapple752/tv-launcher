@echo off
cd /d "%~dp0"
echo Building TV Browser...
rem self-contained: the TV machine doesn't need .NET installed, only the Edge WebView2 Runtime
dotnet publish TVBrowser.csproj -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 (
  echo.
  echo The build failed. Copy the red error text above and send it back.
  pause
  exit /b 1
)
echo.
echo Done. Run publish\TVBrowser.exe
pause
