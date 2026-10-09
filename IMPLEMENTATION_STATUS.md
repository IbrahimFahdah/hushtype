# Implementation status — 9 October 2026

## Phase 1: implemented; live dictation verified by user

The workspace initially contained only PLAN.md. Hushtype.sln now contains the seven planned projects and four executable test projects. This directory is not a Git repository, so no commits were created.

The approved English-only backend is NVIDIA Parakeet TDT 0.6B v2 through sherpa-onnx 1.13.8, running on CPU. The default is INT8 with four inference threads; FP32 is optional. CUDA is no longer a prerequisite. PLAN.md, README.md, dependencies and model attribution have been updated accordingly.

Verified on this machine:

- Release solution build and clean framework-dependent publish succeeded with zero warnings/errors. Runnable output: artifacts/Hushtype/Hushtype.App.exe.
- **53 automated checks passed:** Core 27, Cleanup 14, Speech/model-cache 12. The latter checks pinned downloads, checksum and size validation, cache reuse, corrupt replacement, canceled/failed downloads, and serialized setup.
- Updated interactive-desktop smoke passed: WPF startup, DI graph, tray creation, keyboard hook install/remove, and automatic shutdown.
- Updated hardware probe passed: microphone opened at 16 kHz mono PCM16; sherpa native CPU library loaded.
- Both model profiles downloaded, SHA-256 verified, loaded, warmed, and transcribed the upstream whisper.cpp jfk.wav English fixture correctly. Both produced the same transcript on that fixture.
- Native silence and pre-canceled inference checks passed. One-second and 66-second repeated fixtures also decoded; the longer fixture retained all six repeated passages across overlapping windows.
- Published WPF application speech diagnostic passed with the INT8 model and exited automatically.

## Measured inference

These are local file-based STT measurements, not live hotkey-to-paste timings or a broad accuracy evaluation.

| Profile / fixture | Three runs (ms) | Median (ms) |
| --- | --- | --- |
| INT8 / 11 s / 4 threads, initial comparison | 530, 574, 602 | 574 |
| INT8 / 11 s / 2 threads | 841, 797, 790 | 797 |
| INT8 / 11 s / 8 threads | 654, 781, 694 | 694 |
| FP32 / 11 s / 4 threads | 2889, 2805, 2836 | 2836 |
| INT8 / 1 s / 4 threads | 90, 81, 79 | 81 |
| INT8 / 66 s / 4 threads | 3531, 3457, 3499 | 3499 |

Later verification showed substantial timing variation: the WPF file diagnostic took 3015 ms, the clean published app took 2727 ms, and a repeated console benchmark also slowed into the 2–3 second range. Native DLL hashes matched between the app and console harness. The cause of this variation is not established; the earlier 574 ms result must not be treated as a guaranteed release-to-paste latency. Four threads were fastest in the initial thread comparison; INT8 remains the practical default, with FP32 available for personal accuracy comparison.

Evidence: .build/parakeet-verification/benchmark-*.json and logs; .build/parakeet-smoke/logs; .build/parakeet-probe/logs. Models and test fixtures are retained under ignored .build. Launch with --data-dir .build/parakeet-verification to reuse the verified models. Production logs contain timings rather than transcripts; explicit file diagnostics log the supplied test transcript.

## Live acceptance verification

The user reports that live dictation works correctly. Six live completions logged release-to-paste times of 553, 360, 631, 554, 548 and 468 ms, with audio durations from 2.85 to 9.54 seconds. Startup logs confirm sherpa-onnx, CPU, Parakeet v2 INT8 and four threads. These runs meet the under-one-second latency requirement. The exact target app and test phrase were not recorded; the remaining target-app checklist still needs manual coverage. Phase 2 is now implemented; its verification is recorded below.

Still manual/unverified: AltGr layouts; image clipboard restoration; target applications; elevated-window notification; default microphone switching during real use; sustained live recordings. Build, native inference and desktop diagnostics do not establish these scenarios.

## Phase 2: implemented; live acceptance check pending

- Non-activating, click-through overlay with a live RMS waveform, transcribing/pasting indicators, success check and error feedback. Position uses the foreground monitor work area and physical-pixel coordinates; the application declares PerMonitorV2 DPI awareness.
- Idle/silence/cancellation hides active feedback; success remains briefly visible. A new recording cancels previous dismissal timers. Quit/disable hides the pill, and no completion overlay is shown after disable.
- Soft start/stop sounds use existing NAudio dependencies and run off the UI thread. The stop cue fires after recording and post-roll end. Sounds can be toggled in the tray, persisted in the data directory, or disabled with --no-sounds. Audio output failures do not block dictation.
- Release build: zero warnings/errors. **63 regression checks passed:** Core 33, Cleanup 18, Speech 12, including stop-cue ordering and overlap protection.
- Explicit --verify-overlay desktop diagnostic passed: native styles, monitor work-area containment, real keyboard input into its own foreground editor through every state, success dismissal, cancellation/empty dismissal, and rapid restart. Start/stop playback completed successfully. PNG snapshots were visually inspected.
- Evidence: .build/overlay-final/logs and overlay-*.png. An initial desktop attempt lost foreground focus; the diagnostic now checks editor activation before testing overlay behavior, and subsequent runs passed.
- Still manual: the exact Notepad focus acceptance test, click-through in target applications, microphone-driven waveform during live speech, preferred sound volume, and multiple monitors/mixed DPI. The automated editor test does not establish every target application or hardware configuration.

