@echo off
setlocal
cd /d "%~dp0"
if exist "%LOCALAPPDATA%\Programs\Pin-dotnet\dotnet.exe" (
    set "PATH=%LOCALAPPDATA%\Programs\Pin-dotnet;%PATH%"
    set "DOTNET_ROOT=%LOCALAPPDATA%\Programs\Pin-dotnet"
)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 exit /b 1
echo Done: publish\Pin.exe
pause
