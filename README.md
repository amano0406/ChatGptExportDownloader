# ChatGPT Export Downloader

[日本語版はこちら](README.JA.md)

ChatGPT Export Downloader is a Windows WPF app for downloading large ChatGPT data export ZIP files when Chrome's normal download flow repeatedly fails. It uses an authenticated request copied from Chrome DevTools and downloads the ZIP in sequential HTTP byte ranges.

## Features

- Paste and parse Chrome DevTools `Copy as cURL (cmd)` output
- Detect the request URL, Cookie, User-Agent, and output file name
- Check connectivity and file size with `Range: bytes=0-0`
- Download large ZIP files into a resumable `.part` file
- Resume from an existing `.part` file if the download stops
- Rename to the final ZIP after completion
- Verify that the completed file can be opened as a ZIP

## Requirements

- Windows
- Google Chrome
- .NET SDK / Runtime
  - Development builds use `C:\Program Files\dotnet\dotnet.exe`
- A Chrome session that is logged in to ChatGPT

## Platform Support

This is a Windows desktop app.

## Start The App

Download the release ZIP, extract it, and run `ChatGptExportDownloader.exe`.

## Usage

### 1. Request a ChatGPT data export

1. Open ChatGPT.
2. Open the account menu in the lower-left corner.
3. Open `Settings`.
4. Open `Data Controls`.
5. Click `Export data`.
6. Complete authentication if ChatGPT asks for it.
7. Confirm that OpenAI sends an email similar to `ChatGPT - Your data export has started`.
8. Wait for the follow-up email similar to `ChatGPT - Your data export is ready`.

### 2. Copy the authenticated download request from Chrome DevTools

Normally, clicking the email's download button starts the ZIP download directly. Use this app when that normal Chrome download fails partway through.

1. Open the `ChatGPT - Your data export is ready` email in Gmail.
2. Right-click the `Download data export` button.
3. Choose `Copy link address`.
4. Open a new Chrome tab.
5. Press `F12` to open DevTools.
6. Open the `Network` tab in DevTools.
7. Paste the copied download link into the Chrome address bar and press Enter.
8. When the ZIP download starts, stop it from Chrome's downloads UI in the upper-right corner.
9. In the DevTools Network list, find the request that looks like `content?...`.
10. Right-click that request.
11. Choose `Copy` -> `Copy as cURL (cmd)`.

The clipboard now contains a long `curl ...` command that includes authentication data such as cookies.

## Download With The App

1. Start `ChatGPT Export Downloader`.
2. Paste the copied `cURL (cmd)` text into the input box.
   - You can also use the `Paste from clipboard` button.
3. Click `Analyze`.
4. Confirm that these fields are detected:
   - URL
   - Cookie
   - User-Agent
   - File name
5. Choose the output directory with `Select`.
6. Edit the output file name if needed.
7. Click `Connection check (size check)`.
   - HTTP `206` means byte-range downloading is available.
   - The app displays the total file size.
8. Click `Start download`.
9. After completion, the ZIP verification result appears in the log.

The split size is `128 MiB` by default. Usually you should leave it unchanged. If your network is unstable, using a smaller value reduces the amount of data retried after a failed chunk.

## Security Notes

The copied `cURL (cmd)` text contains authentication information, including ChatGPT login cookies and Cloudflare-related cookies.

- Do not paste the `cURL (cmd)` text into chat, issues, logs, or public places.
- The app does not display cookie values in the UI or log.
- Consider logging out of ChatGPT and logging back in after the download.
- Use the `Clear secrets` button to clear the pasted text and parsed request information.

## If The Download Stops

Use the same output directory and output file name, then click `Start download` again. If the `.part` file still exists, the app resumes from its current size.

If authentication expires or you get `403`, copy a fresh `cURL (cmd)` request from Chrome DevTools and paste it again.

## Development

Build:

```bat
"C:\Program Files\dotnet\dotnet.exe" build ChatGptExportDownloader.slnx
```

Test:

```bat
"C:\Program Files\dotnet\dotnet.exe" run --project tests\ChatGptExportDownloader.Tests\ChatGptExportDownloader.Tests.csproj
```

The tests do not call ChatGPT. They use a fake HTTP handler that returns byte-range responses and verifies the core split-download behavior.
