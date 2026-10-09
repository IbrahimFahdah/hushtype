# Dictation App — Build Plan (handoff)

A Windows push-to-talk dictation app in the spirit of Wispr Flow. You hold a hotkey and speak. When you let go, the speech is transcribed locally, cleaned up with optional deterministic rules and personal dictionary corrections, and pasted wherever the cursor is.

**App name:** `Hushtype` (renamed from the working name Murmur on 9 October 2026) · first packaged release `0.1.0`

**Approved update (9 October 2026):** English-only dictation with NVIDIA Parakeet TDT 0.6B v2 through sherpa-onnx on Windows CPU, replacing Whisper/CUDA. Default: INT8 with four inference threads; optional FP32. Parakeet v3 through FluidAudio was considered, but the implemented Windows backend is Parakeet v2 through sherpa-onnx. No CUDA installation is needed for this backend.

**Current status (9 October 2026):** Phases 1, 2, 4 and 5 are implemented. The user confirmed live dictation works, with six runs at 360–631 ms release-to-paste. **Phase 3 is skipped by user request; no LLM or Ollama integration is included.** Settings, local history, dictionary and branding checks pass. Phase 6 produces a self-contained installer, portable ZIP and update packages; update hosting and code signing are not configured. Remaining manual checks include real-app/multi-monitor behavior and installation on a clean machine. See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

---

## 1. Target machine

The app is built for, and tested on, this machine:

| | |
|---|---|
| Laptop | Acer Nitro ANV15-51 |
| OS | Windows 11 Pro (build 26200) |
| CPU | Intel Core i9-13900H (14C / 20T) |
| GPU | NVIDIA GeForce RTX 4060 Laptop, 8 GB VRAM (plus Intel UHD integrated graphics) |
| RAM | 32 GB |
| Tooling present | .NET SDK, VS Code, Ollama (the `~/.ollama` folder exists) |

Requirements that follow from this hardware:
- **Fully offline.** No network calls are needed for the core flow.
- **CPU first, no CUDA requirement.** Use the approved English Parakeet v2 ONNX model. GPU acceleration may be evaluated later if CPU latency is insufficient.

---

## 2. Stack

| Concern | Choice |
|---|---|
| Language / UI | C# 14 with **WPF**, targeting `net10.0-windows` x64 (.NET 10 LTS, supported until November 2028); SDK **10.0.401** pinned in `global.json`; .NET 10 Desktop Runtime required for framework-dependent builds |
| Hotkey | Low-level keyboard hook (`SetWindowsHookEx` with `WH_KEYBOARD_LL`) via P/Invoke |
| Audio | **NAudio 2.2.1** (its net6.0-windows build runs on .NET 10; 3.x is a breaking major version and was not adopted), `WaveInEvent` capture at 16 kHz mono PCM16, converted to float32 for recognition |
| Speech-to-text | **sherpa-onnx 1.13.8** (`org.k2fsa.sherpa.onnx`), Windows x64 CPU runtime |
| STT model | **NVIDIA Parakeet TDT 0.6B v2**, English only; INT8 ONNX by default (four CPU threads), with optional full-precision profile |
| Cleanup | Deterministic rules plus personal dictionary; LLM phase skipped |
| Tray icon | **H.NotifyIcon.Wpf 2.4.1** |
| Storage | SQLite (`Microsoft.Data.Sqlite` **10.0.12**) for history and dictionary; JSON settings |
| Installer / updates | **Velopack 1.2.0** runtime and pinned packaging CLI |
| DI / hosting | `Microsoft.Extensions.Hosting` **10.0.12** |
| Logging | `Serilog.Extensions.Hosting` **10.0.0**, `Serilog.Sinks.File` **7.0.0** |

For each NuGet package, check its README for the current package names and runtime requirements before pinning versions. Don't rely on memory for this.

NuGet lock files pin dependencies. SQLite and Velopack are implemented; Ollama is excluded by the approved scope change. Model attribution is recorded in [MODEL_NOTICE.md](MODEL_NOTICE.md).

---

## 3. Pipeline

```
Hold Right Ctrl ──► start capture (with ~300 ms pre-roll)
Release        ──► stop capture
               ──► ITranscriber.TranscribeAsync(samples)     // 16 kHz mono float32; Parakeet v2, CPU
               ──► ITextPostProcessor.ProcessAsync(text, ctx) // optional rules → dictionary corrections
               ──► ITextInserter.InsertAsync(text)            // clipboard + Ctrl+V
               ──► save to history                         // opt-in; newest 500, local SQLite
```

