# Hushtype

Private, offline English dictation for Windows. Hold the selected key, speak, and release to paste into the original text field. A small click-through pill shows microphone levels and progress.

Hushtype uses **NVIDIA Parakeet TDT 0.6B v2 through sherpa-onnx**, with INT8 and four CPU threads by default. **No CUDA or LLM is required.**

## Run or install

The current development build can reuse the model downloaded during validation:

```powershell
.\artifacts\Hushtype\Hushtype.App.exe --data-dir .\.build\parakeet-verification
```

Packaged release files are in `artifacts/releases`:

- `Hushtype-win-Setup.exe`: per-user installer with a Start menu shortcut.
- `Hushtype-win-Portable.zip`: extract the whole archive and run the top-level `Hushtype.exe` launcher. The application files are under `current`. Use an absolute `--data-dir` path with this launcher.
- The full `.nupkg` and `releases.win.json` support future updates; they are not the normal launch files.
- `SHA256SUMS.txt` records package checksums.

The packaged app includes .NET. Setup can install the Visual C++ runtime prerequisite. The packages are unsigned. Models are downloaded separately on first use: about 661 MB for INT8 or 2.51 GB for FP32. Every file is verified against pinned size and SHA-256 values. Once cached, speech recognition works offline.

## First use

1. Wait for setup to open the microphone and prepare the model.
2. Choose **Start dictating**.
3. Click a text field, hold **Right Alt**, speak English and release.

Closing the welcome window keeps the app running in the tray. Use **Quit** to exit. If setup fails, use **Retry setup**, **Microphone access**, or choose another microphone in Settings.

## Settings and personal dictionary

Tray → **Settings** provides:

- Right Alt, Right Ctrl, F8, F9 or F10 as the hold-to-talk key.
- Windows default or a specific microphone. Hotkey and microphone changes apply while idle without restarting.
- INT8/FP32 and CPU threads. These model options apply on the next launch.
- Sounds, rule cleanup, history recording, launch at Windows sign-in and automatic updates when a release source is configured.

Tray → **Dictionary** lets you map a recognized phrase to your preferred spelling. For example, `cube cuddle` → `kubectl`. Matches use whole words/phrases, ignore case, prefer longer phrases and never cascade into another correction. Replacement spelling/capitalization is preserved. Corrections stay active when rule cleanup is disabled.

LLM cleanup was deliberately skipped. The optional rule pass removes fillers and tidies spacing and punctuation.

## History

History recording is **off by default**. Enable it in Settings to keep the latest 500 processed dictations locally, including text from refused pastes. Tray → **History** supports copy, deletion and **Paste in 3 seconds**. For re-paste, select an entry, click the button, then focus your destination text field during the three-second delay.

History stores original/final text, target process name and timings. Production logs contain timing/error information rather than dictated text. Explicit file-based speech diagnostics log their test transcript. Audio remains in memory.

## Files and preferences

- Models, history/database and rolling logs: `%LOCALAPPDATA%\Hushtype`.
- Settings: `%APPDATA%\Hushtype\settings.json`.
- `--data-dir <directory>` keeps models, settings, history and logs together in that directory.
- Old `ui-preferences.json` sound preferences are read when there is no settings file.
- `--no-sounds` starts muted. `--background` hides completed onboarding at launch.
- `--speech-precision int8|fp32` and `--speech-threads 1..20` override saved model settings for that launch.

## Build and verify

Windows x64; SDK 9.0.311 is selected by `global.json`. Framework-dependent development builds need .NET 8 Desktop Runtime.

```powershell
dotnet restore Hushtype.sln
dotnet build Hushtype.sln -c Release --no-restore

dotnet run --project tests/Hushtype.Core.Tests -c Release
dotnet run --project tests/Hushtype.Cleanup.Tests -c Release
dotnet run --project tests/Hushtype.Speech.Tests -c Release
dotnet run --project tests/Hushtype.Data.Tests -c Release
```

These are executable assertion harnesses: **dotnet test does not run them**. Current total: 79 checks.

Automatic-exit desktop diagnostics:

```powershell
.\artifacts\Hushtype\Hushtype.App.exe --smoke-test --data-dir .build/smoke
.\artifacts\Hushtype\Hushtype.App.exe --probe-hardware --data-dir .build/probe
.\artifacts\Hushtype\Hushtype.App.exe --verify-overlay --data-dir .build/overlay-test
.\artifacts\Hushtype\Hushtype.App.exe --verify-settings --data-dir .build/settings-test
.\artifacts\Hushtype\Hushtype.App.exe --verify-speech path-to-speech.wav --data-dir .build/parakeet-verification
```

The overlay diagnostic types only into its own editor and saves state snapshots. The settings diagnostic switches microphone/hotkey, restores previous settings, checks a dictionary correction in a separate diagnostic database and renders the settings/onboarding/history/dictionary windows. Use a dedicated test directory. Close any running Hushtype instance first because the app is single-instance.

See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) for exact results and remaining manual coverage.

## Package and updates

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Package.ps1 -Version 0.1.0
```

The script pins Velopack 1.2.0, publishes a self-contained Windows x64 build and creates installer/portable/update artifacts. No files are uploaded or installed by the script. Icons can be regenerated from the code-defined artwork with `scripts/New-BrandAssets.ps1`.

To configure future updates, package with `-UpdateUrl https://your-release-feed/` or an HTTPS GitHub repository URL. The value is written into the package's `update-source.json`. Publish the resulting release assets to that location. Automatic checking is opt-in and runs after successful startup; users can also select **Check for updates**. Downloads apply at the next app launch. Without a configured source, update controls are disabled and no update requests are made.

Update hosting, signing credentials, clean-machine installation and a real hosted upgrade remain unverified/unconfigured. The local installer and portable artifact are ready for testing.

## Known limits

- Right Alt is reserved while enabled, including on AltGr layouts. Other offered keys can be selected in Settings.
- Changing foreground windows or targeting an elevated app refuses the paste; the processed text is copied for recovery. Clipboard format restoration is best effort.
- Long speech uses 20-second windows with one second of overlap. Differently recognized overlap can leave duplicates or vary punctuation.
- RMS silence gating can reject very quiet speech. Personal accents and technical vocabulary still need live testing.
- Phase 2's native focus/editor checks passed; multiple monitors/mixed DPI and the full target-app checklist remain manual.

## Dependencies and attribution

Direct packages: NAudio 2.2.1, H.NotifyIcon.Wpf 2.3.2, org.k2fsa.sherpa.onnx 1.13.8, Microsoft.Extensions.Hosting 8.0.1, Serilog.Extensions.Hosting 8.0.0, Serilog.Sinks.File 7.0.0, Microsoft.Data.Sqlite 8.0.30, Velopack 1.2.0. Lock files pin transitive versions.

NVIDIA Parakeet TDT 0.6B v2 is CC BY 4.0 licensed. The ONNX conversion repositories, revisions and checksums are in [models.json](src/Hushtype.Speech/models.json); attribution is in [MODEL_NOTICE.md](MODEL_NOTICE.md). sherpa-onnx is Apache 2.0 licensed.

Primary references: [NVIDIA model card](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2), [sherpa C# API](https://k2-fsa.github.io/sherpa/onnx/csharp-api/index.html), [SQLite package](https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.30), [Velopack integration](https://docs.velopack.io/integrating/overview).