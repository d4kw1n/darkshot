# DarkShot — Research & Design Notes

> Goal: a Lightshot-class screenshot tool optimized for **pentest / red-team / bug-bounty PoC evidence**, with **report-ready output resolution** and a **very small RAM/CPU footprint**.

## 1. Competitive scan

| Tool | Strengths | Gaps for PoC work |
|---|---|---|
| **Lightshot** | Fast region select, instant editor, copy/upload, minimal UI | No step numbering, weak redaction, no target width/DPI control, cloud upload by default, no local workflow |
| **ShareX** | Huge feature set, workflows, scrolling capture, uploaders | Heavy/daunting UI, .NET + many dependencies, most pentest-specific needs require manual setup, telemetry/upload integrations are easy to misconfigure |
| **Greenshot** | Solid editor, export presets | Dated UI, no step numbering/redaction pipeline, limited DPI-aware output control |
| **Flameshot** | Excellent annotation UX (Linux-first) | Weak Windows integration, no report sizing/DPI controls, no local evidence workflow |
| **Snipaste** | Excellent pin-to-screen, fast | Closed source, annotation set is general-purpose, no PoC numbering/redaction focus |

### Differentiators for DarkShot
1. **Evidence-first annotation set**: auto-increment step badges, solid redaction, pixelate, blur, arrows, highlight, text.
2. **Report-ready output**: target pixel width, DPI metadata, optional physical print width (cm) so Word inserts the image at a predictable size; PNG/JPEG quality control; timestamp watermark.
3. **Local-only by default**: no telemetry, no cloud upload, no network stack usage. Screenshots can contain credentials/tokens.
4. **Lightweight**: native WinForms/GDI+ — no Chromium/WebView2, no Electron, no background polling. Hidden tray footprint; editor loaded only when needed.
5. **Scriptable**: command-line capture (`--rect`, `--output`, `--copy`, `--no-editor`) and a single-instance command pipe for automation.
6. **Mixed-DPI/multi-monitor**: Per-Monitor-V2 awareness, physical-pixel capture, virtual desktop overlay, DWM window bounds.

## 2. Feature scope

### Capture
- Region select with frozen desktop, dimming, 8× magnifier, pixel color, size readout.
- Hover window detection (DWM extended frame bounds) — click a window to capture it.
- Resize/move selection before committing.
- Full screen (current monitor / all monitors), active window, last region.
- Configurable delay (0–10 s) with countdown.
- Optional mouse cursor in capture.
- Multi-monitor incl. negative coordinates; mixed DPI.

### Editor
- Tools: select/move, pen, highlighter, line, arrow, rectangle, ellipse, text, step badge, pixelate, blur, solid redact, crop.
- Shape fill toggle, color palette + custom color, stroke width, font size/bold, pixel block / blur strength.
- Auto-increment step counter with reset.
- Selection: move, 8-handle resize (shapes), endpoint handles (line/arrow), delete, duplicate.
- Undo/redo, zoom (fit/100%/cursor-anchored), pan, crop viewport, reset crop.
- Copy, quick save, save as, pin to screen, open image file.
- Dirty-state close confirmation; keyboard shortcuts.

### Output (Word report focus)
- Target width: original / 800 / 1000 / 1200 / 1600 / 1920 / custom.
- DPI metadata: 96 / 120 / 150 / 192 / 220 / 300 / custom.
- Optional **physical print width in cm** → DPI is computed as `width_px / (cm / 2.54)`, so Word/print size is deterministic.
- PNG (lossless), JPEG (quality slider), BMP.
- Optional 1-px border and timestamp/evidence watermark.
- Quick save with filename template tokens, auto-unique names, folder auto-create.
- Clipboard copy as bitmap + PNG (pastes cleanly into Word).

### Workflow
- Tray application, single instance, startup with Windows.
- Configurable global hotkeys per action, conflict detection.
- After-capture action: open editor / copy / save.
- History browser over the save folder (thumbnails, open, copy path, reveal, delete).
- Portable JSON settings (`portable.txt` next to exe) or `%AppData%\DarkShot`.
- CLI automation and named-pipe control of a running instance.
- No cloud upload.

### Non-goals (v1)
- Screen recording / GIF; scrolling capture; OCR; cloud sharing. These add weight or attack surface and are not required for static PoC evidence. Architecture leaves room for an optional OCR/upload plugin later.

## 3. Architecture

```
Hotkey (RegisterHotKey, message-only window)
        │
TrayAppContext ── CaptureService ── ScreenCapture (GDI BitBlt, physical px)
        │                 │
        │                 ├── CaptureOverlay (frozen desktop, selection, magnifier)
        │                 └── CaptureFrame (bitmap + metadata)
        │
        ├── EditorForm (custom canvas, annotations, undo/redo, crop)
        │        ├── AnnotationRenderer (GDI+)
        │        ├── ExportEngine (compose → scale → watermark → encode)
        │        └── PinForm / clipboard / save
        ├── SettingsForm
        └── HistoryForm
```

### Why WinForms/GDI+ (not Electron/Tauri/WPF)
- No browser runtime: idle tray ~20–40 MB; editor with a 4K bitmap ~80–120 MB working set.
- GDI+ is enough for vector annotations, alpha, text, and image scaling — zero external packages.
- WPF adds composition/render-thread overhead for a tool that is mostly a static bitmap + shapes.
- Tauri would still pull WebView2 (100+ MB) and Rust compile complexity for a Windows-only tool.
- `PublishTrimmed`/NativeAOT are not supported by WinForms; a framework-dependent build is tiny (~1 MB) and a self-contained portable build is provided for machines without .NET.

### Performance rules
- Capture once per gesture; crop from the frozen frame.
- No continuous timers while idle; only hotkey messages + tray icon.
- Editor invalidates only the canvas; annotations are lightweight objects.
- Pixelate/blur sample the original screenshot region (never allocate a full-canvas copy per frame).
- Dispose every `Bitmap`/`Graphics` deterministically; release capture frames when editor closes.
- History thumbnails generated lazily, never for the whole folder up front.
- Workstation GC, no server GC, no background rendering loop.

### Security & privacy
- No network code in v1; no telemetry; no upload.
- Settings/history only reference local files.
- Clipboard contains sensitive images by design; no automatic persistence beyond the configured save folder.
- Logs contain paths/errors only, never image content.
- Runs `asInvoker` (no admin). UAC/secure desktop cannot be captured by design.

## 4. Success criteria
- `dotnet build -c Release` succeeds with zero warnings-as-errors.
- Automated core tests cover filename tokens, export geometry/DPI, annotation hit-testing/undo, hotkey parsing.
- E2E: global hotkey → region select → annotate → save produces a PNG with the configured pixel width/DPI.
- Idle tray RAM measured and documented; startup time documented.
- README (Vietnamese) with usage, shortcuts, Word export recipes, build/publish, privacy notes.