**Latency targets** for a 10-second utterance on this machine:
- Parakeet v2: under 0.5 s (target, verify on this machine)
- Release to paste, with no LLM: under 0.8 s

**Measured live baseline:** six runs recorded 553, 360, 631, 554, 548 and 468 ms release-to-paste. The 9.54-second recording used 388 ms STT and 631 ms total. These runs satisfy the phase 1 under-one-second timing gate; they do not guarantee every future run. Isolated 11-second file benchmarks varied from about 0.57 to 3 seconds. INT8 and FP32 produced identical text on the comparison fixture; this is not a broad accuracy comparison. Keep logging timings, including on battery and for longer recordings.

---

## 4. Project layout

```
Hushtype.sln
src/
  Hushtype.App/            WPF app: App.xaml, tray, overlay pill, settings window
  Hushtype.Core/           Interfaces, pipeline orchestrator, models, settings
  Hushtype.Platform/       Win32 interop: keyboard hook, SendInput, clipboard, foreground app
  Hushtype.Audio/          NAudio capture, ring buffer, resampling, level meter
  Hushtype.Speech/         Parakeet/sherpa-onnx transcriber, model manager/downloader
  Hushtype.Cleanup/        Optional rules + deterministic dictionary corrections
  Hushtype.Data/           SQLite history + dictionary
tests/
  Hushtype.Core.Tests/
  Hushtype.Cleanup.Tests/
  Hushtype.Speech.Tests/
  Hushtype.Data.Tests/
```

### Core interfaces (in `Hushtype.Core`)

```csharp
public sealed record TargetApp(string ProcessName, string WindowTitle, nint WindowHandle);
public sealed record TranscribeOptions(string Language = "en", string[]? Dictionary = null);
public interface IHotkeyService : IDisposable { event EventHandler? PressStarted; event EventHandler? PressEnded; void Start(); void Stop(); }
public interface IAudioRecorder : IDisposable { void Start(); float[] Stop(); event EventHandler<float>? LevelChanged; } // 16 kHz mono floats
public interface ITranscriber     { Task<string> TranscribeAsync(float[] samples, TranscribeOptions opts, CancellationToken ct); }
public interface ITextPostProcessor { Task<string> ProcessAsync(string raw, TargetApp ctx, CancellationToken ct); }
public interface ITextInserter    { Task InsertAsync(string text, TargetApp target, CancellationToken ct); }
public interface IForegroundAppProvider { TargetApp GetCurrent(); }
```

`DictationPipeline` coordinates these as a small state machine: `Idle → Recording → Transcribing → Inserting → Idle`.

---

## 5. Implementation details and gotchas

### Hotkey (`Hushtype.Platform`)
- Install the hook on a **dedicated thread with its own message loop**, so UI work can never delay the callback past `LowLevelHooksTimeout` (Windows silently removes slow hooks). The callback stays tiny and marshals press/release to the dispatcher; the delegate is rooted for the hook's lifetime.
- Default key for new settings: **Right Ctrl** (`VK_RCONTROL`), which types no character on any layout. Settings also offers Right Alt (with an AltGr warning), F8, F9 and F10, applied without restarting. Saved choices are kept. The Fn key isn't visible to Windows.
- On some layouts Right Alt is AltGr, which also injects a fake `LCtrl` down. Ignore `LCtrl` events flagged `LLKHF_INJECTED`, or ones that arrive within a few ms of `RMENU`.
- Ignore auto-repeat key-downs while the key is already held.
- **Swallow** Right Alt and F8–F10 (return `1`) so the active app doesn't open its menu bar or type AltGr characters. **Observe** Right Ctrl (pass it through) so the target app's modifier state stays truthful.
- Stuck-key watchdog: while held, every 250 ms the hook thread ends the press as a normal release if an observed key is physically up (`GetAsyncKeyState`), the input desktop changed (lock screen, UAC), or an elevated window took focus. Swallowed keys never update `GetAsyncKeyState`, so they rely on the latter two checks.
- Ignore presses shorter than about 150 ms, since they're accidental taps.
- Optional later: a "hands-free" toggle where a double-tap latches recording on.

