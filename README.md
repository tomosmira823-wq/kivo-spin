# Kivo Spin

Drag any file, hold **Shift**, drop it on the wheel. Windows 10/11.

**Convert:** PNG · JPG · WEBP · HEIC · AVIF · TIFF · BMP · ICO · PDF · DOCX
**Tools** (hover the middle of the wheel): Compress · Metadata · Edit · Annotate · Remove BG · Add BG · Crop · Redact · Combine

Results are saved next to the original (or a folder you pick). Originals are never changed.

---

## Build the installer

### Option A – GitHub (recommended, also turns on auto-updates)
1. Create a **public** GitHub repo (e.g. `kivo-spin`) and push this folder to it.
2. Tag a version and push it:
   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```
3. The **Build & release** action builds everything and publishes a Release containing
   `KivoSpin-win-Setup.exe`. That's the file customers download.

Every installed copy checks the repo's Releases 20 s after starting and then every 4 hours.
To ship an update: edit `RELEASE_NOTES.md`, bump the tag (`v1.0.1`), push. Users get a
"Kivo Spin 1.0.1 is ready – Restart now / Later" notice (or it installs itself if they turned on
*Install updates automatically*). If they pick Later, it installs when they quit.

> The repo must be public (or use a separate public repo just for releases) so customers'
> copies can download updates without a login.

### Option B – on your PC
Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then in PowerShell:
```
.\build.ps1 -Version 1.0.0 -Repo https://github.com/YOU/kivo-spin
```
The installer lands in `Releases\KivoSpin-win-Setup.exe`. Leave `-Repo` off for a build without updates.

## "An update is coming" messages
Edit `announcement.json` on the `main` branch and set `"active": true` with a new `"id"`.
Every running copy shows it once (within 4 hours). `minVersion` / `maxVersion` let you target
older copies only. Set `active` back to `false` when you're done.

## Code signing
Unsigned installers trigger a SmartScreen "Windows protected your PC" warning (More info → Run anyway).
For a store release, buy a code-signing certificate (or use Azure Trusted Signing) and add
`--signParams` to the `vpk pack` step.

## Notes
- **Word files** convert using Microsoft Word if it's installed, otherwise the free LibreOffice.
- **Remove BG** runs locally. The first time, it downloads a ~170 MB model to
  `%LocalAppData%\KivoSpinData\models`.
- Settings: `%AppData%\KivoSpin\settings.json` · Log: `%LocalAppData%\KivoSpinData\log.txt`

## Project layout
```
src/KivoSpin/
  Program.cs, App.cs        startup, single instance, tray icon
  Core/                     conversions & tools (Magick.NET, PDFium, PDFsharp, OpenXML, ONNX)
  UI/                       wheel, drag watcher, windows, toasts, updater
  Assets/                   icon, Michroma font
.github/workflows/release.yml   builds Setup.exe + publishes updates
```

## Third-party
Magick.NET (Apache-2.0) · PDFium via Docnet (BSD/Apache-2.0, MIT) · PDFsharp (MIT) ·
Open XML SDK (MIT) · ONNX Runtime (MIT) · IS-Net model via rembg (Apache-2.0) ·
Velopack (MIT) · Michroma font (SIL OFL 1.1). Include these notices on the store page.