## Phases 3–6 update

The user requested skipping LLM cleanup and proceeding through settings, branding and packaging. Phase 3 is excluded: no Ollama or LLM integration.

### Phase 4 — implemented

- Live hotkey/microphone settings, atomic JSON persistence with legacy sound migration, model precision/threads for next launch, cleanup and sound controls, opt-in history and launch-at-sign-in controls.
- SQLite history retains 500 processed dictations and supports copy, deletion and delayed re-paste. It is off by default. The personal dictionary uses literal whole-phrase matching and a single correction pass, preserving replacement spelling.
- **79 total regression checks pass:** Core 33, Cleanup 18, Speech 12, Data/settings 16. Tests cover corrupt/invalid settings, retention/deletion, SQL text round trips, dictionary boundaries, longest match and non-cascading replacements.
- Actual desktop diagnostic switched to F9 and the FHD Webcam microphone, reopened capture without process restart, reloaded settings and restored previous choices. A saved cube cuddle → kubectl correction produced Use kubectl.
- Evidence: .build/settings-verification and .build/features-branded logs/snapshots. Final diagnostics use a separate diagnostic database to preserve user history/dictionary.

### Phase 5 — implemented

- Name changed from Murmur to Hushtype on 9 October 2026 (projects, namespaces, data folders, installer ID, assets and docs). Release version 0.1.0. Added SVG/PNG and ICO frames at 16, 24, 32, 48, 64, 128 and 256 pixels. App/tray/installer use the same mark. Onboarding uses the highest-resolution frame.
- Welcome flow covers microphone access, model preparation, the configured hotkey and Start dictating. Completion is saved. The overlay has a short fade-in and retains its waveform/progress feedback.
- Settings, history, dictionary and welcome screens were rendered and inspected; history timestamps display local time.

### Phase 6 — local release built; distribution prerequisites outstanding

- Velopack 1.2.0 runtime and CLI integrated. Bootstrap executes before WPF startup. scripts/Package.ps1 builds self-contained Windows x64 installer, portable ZIP, full update package, release feed and checksums under artifacts/releases.
- .NET is bundled; Setup declares the Visual C++ runtime prerequisite. Models are downloaded separately or reused via --data-dir.
- Settings exposes launch at sign-in; update/uninstall callbacks maintain the startup entry. No startup registration was enabled by testing.
- HTTPS/GitHub update support is wired in, with opt-in startup checking and next-launch application of downloaded updates. No release URL was provided: update checks are disabled in shipped configuration. No publishing or machine installation was performed.
- Artifacts are unsigned. Signing credentials, clean-machine install/uninstall and a real hosted upgrade remain external/manual verification, not completed claims.

## Review notes

- SDK 10.0.401 builds the net10.0-windows target (.NET 10 LTS). The packaged app is self-contained; framework-dependent builds need the .NET 10 Desktop Runtime.
- Model revisions, sizes and SHA-256 checksums are pinned in src/Hushtype.Speech/models.json, including FP32 external weights. Model/license attribution is in MODEL_NOTICE.md.
- All Win32 P/Invoke declarations remain in Hushtype.Platform/NativeMethods.cs.
- Parakeet TDT does not accept Whisper initial prompts; future dictionary support must use explicit post-processing corrections.
- Long audio uses bounded 20-second decoding windows with one-second overlap. Exact overlap-word matching preserves recognized words but can vary punctuation or retain duplicates if shared audio is recognized differently.
- History is opt-in, local and capped at 500 entries. Arabic is outside the approved English-only scope.

## Final release verification

- Final self-contained build at artifacts/Hushtype passed live settings/device and overlay focus diagnostics. Evidence: .build/features-final and .build/features-overlay-final.
- Final welcome screen was inspected after fixing icon resolution. Settings/history/dictionary rendering passed earlier visual review.
- Extracted artifacts/releases/Hushtype-win-Portable.zip into .build/portable-final-0.1.0. Its current/Hushtype.App.exe loaded the native runtime and cached Parakeet INT8 model, transcribed the 11-second fixture correctly in 552.7 ms and exited successfully. This is file inference, not an additional live latency claim.
- Final artifacts: Setup 89,804,984 bytes; portable ZIP 85,325,560 bytes; full update package 85,333,688 bytes. All six listed release-file SHA-256 checksums were verified.
- No app installation, auto-start activation, release upload or signing was performed. Portable top-level launcher is Hushtype.exe; keep its current directory and Update.exe together.
- The extracted portable top-level Hushtype.exe launcher also passed the tray/DI/hook smoke test. It starts the app with its current folder as working directory; use an absolute --data-dir path when launching through that wrapper. Evidence: .build/portable-launcher-absolute/logs.