### Audio (`Hushtype.Audio`)
- Keep the device open whenever the app runs, writing into a **ring buffer of about 1 s**. When the key goes down, start the utterance from about 300 ms earlier so the first syllable isn't clipped.
- Convert to **16 kHz mono float32** (the format Parakeet expects), using NAudio's `WdlResamplingSampleProvider` or `MediaFoundationResampler`.
- Emit RMS level about 30 times per second for the overlay waveform.
- Cap recordings at about 5 minutes.
- If the default device changes (for example a headset plugged in), re-open capture.

### Transcription (`Hushtype.Speech`)
- **Load the model once at startup and keep it warm.** Never load it per utterance. Run one tiny dummy transcription at startup so the first real one is fast.
- `ModelManager` downloads models on first run into `%LOCALAPPDATA%\Hushtype\models\`, shows progress, and verifies **both exact size and SHA-256** against pinned Hugging Face revisions in `src/Hushtype.Speech/models.json`. Failed downloads are never promoted into the cache. Once cached, recognition runs offline.
- Separate profiles: INT8 approximately **661 MB**; FP32 approximately **2.51 GB**, including external encoder weights. CLI options: `--speech-precision int8|fp32`, `--speech-threads 1..20` (default 4), and `--data-dir <directory>`.
- Runtime: ONNX CPU; log the selected provider, precision and thread count. No CUDA toolkit installation is required.
- **Personal dictionary:** explicit post-processing corrections for names and jargon are implemented in phase 4. Parakeet TDT does not accept Whisper initial prompts; do not claim prompt-based vocabulary biasing.
- Language: fixed English. Arabic and multilingual recognition are outside the approved scope.
- Process long utterances in **20-second windows with one second of overlap** and merge matching overlap words. Keep the model warm across windows. A 66-second repeated fixture retained all six passages; live long-recording coverage remains pending. Differing overlap recognition can leave duplicates or vary punctuation.
- Native decoding runs off the UI thread and cannot be interrupted mid-decode. Honor cancellation after decode and before insertion; retain the recognizer until active work completes.
- Trim leading and trailing silence before inference. If the clip is basically silent, skip it, because ASR can hallucinate on silence (for example "Thanks for watching!").

### Cleanup (`Hushtype.Cleanup`)
- The LLM phase is skipped. No Ollama model, timeout, per-app style or network cleanup is needed.
- Optional rule pass removes filler words, fixes spacing/capitalization/punctuation and collapses accidental repeated words while preserving supported intentional repetitions and repeated digits.
- Dictionary entries map a recognized phrase to its replacement. Matching is case-insensitive at whole-word/phrase boundaries; longer matches win. Multi-word phrases also match run-together and hyphenated forms (`cubecuddle`, `cube-cuddle`). Text and phrases are normalized to Unicode NFC before matching. Replacement is a single literal pass, so corrections do not cascade. Exact replacement capitalization is preserved.
- Dictionary corrections remain active when the rule pass is disabled in Settings.
### Insertion (`Hushtype.Platform`)
1. Save the current clipboard, including non-text formats where possible (`IDataObject`).
2. Set the clipboard text. Retry on `CLIPBRD_E_CANT_OPEN` up to 5 times, 20 ms apart. This must run on an STA thread.
3. Send **Ctrl+V** via `SendInput`. Make sure no modifiers are still logically down first, since the user just released the hotkey.
4. Wait about 150 ms, then restore the original clipboard.
- **Known limitation:** you can't paste into elevated (admin) windows from a non-elevated app because of UIPI. Detect this case and show a toast instead of failing silently.
- Optional fallback setting: "type characters" mode via `SendInput` with `KEYEVENTF_UNICODE`, for apps that block paste.

### Overlay pill (`Hushtype.App`)
- A borderless, transparent WPF window with `Topmost=true`, `ShowActivated=false` and `ShowInTaskbar=false`, plus extended styles `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`. **It must never take focus**, or the paste lands in the pill.
- Position it bottom-centre on the monitor that has the foreground window.
- States: *listening* (live waveform), *transcribing* (shimmer or spinner), *done* (brief check), *error* (brief red tint; full details in the tray notification and Status / setup).
- Short start and stop sounds, which can be turned off.

### Tray and settings
- Tray: Enabled, Sounds, Settings, History, Dictionary, Check for updates, Status / setup, Quit.
- Hotkey and microphone apply without restarting while idle. A failed microphone switch restores the previous device where possible.
- Precision (INT8/FP32) and CPU threads persist for the next launch. The current CPU model is displayed separately.
- Sounds, rule cleanup, local history recording and launch at sign-in are configurable. Automatic update checks are available only when an HTTPS release source is configured.
- Default settings: `%APPDATA%\Hushtype\settings.json`; custom `--data-dir` keeps settings beside its data. Settings are validated and saved atomically. Phase 2's `ui-preferences.json` sound value is read when no settings file exists.

### Data
- SQLite at `%LOCALAPPDATA%\Hushtype\hushtype.db`, or under `--data-dir`.
- `history(id, created_at, app, raw_text, final_text, duration_ms, stt_ms, cleanup_ms, corrections)` keeps the newest 500 processed dictations when history is enabled. History is off by default, includes text from refused pastes, and supports search, copy, selected deletion, clear-all and a three-second delayed re-paste into the foreground app. `corrections` is a JSON list of the dictionary corrections that fired (matched form → replacement × count), shown as badges; it is never logged.
- `dictionary(heard, replacement, enabled)` stores deterministic phrase corrections. Terms can be searched, added/updated, turned off (kept but not applied) or removed without restarting, and imported/exported as plain text (`heard -> replacement`, `# off:` for disabled entries). Saving shows non-blocking warnings for common single words, phrases of three characters or fewer, and replacements identical to the phrase.
- Schema version 2 (`PRAGMA user_version`); version 1 databases gain the new columns on startup.
- Database work runs off the UI thread. History-write failures do not prevent dictation from being pasted.
### Logging
- Use Serilog writing to a rolling file in `%LOCALAPPDATA%\Hushtype\logs\`.
- Log per-stage timings for every utterance. These are the source of truth for the latency targets.
- Production logs do not contain dictated text. Only explicit `--verify-speech` diagnostics log their supplied test transcript. History is stored only when explicitly enabled in Settings.

---

## 6. Build phases

Work phase by phase and verify each implementation. The user authorized skipping phase 3 and proceeding through phases 4–6; carry outstanding manual hardware/app checks forward without claiming they passed.

### Phase 1: Skeleton (MVP) — implemented; user live test passed
- Solution and projects, DI host, tray icon with Quit.
- Hold Right Alt to record, then release to run Parakeet v2 locally on CPU, then paste.
- Run the rule-based cleanup only.
- Download the model on first run.
- **Done when:** in Notepad, holding Right Alt and saying "testing one two three" pastes "Testing one two three." in under 1 s after release, and the log shows `engine=sherpa-onnx`, `runtime=cpu`, and the selected Parakeet v2 model.
- **Verified:** Release build and publish passed; 53 automated checks passed (Core 27, Cleanup 14, Speech 12); tray/hook lifecycle, microphone and native CPU runtime diagnostics passed. The user confirmed successful live dictation, with six logged completions under one second. The exact Notepad phrase/app was not recorded; retain target-app checklist coverage below.

### Phase 2: Overlay pill — implemented; live acceptance check pending
- Non-activating, click-through pill, bottom-centre on the active monitor.
- Live RMS waveform while listening; transcribing indicator, brief done check, and error tint/message.
- Short start/stop sounds with a persisted Sounds switch in the tray; --no-sounds disables them at launch. The stop cue runs after capture ends, including post-roll. Sound failures are logged without blocking dictation.
- **Done when:** the pill never steals focus. Test this by typing in Notepad while the pill is visible: text still goes to Notepad.
- **Verified:** --verify-overlay passed native no-activate/click-through style checks, work-area placement, typing into its own foreground editor across overlay states, feedback dismissal and rapid restart checks, and start/stop sound playback. Rendered pill states were visually inspected. Release build passed with zero warnings/errors; 63 regression checks pass (Core 33, Cleanup 18, Speech 12). Live Notepad typing, multiple monitors and mixed DPI still need user validation as remaining manual coverage.

### Phase 3: LLM cleanup — skipped by user request
- No Ollama, LLM rewrite, per-app styles or LLM-specific tests.

### Phase 4: Settings, history, dictionary — implemented and verified
- Live hotkey/microphone settings, atomic JSON preferences, opt-in SQLite history and phrase corrections are implemented.
- **Verified:** 16 data/settings tests cover persistence, corruption handling, retention/deletion, phrase boundaries, case-insensitive upsert and non-cascading corrections. Desktop diagnostics changed the hotkey to F9 and switched to the webcam microphone, then restored previous settings. A saved `cube cuddle` → `kubectl` correction produced `Use kubectl.`.
- Model precision/thread changes apply next launch; this is stated in the UI. Remaining manual checks: personal vocabulary, additional audio devices and history re-paste in target apps.

### Phase 5: Branding — implemented and visually reviewed
- Name: **Hushtype** (renamed from Murmur on 9 October 2026 to avoid confusion with Mumble's Murmur server). A waveform icon is supplied as SVG, PNG and multi-resolution ICO (16/24/32/48/64/128/256 px), used by the app, tray and installer.
- Branded onboarding explains local processing, opens Windows microphone access settings, shows model download/setup progress and teaches the selected hotkey. Successful onboarding is saved.
- Pill states retain the live waveform and processing animation, with a brief fade-in. Settings, history, dictionary and onboarding snapshots have been reviewed.

### Phase 6: Packaging — local artifacts built; distribution configuration pending
- `scripts/Package.ps1` builds a self-contained Windows x64 app, Velopack Setup executable, portable ZIP, full update package and release feed. .NET is bundled; Setup can install the Visual C++ runtime prerequisite.
- Version 0.1.0 artifacts are under `artifacts/releases`, with SHA-256 checksums. Model files remain outside the installer and download on first use, or reuse `--data-dir`.
- Optional launch at Windows sign-in is controlled in Settings. Update/uninstall callbacks maintain the app's startup entry.
- Update source is supplied at packaging time with `-UpdateUrl https://...`; HTTPS feed folders and GitHub repositories are supported. Automatic checks require opt-in; downloaded updates apply at the next launch without interrupting dictation.
- No update endpoint was supplied, so shipped update checks are disabled. No release was uploaded. Packages are unsigned; code signing needs a signing identity and remains a later distribution task. Clean-machine install/uninstall and hosted update verification remain manual/external checks.

