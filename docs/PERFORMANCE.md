# DarkShot — Performance

## Measured result (published Release build)

| Metric | Result |
|---|---|
| Published app size (framework-dependent) | **0.6 MB** (+ .NET Desktop Runtime 10) |
| Startup → tray ready | **290 ms** |
| Idle working set | **49.1 MB** |
| Idle private memory | **11.8 MB** |
| Idle CPU (10 s average) | **0 %** of one core |
| Idle handles / threads | 330 / 18 |
| Editor open — working set | **71.1 MB** |
| Editor open — private memory | **20.4 MB** |
| CLI capture end-to-end, 640×400 → 800 px PNG | **290 ms avg** (246–412 ms over 5 runs) |

Environment: AMD Ryzen 7 4800H, 23.4 GB RAM, Windows 11 Home 10.0.26200 (x64), primary display 1920×1080 at 125% scaling (the measurement PowerShell was DPI-unaware, so it printed 1536×864; DarkShot itself is Per-Monitor-V2 and captures physical pixels).

CLI capture timing includes: starting a second DarkShot process, forwarding the command over the named pipe, capturing the screen, scaling, PNG encoding and atomic file write. The raw screen capture itself is a single GDI `BitBlt`.

Reproduce:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File tools/measure.ps1 -ResetSettings
```

The raw output is saved to [`artifacts/performance.txt`](../artifacts/performance.txt).

## Why it is lightweight

- **No WebView2, Electron, Chromium or browser runtime.** UI is native WinForms + GDI+.
- **No third-party NuGet packages.** Only the .NET base class library / Windows Desktop framework.
- **No background polling or animation timers.** Idle cost is a tray icon plus a message-only hotkey window. Measured 0% CPU over 10 seconds.
- **Capture once per gesture.** The frozen desktop bitmap is reused for the selection overlay; only the selected rectangle is cloned into the editor.
- **Bounded undo history** (default 100 snapshots, configurable 10–500). Snapshots hold annotation objects, not image copies.
- **One display-size cache** for the editor canvas; annotations are vector objects drawn over it. Pixelate/blur sample only the small source rect.
- **Lazy history previews.** The history window decodes one thumbnail on selection; it never loads the whole folder.
- **Deterministic disposal** of every `Bitmap`, `Graphics`, frame and frozen desktop.
- Workstation GC and concurrent GC disabled in the project file to avoid extra background GC threads for a small desktop app.

## Transient memory

While the region overlay is open, the app temporarily holds:

- frozen desktop: ~8.3 MB at 1920×1080 (32 bpp),
- dimmed copy: ~6.2 MB (24 bpp),
- the overlay window back-buffer.

On a two-monitor 3840×1080 desktop this is roughly 2× higher. The overlay is closed and all buffers are disposed immediately after the selection is committed or cancelled. The idle figure above (48 MB) is the steady state with no capture open.

## Not a benchmark target

The app intentionally does not support screen recording, scrolling capture, OCR or cloud upload. Those features would add large runtimes, network stacks or continuous capture loops and conflict with the low-footprint goal.
