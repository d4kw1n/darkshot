param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\dist\DarkShot\DarkShot.exe'),
    [switch]$ResetSettings
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$Exe = [System.IO.Path]::GetFullPath($Exe)
if (-not (Test-Path $Exe)) { throw "DarkShot.exe not found: $Exe" }

$artifactDir = Join-Path $PSScriptRoot '..\artifacts'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$log = Join-Path $env:APPDATA 'DarkShot\logs\darkshot.log'
$settingsFile = Join-Path $env:APPDATA 'DarkShot\settings.json'

Get-Process DarkShot -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

if ($ResetSettings -and (Test-Path $settingsFile)) {
    Remove-Item $settingsFile -Force
}
if (Test-Path $log) { Remove-Item $log -Force }

$os = Get-CimInstance Win32_OperatingSystem
$cpuName = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
$ramGb = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
# The script may run DPI-unaware; values are informational.

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $Exe -PassThru
$ready = $false
while ($sw.ElapsedMilliseconds -lt 15000) {
    if ($proc.HasExited) { throw "DarkShot exited during startup" }
    if (Test-Path $log) {
        $text = Get-Content $log -Raw -ErrorAction SilentlyContinue
        if ($text -match 'Tray application ready') { $ready = $true; break }
    }
    Start-Sleep -Milliseconds 20
}
$sw.Stop()
if (-not $ready) { throw "Startup timed out" }
$startupMs = $sw.ElapsedMilliseconds

Start-Sleep -Seconds 3
$proc.Refresh()
$idleWsMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
$idlePrivateMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
$idleHandles = $proc.HandleCount
$idleThreads = $proc.Threads.Count
$cpu1 = $proc.TotalProcessorTime.TotalSeconds
Start-Sleep -Seconds 10
$proc.Refresh()
$cpu2 = $proc.TotalProcessorTime.TotalSeconds
$idleCpuPct = [math]::Round((($cpu2 - $cpu1) / 10.0) * 100.0, 2)

$captures = @()
for ($i = 1; $i -le 5; $i++) {
    $out = Join-Path $env:TEMP ("darkshot-bench-{0}.png" -f $i)
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    $t = [System.Diagnostics.Stopwatch]::StartNew()
    & $Exe --rect 100,100,640,400 --no-editor --output $out --width 800 --print-width 0 --format png --silent
    while (-not (Test-Path $out) -and $t.ElapsedMilliseconds -lt 8000) {
        Start-Sleep -Milliseconds 10
    }
    $t.Stop()
    if (Test-Path $out) {
        $captures += $t.ElapsedMilliseconds
        $size = (Get-Item $out).Length
        Remove-Item $out -Force -ErrorAction SilentlyContinue
    }
}
$captureAvg = if ($captures.Count) { [math]::Round(($captures | Measure-Object -Average).Average, 0) } else { -1 }
$captureMin = if ($captures.Count) { ($captures | Measure-Object -Minimum).Minimum } else { -1 }
$captureMax = if ($captures.Count) { ($captures | Measure-Object -Maximum).Maximum } else { -1 }

# Open the editor (no output → default OpenEditor) and measure its footprint.
& $Exe --rect 100,100,900,600
$editorSeen = $false
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ([DateTime]::UtcNow -lt $deadline) {
    $proc.Refresh()
    if ($proc.MainWindowTitle -eq 'DarkShot Editor') { $editorSeen = $true; break }
    Start-Sleep -Milliseconds 50
}
Start-Sleep -Seconds 2
$proc.Refresh()
$editorWsMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
$editorPrivateMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)

& $Exe --quit
$deadline = [DateTime]::UtcNow.AddSeconds(6)
while (-not $proc.HasExited -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
}

$lines = @(
    "DarkShot performance measurement",
    "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Executable: $Exe",
    "OS: $($os.Caption) $($os.Version)",
    "CPU: $cpuName",
    "RAM: $ramGb GB",
    "Primary screen (DPI-unaware script): $($screen.Width)x$($screen.Height)",
    "",
    "Startup until tray ready: $startupMs ms",
    "Idle working set: $idleWsMb MB",
    "Idle private memory: $idlePrivateMb MB",
    "Idle handles: $idleHandles ; threads: $idleThreads",
    "Idle CPU over 10 s: $idleCpuPct % of one core",
    "Editor working set: $editorWsMb MB",
    "Editor private memory: $editorPrivateMb MB",
    "Editor opened: $editorSeen",
    "CLI capture wall time (process start + IPC + capture + PNG write): avg ${captureAvg} ms, min ${captureMin} ms, max ${captureMax} ms",
    "CLI captures completed: $($captures.Count)/5",
    "",
    "Method: published Release build, no debugger, idle tray with no visible window.",
    "Capture test: always-on region 640x400 -> 800 px wide PNG, no UI, --silent."
)

$text = $lines -join [Environment]::NewLine
$text | Set-Content -Path (Join-Path $artifactDir 'performance.txt') -Encoding UTF8
$text
