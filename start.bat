@echo off
setlocal
set "ROOT=%~dp0"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"

if not exist "%DOTNET%" (
  echo dotnet.exe was not found: %DOTNET%
  exit /b 1
)

"%DOTNET%" build "%ROOT%ChatGptExportDownloader.slnx"
if errorlevel 1 exit /b %errorlevel%

start "" "%ROOT%src\ChatGptExportDownloader.Wpf\bin\Debug\net10.0-windows\ChatGptExportDownloader.Wpf.exe"
