# Hushtype — next tasks

Backlog for the next working session. Read [PLAN.md](PLAN.md) and [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) first; the rules in PLAN.md §9 still apply (ask before adding dependencies, Win32 interop only in `Hushtype.Platform/NativeMethods.cs`, build + run the test harnesses after each task, keep status docs current).

Repository: https://github.com/IbrahimFahdah/hushtype (public). Releases are published there via Velopack.

## Source of the feature ideas

Several items below come from comparing against [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube) (Windows port under `windows/`, Avalonia, .NET 10). **That repository has no license, so treat it as reference for ideas only — do not copy its code.** Re-implement in our style.

Already checked: we do **not** currently have search, per-entry enable/disable, applied-correction tracking, or a typing mode. We are ahead of it on in-app model download with SHA-256 verification, the overlay pill, the installer/updates, and the SQLite history with raw + final text.

---

## Priority 1 — must do soon

### 1.1 Move from .NET 8 to .NET 10 (LTS)
.NET 8 reaches end of support on **10 November 2026**. Retarget `Directory.Build.props` to `net10.0-windows`, pin a .NET 10 SDK in `global.json`, re-check each NuGet package's README for .NET 10 compatibility (NAudio, sherpa-onnx, H.NotifyIcon.Wpf, Microsoft.Data.Sqlite, Velopack, Serilog, Microsoft.Extensions.Hosting), regenerate lock files, and update the stack table in PLAN.md and README.
**Done when:** release build has zero warnings, all four test harnesses pass, `--smoke-test` and `--verify-speech` pass with the published self-contained build, and `scripts/Package.ps1` produces a working installer.

### 1.2 Run the keyboard hook on its own thread
The low-level hook is installed on the WPF UI thread (`HotkeyService`). If any UI work (clipboard snapshot, overlay animation, layout) delays the callback past `LowLevelHooksTimeout` (max 1000 ms), Windows **silently removes the hook** and dictation stops working with no error. Move the hook to a dedicated thread with its own message loop; marshal press/release to the dispatcher as today; keep the hook delegate rooted for the hook's lifetime.
**Done when:** `--smoke-test` and the settings diagnostic (hotkey change without restart) still pass; a test that blocks the UI thread for 2 s while the key is pressed and released still delivers both events afterwards.

### 1.3 Stuck-hotkey watchdog
If the key-up never reaches the hook (Win+L, UAC prompt, focus moving into an elevated window), `held` stays true and recording continues until the 5-minute cap. While recording, poll `GetAsyncKeyState(<hotkey>)` (e.g. every 250 ms); if the key is physically up, end the recording as a normal release.
Also consider murmur-youtube's argument for **observing rather than swallowing** the hotkey: swallowing the down but missing the up can leave the target app thinking a modifier is held. Swallowing is still needed for Right Alt (menu bar activation); evaluate per key.

### 1.4 Default hotkey: Right Ctrl instead of Right Alt
Right Alt is AltGr on German, Polish, UK, Nordic and most Latin-American layouts (`@`, `€`, `\`, `|`). We swallow it, so those users can't type those characters while Hushtype runs. Right Ctrl produces no character on any layout and is already a supported option. Change the default for **new** settings only (don't override a saved choice), and show a warning next to Right Alt in Settings. Update onboarding text and docs.

---

## Priority 2 — dictionary and history features

### 2.1 Search history and dictionary
Add a search box to both tabs of `LibraryWindow`. History: case-insensitive match on final and raw text (and app name). Dictionary: match on heard and replacement. Run queries off the UI thread (SQLite `LIKE` with escaped wildcards is sufficient at 500 history rows). Show "No matches" vs "Nothing here yet" empty states.

### 2.2 Enable/disable individual corrections
Add `enabled INTEGER NOT NULL DEFAULT 1` to `dictionary` (schema migration for existing `hushtype.db`). Disabled entries stay listed but are excluded from `PersonalPostProcessor.SetCorrections`. Toggle applies without restart.
Also add **trigger warnings** (never blocking): warn when the "heard" side is a single common English word, is ≤ 3 characters, or equals the replacement.

### 2.3 Show which corrections fired
Make the correction pass report what it changed (`heard-as-actually-matched → replacement × count`). Store it with the history row (e.g. a JSON column `corrections`) and show it as small badges under each history entry. Production logs must still never contain dictated text.

### 2.4 Correction matching improvements
- Match run-together and hyphenated forms of multi-word triggers: a "cube cuddle" rule should also catch "cubecuddle" and "cube-cuddle" (join trigger parts with `[\s\-]*`).
- Normalize both the text and the triggers to Unicode NFC before matching, so accented triggers always match.
- Keep the existing guarantees: whole-word boundaries, longest match wins, single pass (no cascades), literal replacement.
Add test cases for each.

### 2.5 Dictionary import/export (optional)
Plain-text format, one entry per line: `heard -> replacement`, `# off: heard -> replacement` for disabled entries, `#` for comments. Useful for backup and editing in bulk.

---

## Priority 3 — robustness and reach

### 3.1 Optional direct typing mode
Setting: "Insert text by: Paste (default) / Type". Typing sends `KEYEVENTF_UNICODE` via `SendInput` in chunks of ~40 UTF-16 units (never split a surrogate pair), with a real `VK_RETURN` for newlines. Suggested default when enabled: type short text (≤ 200 chars, no newlines), fall back to paste for longer text. Tag injected input via `dwExtraInfo` so our own hook ignores it. Helps apps that block or mishandle the clipboard, and leaves the clipboard untouched.

### 3.2 Detect a blocked microphone
When Windows "Let desktop apps access your microphone" is off, capture succeeds but delivers digital silence; today that looks like "nothing said" and nothing is pasted. A run of ~1.5 s of **exact** zero samples is a reliable signal (real mics have a noise floor). Show a notification with a link to the Windows microphone privacy settings.

### 3.3 Split long recordings near pauses
We decode in 20 s windows with 1 s overlap and merge the overlapping words. Try cutting near the quietest ~20 ms frame within the last few seconds before each boundary (keeping a small overlap) so cuts land between words. Measure on the 66 s fixture and a real long recording: duplicate/missing words at boundaries before vs after. Keep whichever is better; don't remove the overlap merge unless the measurement supports it.

### 3.4 Investigate the 2–3 s inference variance
File benchmarks varied from ~0.57 s to ~3 s on the same 11 s fixture. Hypothesis: Windows power throttling / EcoQoS moves inference threads of a background tray app onto the i9-13900H's efficiency cores. Try opting out with `SetProcessInformation(ProcessPowerThrottling)` (execution-speed throttling off) and compare repeated benchmarks with the app in the background, on AC and on battery.

---

## Smaller items from the code review
- `IsElevated` throws if the target process can't be opened, which blocks the paste; treat that as "not elevated" and attempt the paste.
- Model files are fully re-hashed on every launch (661 MB INT8, 2.5 GB FP32). Cache a verified marker (size + last-write time + hash) and re-hash only when it changes.
- The silence threshold (RMS 0.006) is fixed; quiet microphones may get nothing pasted, with no message. Make it adaptive or notify when audio was too quiet.
- `--speech-threads abc` produces a raw parse error; a second instance exits silently; Arabic leftovers remain in the cleanup code/tests despite the English-only scope.

## Still-open manual checks
See the checklist in PLAN.md §7 (target apps, long utterances, AltGr layouts, clipboard image restore, elevated windows, battery, multi-monitor/DPI, clean-machine install).
