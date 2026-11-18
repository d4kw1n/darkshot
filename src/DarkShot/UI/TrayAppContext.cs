using System.Drawing;
using System.Windows.Forms;
using DarkShot.Annotations;
using DarkShot.Core;
using DarkShot.UI.Editor;

namespace DarkShot.UI;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly HotkeyManager _hotkeys;
    private readonly ForegroundTracker _foreground;
    private readonly List<EditorForm> _editors = new();
    private SingleInstanceServer? _ipc;
    private SettingsForm? _settingsForm;
    private HistoryForm? _historyForm;
    private bool _capturing;
    private bool _disposed;
    private bool _firstRun;
    private bool _startupLaunch;

    public TrayAppContext(AppSettings settings)
    {
        _settings = settings;
        _firstRun = !File.Exists(AppPaths.SettingsFile);
        _startupLaunch = Environment.GetCommandLineArgs().Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        UiDispatcher.Initialize();

        _foreground = new ForegroundTracker();
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable()) { RoundedEdges = false }
        };

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "DarkShot — screenshot evidence",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) => StartRegionCapture();

        BuildTrayMenu();

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += (_, action) =>
        {
            switch (action)
            {
                case HotkeyAction.Region: StartRegionCapture(); break;
                case HotkeyAction.Fullscreen: StartFullscreenCapture(); break;
                case HotkeyAction.ActiveWindow: StartActiveWindowCapture(); break;
                case HotkeyAction.RepeatLast: RepeatLastRegion(); break;
            }
        };
        RegisterHotkeys();

        if (_settings.StartWithWindows)
            StartupManager.SetEnabled(true);

        Log.Info("Tray application ready.");
        if (_firstRun && !_startupLaunch)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 900 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                Notify("DarkShot is running",
                    "Press PrintScreen to select a region. Right-click the tray icon for all options.",
                    ToolTipIcon.Info);
            };
            timer.Start();
        }
    }

    public void AttachIpc(SingleInstanceServer server)
    {
        _ipc = server;
        _ipc.ArgsReceived += args => UiDispatcher.Post(() => HandleCommand(args));
    }

    private void RegisterHotkeys()
    {
        Log.Info($"Hotkeys requested: Region={_settings.Hotkeys.Region}; Full={_settings.Hotkeys.Fullscreen}; Window={_settings.Hotkeys.ActiveWindow}; Repeat={_settings.Hotkeys.RepeatLast}");
        var failures = _hotkeys.RegisterAll(_settings.Hotkeys);
        if (failures.Count == 0) Log.Info("All configured hotkeys registered.");
        if (failures.Count > 0)
        {
            string text = string.Join(Environment.NewLine,
                failures.Select(f => $"{f.FriendlyAction}: {f.Spec} — {f.Reason}"));
            Log.Warn("Hotkey registration failures:\n" + text);
            Notify("Some hotkeys are unavailable", text, ToolTipIcon.Warning);
        }
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        var spec = HotkeySpec.ParseOrEmpty(_settings.Hotkeys.Region);
        _tray.Text = spec.IsEmpty
            ? "DarkShot — screenshot evidence"
            : $"DarkShot — {spec.ToDisplayString()} to capture";
    }

    private void BuildTrayMenu()
    {
        _menu.Items.Clear();

        var header = new ToolStripMenuItem("DarkShot") { Enabled = false, ForeColor = Theme.Text };
        _menu.Items.Add(header);
        var privacy = new ToolStripMenuItem("Local-only · no telemetry · no cloud upload")
        {
            Enabled = false,
            ForeColor = Theme.TextDim
        };
        _menu.Items.Add(privacy);
        _menu.Items.Add(new ToolStripSeparator());

        AddItem("Capture region", (_, _) => StartRegionCapture(), HotkeyDisplay(_settings.Hotkeys.Region));
        AddItem("Capture full screen", (_, _) => StartFullscreenCapture(), HotkeyDisplay(_settings.Hotkeys.Fullscreen));
        AddItem("Capture active window", (_, _) => StartActiveWindowCapture(), HotkeyDisplay(_settings.Hotkeys.ActiveWindow));
        AddItem("Repeat last region", (_, _) => RepeatLastRegion(), HotkeyDisplay(_settings.Hotkeys.RepeatLast));

        var delayed = new ToolStripMenuItem("Capture region with delay")
        {
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        foreach (int seconds in new[] { 3, 5, 10 })
        {
            var item = new ToolStripMenuItem($"{seconds} seconds")
            {
                ForeColor = Theme.Text,
                BackColor = Theme.Surface
            };
            int captured = seconds;
            item.Click += (_, _) => StartRegionCapture(captured);
            delayed.DropDownItems.Add(item);
        }
        _menu.Items.Add(delayed);
        _menu.Items.Add(new ToolStripSeparator());

        AddItem("Open image…", (_, _) => OpenImageFile());
        AddItem("Image from clipboard", (_, _) => OpenClipboardImage());
        AddItem("History…", (_, _) => OpenHistory());
        AddItem("Settings…", (_, _) => OpenSettings());
        AddItem("Open captures folder", (_, _) => ShellHelper.OpenFolder(_settings.SaveFolder));
        _menu.Items.Add(new ToolStripSeparator());
        AddItem("About DarkShot", (_, _) => ShowAbout());
        AddItem("Exit", (_, _) => ExitApplication());
    }

    private void AddItem(string text, EventHandler click, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        if (!string.IsNullOrWhiteSpace(shortcut))
            item.ShortcutKeyDisplayString = shortcut;
        item.Click += click;
        _menu.Items.Add(item);
    }

    private static string? HotkeyDisplay(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var parsed = HotkeySpec.ParseOrEmpty(spec);
        return parsed.IsEmpty ? null : parsed.ToDisplayString();
    }

    public void HandleCommand(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        if (options.Errors.Count > 0)
        {
            Log.Warn("Command line: " + string.Join("; ", options.Errors));
            Notify("Command line error", string.Join(Environment.NewLine, options.Errors), ToolTipIcon.Warning);
        }

        if (options.Quit)
        {
            ExitApplication();
            return;
        }
        if (options.Help || options.Version) return;
        if (options.Startup) _startupLaunch = true;

        if (options.OpenSettings) { OpenSettings(); return; }
        if (options.OpenHistory) { OpenHistory(); return; }
        if (options.ClipboardImage) { OpenClipboardImage(); return; }
        if (!string.IsNullOrWhiteSpace(options.OpenFile)) { OpenImageFile(options.OpenFile); return; }
        if (options.Rect.HasValue)
        {
            StartRectCapture(options.Rect.Value, options);
            return;
        }
        if (options.Region) { StartRegionCapture(cli: options); return; }
        if (options.Fullscreen || options.AllMonitors) { StartFullscreenCapture(options); return; }
        if (options.ActiveWindow) { StartActiveWindowCapture(options); return; }
        if (options.RepeatLast) { RepeatLastRegion(options); return; }
    }

    private bool TryBeginCapture()
    {
        if (_capturing)
        {
            Log.Info("Capture ignored — another capture is already in progress");
            return false;
        }
        _capturing = true;
        return true;
    }

    private async void StartRegionCapture(int? delayOverride = null, CommandLineOptions? cli = null)
    {
        if (!TryBeginCapture()) return;
        try
        {
            int delay = cli?.DelaySeconds ?? delayOverride ?? _settings.CaptureDelaySeconds;
            if (delay > 0)
            {
                Notify("DarkShot", $"Capturing in {delay} second{(delay == 1 ? "" : "s")}…", ToolTipIcon.Info, cli?.Silent == true);
                await Task.Delay(delay * 1000);
            }
            await Task.Delay(160);

            FrozenDesktop frozen;
            try
            {
                frozen = ScreenCapture.FreezeDesktop(_settings.IncludeCursor);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Freeze desktop failed");
                Notify("Capture failed", ex.Message, ToolTipIcon.Error);
                _capturing = false;
                return;
            }

            var foreground = _foreground.GetActiveWindowInfo();
            var overlay = new CaptureOverlayForm(frozen, _settings, foreground);
            bool processed = false;
            overlay.Completed += (_, result) =>
            {
                processed = true;
                HandleOverlayResult(result, cli);
            };
            overlay.FormClosed += (_, _) =>
            {
                if (!processed) _capturing = false;
            };
            overlay.Show();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Region capture failed");
            Notify("Capture failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
            _capturing = false;
        }
    }

    private void HandleOverlayResult(CaptureOverlayResult result, CommandLineOptions? cli)
    {
        try
        {
            if (result.Action == OverlayAction.Cancel || result.Frame == null)
            {
                result.Dispose();
                return;
            }

            var frame = result.Frame;
            _settings.LastRegion = IntRect.From(result.ScreenBounds);
            SettingsStore.Save(_settings);

            switch (result.Action)
            {
                case OverlayAction.Edit:
                    OpenEditor(frame);
                    return; // ownership transferred
                case OverlayAction.Copy:
                    CopyFrame(frame, cli);
                    break;
                case OverlayAction.Save:
                    SaveFrame(frame, cli);
                    break;
                case OverlayAction.Pin:
                    PinFrame(frame);
                    break;
            }
            result.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Overlay action failed");
            Notify("Action failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
            result.Dispose();
        }
        finally
        {
            _capturing = false;
        }
    }

    private async void StartFullscreenCapture(CommandLineOptions? cli = null)
    {
        if (!TryBeginCapture()) return;
        try
        {
            int delay = cli?.DelaySeconds ?? _settings.CaptureDelaySeconds;
            if (delay > 0)
            {
                Notify("DarkShot", $"Capturing in {delay} second{(delay == 1 ? "" : "s")}…", ToolTipIcon.Info, cli?.Silent == true);
                await Task.Delay(delay * 1000);
            }
            await Task.Delay(120);

            var foreground = _foreground.GetActiveWindowInfo();
            bool all = cli?.AllMonitors == true || (cli == null && _settings.FullscreenScope == FullscreenScope.AllMonitors);
            CaptureFrame frame;
            if (all)
            {
                frame = ScreenCapture.CaptureRect(MonitorLayout.AllMonitorsBounds, CaptureKind.AllMonitors,
                    _settings.IncludeCursor, foreground?.ProcessName, foreground?.Title, foreground?.Handle ?? IntPtr.Zero);
            }
            else
            {
                var monitor = MonitorLayout.GetMonitorAt(Cursor.Position) ?? MonitorLayout.Primary;
                if (monitor == null) throw new InvalidOperationException("No monitor found.");
                frame = ScreenCapture.CaptureRect(monitor.Bounds, CaptureKind.Fullscreen,
                    _settings.IncludeCursor, foreground?.ProcessName, foreground?.Title, foreground?.Handle ?? IntPtr.Zero);
            }

            _capturing = false;
            HandleCapturedFrame(frame, cli);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Full screen capture failed");
            Notify("Capture failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
            _capturing = false;
        }
    }

    private async void StartActiveWindowCapture(CommandLineOptions? cli = null)
    {
        if (!TryBeginCapture()) return;
        try
        {
            int delay = cli?.DelaySeconds ?? _settings.CaptureDelaySeconds;
            if (delay > 0)
            {
                Notify("DarkShot", $"Capturing in {delay} second{(delay == 1 ? "" : "s")}…", ToolTipIcon.Info, cli?.Silent == true);
                await Task.Delay(delay * 1000);
            }
            await Task.Delay(120);

            var info = _foreground.GetActiveWindowInfo();
            if (info == null)
            {
                Notify("No active window", "Could not find a capturable foreground window.", ToolTipIcon.Warning, cli?.Silent == true);
                _capturing = false;
                return;
            }

            var frame = ScreenCapture.CaptureWindow(info, _settings.IncludeCursor);
            _capturing = false;
            HandleCapturedFrame(frame, cli);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Active window capture failed");
            Notify("Capture failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
            _capturing = false;
        }
    }

    private async void RepeatLastRegion(CommandLineOptions? cli = null)
    {
        if (_settings.LastRegion is not { IsValid: true } last)
        {
            Notify("No previous region", "Capture a region first, then use Repeat last region.", ToolTipIcon.Info, cli?.Silent == true);
            return;
        }

        if (!TryBeginCapture()) return;
        try
        {
            int delay = cli?.DelaySeconds ?? _settings.CaptureDelaySeconds;
            if (delay > 0)
            {
                Notify("DarkShot", $"Capturing in {delay} second{(delay == 1 ? "" : "s")}…", ToolTipIcon.Info, cli?.Silent == true);
                await Task.Delay(delay * 1000);
            }
            await Task.Delay(100);

            var foreground = _foreground.GetActiveWindowInfo();
            var frame = ScreenCapture.CaptureRect(last.ToRectangle(), CaptureKind.Region,
                _settings.IncludeCursor, foreground?.ProcessName, foreground?.Title, foreground?.Handle ?? IntPtr.Zero);
            _capturing = false;
            HandleCapturedFrame(frame, cli);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Repeat region failed");
            Notify("Capture failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
            _capturing = false;
        }
    }

    private async void StartRectCapture(Rectangle rect, CommandLineOptions? cli)
    {
        if (!TryBeginCapture()) return;
        try
        {
            int delay = cli?.DelaySeconds ?? 0;
            if (delay > 0) await Task.Delay(delay * 1000);
            await Task.Delay(80);

            var foreground = _foreground.GetActiveWindowInfo();
            var frame = ScreenCapture.CaptureRect(rect, CaptureKind.Region,
                _settings.IncludeCursor, foreground?.ProcessName, foreground?.Title, foreground?.Handle ?? IntPtr.Zero);
            _capturing = false;
            HandleCapturedFrame(frame, cli);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rect capture failed");
            Notify("Capture failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
            _capturing = false;
        }
    }

    private void HandleCapturedFrame(CaptureFrame frame, CommandLineOptions? cli)
    {
        bool transferred = false;
        try
        {
            _settings.LastRegion = IntRect.From(frame.Metadata.ScreenBounds);
            SettingsStore.Save(_settings);

            bool noEditor = cli?.NoEditor == true;
            bool outputSpecified = !string.IsNullOrWhiteSpace(cli?.Output);
            bool copyRequested = cli?.Copy == true;

            if (!noEditor && !outputSpecified && !copyRequested && _settings.AfterCapture == AfterCaptureAction.OpenEditor)
            {
                transferred = true;
                OpenEditor(frame);
                return; // ownership transferred to the editor
            }

            var export = cli?.ApplyTo(_settings.Export) ?? _settings.Export;

            if (copyRequested || _settings.AfterCapture == AfterCaptureAction.CopyToClipboard && !outputSpecified && !noEditor)
            {
                var copy = CaptureOutput.Copy(frame.Image, Array.Empty<Annotation>(), new Rectangle(0, 0, frame.Image.Width, frame.Image.Height),
                    _settings, export, frame.Metadata.CapturedAt);
                if (copy.Success)
                    Notify("Copied to clipboard", $"{copy.PixelSize.Width}×{copy.PixelSize.Height} · {copy.Dpi} DPI", ToolTipIcon.Info, cli?.Silent == true);
                else
                    Notify("Clipboard busy", "The image could not be copied. Try again.", ToolTipIcon.Warning, cli?.Silent == true);
            }

            bool shouldSave = outputSpecified ||
                              _settings.AfterCapture == AfterCaptureAction.SaveToFile ||
                              (noEditor && !copyRequested);
            if (shouldSave)
            {
                string? explicitPath = ResolveOutputPath(cli?.Output, frame.Metadata, export);
                var outcome = CaptureOutput.Save(frame.Image, Array.Empty<Annotation>(),
                    new Rectangle(0, 0, frame.Image.Width, frame.Image.Height),
                    frame.Metadata, _settings, export, explicitPath);

                Notify("Saved screenshot",
                    $"{Path.GetFileName(outcome.FilePath)}\n{outcome.PixelSize.Width}×{outcome.PixelSize.Height} · {outcome.Dpi} DPI",
                    ToolTipIcon.Info, cli?.Silent == true);

                if (_settings.CopyAfterSave && !copyRequested)
                    CaptureOutput.Copy(frame.Image, Array.Empty<Annotation>(),
                        new Rectangle(0, 0, frame.Image.Width, frame.Image.Height), _settings, export, frame.Metadata.CapturedAt);
                if (_settings.OpenFolderAfterSave)
                    ShellHelper.OpenFolder(Path.GetDirectoryName(outcome.FilePath));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Output failed");
            Notify("Output failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
        }
        finally
        {
            if (!transferred)
            {
                try { frame.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    private string? ResolveOutputPath(string? output, CaptureMetadata metadata, ExportSettings export)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            string path = Environment.ExpandEnvironmentVariables(output.Trim());
            if (Directory.Exists(path) || path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
                return Path.Combine(path, CaptureOutput.SuggestedFileName(_settings, metadata, export));
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                return path + ImageFormatInfo.Extension(export.Format);
            return Path.GetFullPath(path);
        }
        catch
        {
            return output;
        }
    }

    private void CopyFrame(CaptureFrame frame, CommandLineOptions? cli)
    {
        var export = cli?.ApplyTo(_settings.Export) ?? _settings.Export;
        var copy = CaptureOutput.Copy(frame.Image, Array.Empty<Annotation>(),
            new Rectangle(0, 0, frame.Image.Width, frame.Image.Height), _settings, export, frame.Metadata.CapturedAt);
        if (copy.Success)
            Notify("Copied to clipboard", $"{copy.PixelSize.Width}×{copy.PixelSize.Height} · {copy.Dpi} DPI", ToolTipIcon.Info, cli?.Silent == true);
        else
            Notify("Clipboard busy", "The image could not be copied.", ToolTipIcon.Warning, cli?.Silent == true);
    }

    private void SaveFrame(CaptureFrame frame, CommandLineOptions? cli)
    {
        try
        {
            var export = cli?.ApplyTo(_settings.Export) ?? _settings.Export;
            string? explicitPath = ResolveOutputPath(cli?.Output, frame.Metadata, export);
            var outcome = CaptureOutput.Save(frame.Image, Array.Empty<Annotation>(),
                new Rectangle(0, 0, frame.Image.Width, frame.Image.Height), frame.Metadata, _settings, export, explicitPath);
            Notify("Saved screenshot",
                $"{Path.GetFileName(outcome.FilePath)}\n{outcome.PixelSize.Width}×{outcome.PixelSize.Height} · {outcome.Dpi} DPI",
                ToolTipIcon.Info, cli?.Silent == true);
            if (_settings.CopyAfterSave) CopyFrame(frame, cli);
            if (_settings.OpenFolderAfterSave) ShellHelper.OpenFolder(Path.GetDirectoryName(outcome.FilePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Overlay save failed");
            Notify("Save failed", ex.Message, ToolTipIcon.Error, cli?.Silent == true);
        }
    }

    private void PinFrame(CaptureFrame frame)
    {
        var bitmap = AnnotationRenderer.Compose(frame.Image, Array.Empty<Annotation>(),
            new Rectangle(0, 0, frame.Image.Width, frame.Image.Height));
        var pin = new PinForm(bitmap, _settings);
        pin.Show();
        Notify("Pinned to screen", "Drag to move · wheel to zoom · right-click for actions", ToolTipIcon.Info);
    }

    private void OpenEditor(CaptureFrame frame)
    {
        try
        {
            var editor = new EditorForm(frame, _settings);
            editor.SettingsRequested += (_, _) => OpenSettings();
            editor.FormClosed += (_, _) => _editors.Remove(editor);
            _editors.Add(editor);
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not open editor");
            Notify("Editor failed", ex.Message, ToolTipIcon.Error);
            try { frame.Dispose(); } catch { /* ignore */ }
        }
    }

    private void OpenImageFile(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Open image in DarkShot",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            path = dialog.FileName;
        }

        try
        {
            var bmp = ImageLoader.LoadUnlocked(path);
            var metadata = ImageLoader.MetadataForImage(path);
            var frame = new CaptureFrame(bmp, metadata);
            OpenEditor(frame);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Open image failed");
            Notify("Could not open image", ex.Message, ToolTipIcon.Error);
        }
    }

    private void OpenClipboardImage()
    {
        var bmp = ClipboardHelper.TryGetImage();
        if (bmp == null)
        {
            Notify("No image on clipboard", "Copy an image first, then try again.", ToolTipIcon.Info);
            return;
        }
        var metadata = new CaptureMetadata
        {
            Kind = CaptureKind.Clipboard,
            CapturedAt = DateTime.Now,
            SourceProcess = "clipboard",
            SourceTitle = "Clipboard image",
            MonitorDpi = 96,
            ImageWidth = bmp.Width,
            ImageHeight = bmp.Height,
            ScreenBounds = Rectangle.Empty
        };
        OpenEditor(new CaptureFrame(bmp, metadata));
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        var form = new SettingsForm(_settings);
        _settingsForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_settingsForm, form)) _settingsForm = null;
        };

        DialogResult result;
        try
        {
            var owner = _editors.FirstOrDefault(e => !e.IsDisposed);
            result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Settings dialog failed");
            result = DialogResult.Cancel;
        }
        finally
        {
            if (ReferenceEquals(_settingsForm, form)) _settingsForm = null;
        }

        if (result == DialogResult.OK)
        {
            _settings.CopyFrom(form.Result);
            bool saved = SettingsStore.Save(_settings);
            StartupManager.SetEnabled(_settings.StartWithWindows);
            ReconfigureAfterSettings();
            foreach (var editor in _editors.ToList())
            {
                try { editor.RefreshFromSettings(); } catch { /* ignore */ }
            }
            Notify(saved ? "Settings saved" : "Settings applied (config not written)",
                saved
                    ? "Hotkeys were re-registered and new captures use the updated output defaults."
                    : "The settings file could not be written. Check the log.",
                saved ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        form.Dispose();
    }

    private void OpenHistory()
    {
        if (_historyForm is { IsDisposed: false })
        {
            _historyForm.Activate();
            return;
        }

        var form = new HistoryForm(_settings, path => OpenImageFile(path));
        _historyForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_historyForm, form)) _historyForm = null;
        };
        form.Show();
        form.Activate();
    }

    private void ShowAbout()
    {
        var version = typeof(TrayAppContext).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        string hotkeys =
            $"Region: {_settings.Hotkeys.Region}\nFull screen: {_settings.Hotkeys.Fullscreen}\nActive window: {_settings.Hotkeys.ActiveWindow}\nRepeat last: {_settings.Hotkeys.RepeatLast}";
        MessageBox.Show(
            $"DarkShot {version}\n\nScreenshot and PoC evidence tool for pentest, red-team and bug-bounty work.\n\n{hotkeys}\n\nLocal-only: no telemetry, no cloud upload, no network access.\nConfig: {AppPaths.SettingsFile}",
            "About DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowStatusNotification(string title, string message, ToolTipIcon icon)
        => Notify(title, message, icon);

    private void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, bool silent = false)
    {
        if (silent || !_settings.ShowNotifications) return;
        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = message;
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(3200);
        }
        catch (Exception ex)
        {
            Log.Warn("Tray notification failed: " + ex.Message);
        }
    }

    private void ExitApplication()
    {
        foreach (var editor in _editors.ToList())
        {
            editor.Close();
            if (!editor.IsDisposed) return; // user cancelled close (unsaved edits)
        }

        try
        {
            _menu.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _hotkeys.Dispose();
            _foreground.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Dispose on exit failed");
        }

        ExitThread();
    }

    public void ReconfigureAfterSettings()
    {
        RegisterHotkeys();
        BuildTrayMenu();
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            base.Dispose(disposing);
            return;
        }
        _disposed = true;
        if (disposing)
        {
            try { _tray.Visible = false; _tray.Dispose(); } catch { /* ignore */ }
            try { _menu.Dispose(); } catch { /* ignore */ }
            try { _hotkeys.Dispose(); } catch { /* ignore */ }
            try { _foreground.Dispose(); } catch { /* ignore */ }
        }
        base.Dispose(disposing);
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
