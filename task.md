# Hushtype — next tasks

Backlog for the next working session. Latest release: 0.2.0 (draft uploaded 9 October 2026). Read [PLAN.md](PLAN.md) and [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) first; the rules in PLAN.md §9 still apply (ask before adding dependencies, Win32 interop only in `Hushtype.Platform/NativeMethods.cs`, build + run the test harnesses after each task, keep status docs current).

Repository: https://github.com/IbrahimFahdah/hushtype (public). Releases are published there via Velopack.

## Release process
1. Bump the version and build packages that check GitHub for updates:
   `powershell -File scripts/Package.ps1 -Version 0.2.1 -UpdateUrl https://github.com/IbrahimFahdah/hushtype`
2. Upload as a **draft** release:
   `.build/tools/vpk.exe upload github --outputDir artifacts/releases --repoUrl https://github.com/IbrahimFahdah/hushtype --token <gh auth token> --releaseName "Hushtype 0.2.1" --tag v0.2.1`
3. Review the draft on GitHub, then publish it. Installed apps only see **published, non-prerelease** releases (`GithubSource(..., prerelease: false)`).

Packages are unsigned, so Windows SmartScreen will warn on first install until code signing is set up.

## Source of the feature ideas

Several 0.2.0 items came from comparing against [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube) (Windows port under `windows/`, Avalonia, .NET 10). **That repository has no license, so treat it as reference for ideas only — do not copy its code.** Re-implement in our style.

Search, per-entry enable/disable, applied-correction tracking and a typing mode were added in 0.2.0. We are also ahead of it on in-app model download with SHA-256 verification, the overlay pill, the installer/updates, and the SQLite history with raw + final text.

---

## Done in 0.2.0 (9 October 2026)

Everything previously listed here was implemented; evidence and measurements are in [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md#020-backlog--9-october-2026).

- 1.1 .NET 10 LTS · 1.2 hook on its own thread · 1.3 stuck-hotkey watchdog (Right Ctrl observed, Right Alt/F-keys swallowed) · 1.4 Right Ctrl default with AltGr warning
- 2.1 search · 2.2 per-entry on/off + trigger warnings · 2.3 applied-correction badges · 2.4 run-together/hyphenated matching + NFC · 2.5 import/export
- 3.1 typing mode · 3.2 blocked/quiet microphone notices · 3.3 cut at pauses (kept: fewer boundary duplicates) · 3.4 power-throttling opt-out (forced EcoQoS was ~7.5× slower)
- Code-review items: elevation check, model hash cache, adaptive silence gate + quiet notice, `--speech-threads` message, second-instance handling, Arabic leftovers

## Follow-ups

- Confirm the blocked-microphone notice live: turn off "Let desktop apps access your microphone" in Windows Settings and dictate. The registry toggle did not change capture on this machine, and both microphones here deliver near-silence (default Intel array at 8% input level).
- Measure inference on battery with and without the throttling opt-out.
- Check pause-aware splitting on a real long recording (only a 66 s repeated fixture and an 85 s synthetic TTS clip were measured).
- Code signing, so SmartScreen stops warning.

## Still-open manual checks
See the checklist in PLAN.md §7 (target apps, long utterances, AltGr layouts, clipboard image restore, elevated windows, battery, multi-monitor/DPI, clean-machine install).
