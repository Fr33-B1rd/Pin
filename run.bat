@echo off
setlocal
cd /d "%~dp0"
if exist "%LOCALAPPDATA%\Programs\Pin-dotnet\dotnet.exe" (
    set "PATH=%LOCALAPPDATA%\Programs\Pin-dotnet;%PATH%"
    set "DOTNET_ROOT=%LOCALAPPDATA%\Programs\Pin-dotnet"
)
dotnet run -c Release
