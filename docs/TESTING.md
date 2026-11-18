# DarkShot — Testing log

All tests are Windows GUI/integration tests on the development desktop. Test images are synthetic; the UI screenshot test writes its own mock screenshots. No user screenshots are part of the repository.

## Build

```text
dotnet build .\DarkShot.slnx -c Release
dotnet publish .\src\DarkShot\DarkShot.csproj -c Release -r win-x64 --self-contained false -o .\dist\DarkShot
```

Result: **succeeded**, 0 warnings, published size **0.6 MB**.

## Automated unit tests

Command:

```powershell
.\tests\DarkShot.Tests\bin\Debug\net10.0-windows\DarkShot.Tests.exe tests
```

Result: **102 passed, 0 failed**.

Coverage:

- hotkey parsing/normalisation/rejection (bare letters, modifier-only, unknown keys), plus the hotkey recorder UI control,
- CLI parsing including **negative multi-monitor coordinates**, formats, export overrides,
- filename tokens, custom `{date:yyyyMMdd-HHmmss}` formats, sanitisation, reserved names, unique suffixes,
- settings normalisation/clamping and `CopyFrom`,
- annotation hit-testing, moving, 8-handle resizing, line endpoints, step numbering, text measurement, cloning,
- undo/redo stack semantics,
- export scaling, DPI metadata, print-width derivation, crop, border, timestamp, redaction, pixelate, blur, JPEG round-trip,
- physical screen capture, frozen-desktop crop, negative/positive coordinates,
- save + PNG DPI metadata + unlocked image loading,
- clipboard `DataObject` contains both `CF_BITMAP` and a valid PNG flavour (PNG signature checked).

## Editor UI test

Command:

```powershell
.\tests\DarkShot.Tests\bin\Debug\net10.0-windows\DarkShot.Tests.exe editor
```

Synthetic mock admin console (1200×700), driven by real Win32 mouse messages:

```text
Text annotation committed
Editor screenshot saved (1488x990), annotations=9, crop={X=0,Y=0,Width=1200,Height=700}
Undo/redo: 9 -> 8 -> 9
Crop applied: cropped=True crop={X=40,Y=40,Width=1120,Height=620}
Crop undo: cropped=False crop={X=0,Y=0,Width=1200,Height=700}
QuickSave=True path=...editor-test_..._5.png
EDITOR TEST OK output=... size=1200x700 dpi=191.0
```

This exercises arrow, rectangle, three step badges, pixelate, blur, solid redact, text commit, undo/redo, crop + crop undo, and export with target width + 16 cm print width + timestamp + border.

## Overlay test

Command:

```powershell
.\tests\DarkShot.Tests\bin\Debug\net10.0-windows\DarkShot.Tests.exe overlay-auto
```

```text
Overlay completed action=Edit bounds={X=-1320,Y=350,Width=1000,Height=500} source=Account Management - Chromium
Saved ...\overlay-result.png 1000x500
```

Then the real tray app was launched and a second instance sent `--region`; a real drag selected 900×500, the **Edit** button in the action bar was clicked, and the editor opened with the correct capture. This verified the overlay → editor integration and the exact toolbar hit area.

## CLI / single-instance tests

```powershell
DarkShot.exe --rect 100,100,640,400 --no-editor --output cli-output.png --width 800 --print-width 16 --timestamp --silent
# file is 800×500 @ 127 DPI
DarkShot.exe --repeat --no-editor --output repeat-output.png --silent
# 1200×750 (16:10 aspect of the previous 640×400 region; test config had a 1200 px output width)
DarkShot.exe --full --no-editor --output full-output.png --silent
# 1200×675 (16:9)
DarkShot.exe --rect 200,200,320,240 --delay 1 --no-editor --output delay-output.png --silent
# 1200×900 (4:3), delay path worked
DarkShot.exe --window --no-editor --output window-output.png --width 800 --silent
# 800×425 @ 96 DPI, active foreground window captured
DarkShot.exe --quit
# process exited, tray icon removed
```

Hotkeys registered successfully in the same run (log): `Region=PrintScreen; Full=Ctrl+PrintScreen; Window=Ctrl+Alt+PrintScreen; Repeat=Ctrl+Shift+PrintScreen` — "All configured hotkeys registered."

## Settings / History / Pin UI

Command:

```powershell
.\tests\DarkShot.Tests\bin\Debug\net10.0-windows\DarkShot.Tests.exe ui
```

```text
saved settings-section-0/1/2.png
Settings submit: width=1200 print=16cm timestamp=True format=Png folder=...\artifacts\history-test
SETTINGS SAVE TEST OK
saved history-window.png
saved pin-window.png
UI SCREENSHOT TEST OK
```

Synthetic screenshots are in [`docs/images/`](images/): `settings-capture.png`, `settings-output.png`, `settings-general.png`, `export-dialog.png`, `history.png`, `pin.png`, `editor.png`.

## Clipboard

The clipboard data object is verified to contain both `CF_BITMAP` and `PNG` (signature `89 50 4E 47`). The OS-level clipboard round-trip test is deliberately skipped when the active clipboard contains proprietary formats (for example a Chromium internal token) to avoid destroying the user's clipboard. Manual check:

1. In the editor press **Ctrl+C**.
2. Paste into Word / Paint / any chat app.
3. Confirm the image and its configured pixel size.

## Performance

Measured on the published Release build: see [PERFORMANCE.md](PERFORMANCE.md).