---
## 7. Testing checklist (manual, run every phase)

- [x] User confirmed the basic live dictation flow; six logged release-to-paste timings below one second
- [ ] Notepad, VS Code, Chrome text field, Word, Outlook, Teams, Slack
- [ ] Very short utterance (1 word) and long one (60 s)
- [ ] Silence only: nothing is pasted, no hallucinated text
- [ ] English speech with the user’s accent, names and technical terms
- [ ] Clipboard contents preserved after dictation (text and image)
- [ ] Elevated window shows a friendly toast
- [ ] On battery and without CUDA: English Parakeet dictation still works on CPU
- [ ] Rapid repeated presses don't deadlock or overlap pipelines
- [ ] Holding Right Alt doesn't trigger menus in the target app; holding Right Ctrl (default) has no side effects

Unit tests: overlapping-window transcript merging, model integrity/cache/cancellation, the rule-based cleaner, the pipeline state machine (with fake interfaces), and ring-buffer pre-roll maths.

Current tests are executable assertion harnesses: run `dotnet run --project tests/Hushtype.Core.Tests -c Release`, and similarly for `Hushtype.Cleanup.Tests`, `Hushtype.Speech.Tests` and `Hushtype.Data.Tests`. `dotnet test` does not execute these harnesses. Current total: 79 automated checks across Core, Cleanup, Speech and Data. LLM tests are excluded.

### Run the current build

From the workspace root, reuse the downloaded and verified model:

```powershell
.\artifacts\Hushtype\Hushtype.App.exe --data-dir .\.build\parakeet-verification
```

Wait for Ready, focus a text field, hold Right Ctrl (or your chosen key) to speak English, and release to paste. Timing logs for this command are in `.build/parakeet-verification/logs`. Use the tray menu → Quit to stop the app. Build commands and diagnostic options are in [README.md](README.md).

---

## 8. Non-goals (for now)
- Cloud APIs or accounts
- macOS or Linux
- Streaming partial transcripts while speaking (possible later)
- Microsoft Store packaging (the global hook and SendInput fit poorly with MSIX sandboxing)

---

## 9. Instructions for the implementing agent
- Work **phase by phase**. At the end of each phase, build, run, and report what was verified against its "Done when".
- Ask before adding dependencies not listed here.
- Keep Win32 interop isolated in `Hushtype.Platform`, with `[LibraryImport]`/`[DllImport]` signatures in one `NativeMethods.cs`.
- No blocking calls on the UI thread. Model and database work runs off-thread.
- Prefer small, reviewable commits per feature.
- Continue autonomously within the approved plan unless a material question arises. Keep status and verification evidence current; do not mark later phases complete before their acceptance checks pass.
