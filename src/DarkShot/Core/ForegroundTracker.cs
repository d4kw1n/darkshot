using System.Diagnostics;
using System.Drawing;

namespace DarkShot.Core;

public sealed class ForegroundTracker : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _hook;
    private IntPtr _lastExternal;
    private bool _disposed;

    public ForegroundTracker()
    {
        _callback = OnWinEvent;
        try
        {
            _hook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _callback,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && NativeMethods.GetWindowPid(fg) != Environment.ProcessId)
                _lastExternal = fg;
        }
        catch (Exception ex)
        {
            Log.Warn("Foreground tracker unavailable: " + ex.Message);
        }
    }

    public IntPtr LastExternalWindow
    {
        get
        {
            if (_lastExternal != IntPtr.Zero && NativeMethods.IsWindow(_lastExternal))
                return _lastExternal;
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && NativeMethods.GetWindowPid(fg) != Environment.ProcessId)
                _lastExternal = fg;
            return _lastExternal;
        }
    }

    public WindowInfo? GetActiveWindowInfo()
    {
        var fg = NativeMethods.GetForegroundWindow();
        if (fg != IntPtr.Zero && NativeMethods.GetWindowPid(fg) != Environment.ProcessId)
        {
            var direct = WindowEnumerator.GetWindowInfo(fg, allowOwnProcess: false);
            if (direct != null) return direct;
        }

        var last = LastExternalWindow;
        return last != IntPtr.Zero ? WindowEnumerator.GetWindowInfo(last, allowOwnProcess: false) : null;
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return;
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (NativeMethods.GetWindowPid(hwnd) == Environment.ProcessId) return;
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsCloaked(hwnd)) return;
        _lastExternal = hwnd;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            try { NativeMethods.UnhookWinEvent(_hook); } catch { /* ignore */ }
            _hook = IntPtr.Zero;
        }
    }
}

public static class UiDispatcher
{
    private static Control? _anchor;

    public static void Initialize()
    {
        if (_anchor != null) return;
        _anchor = new Control();
        _ = _anchor.Handle;
    }

    public static void Post(Action action)
    {
        var anchor = _anchor;
        if (anchor == null || anchor.IsDisposed)
        {
            try { action(); } catch (Exception ex) { Log.Error(ex, "UI dispatcher action failed"); }
            return;
        }

        try
        {
            if (anchor.InvokeRequired)
                anchor.BeginInvoke(new Action(() =>
                {
                    try { action(); } catch (Exception ex) { Log.Error(ex, "UI dispatcher action failed"); }
                }));
            else
                action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UI dispatcher post failed");
        }
    }
}

public static class ImageLoader
{
    public static Bitmap LoadUnlocked(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var source = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: true);
        var bmp = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        }
        bmp.SetResolution(96f, 96f);
        return bmp;
    }

    public static CaptureMetadata MetadataForImage(string path)
    {
        DateTime time = DateTime.Now;
        try { time = File.GetLastWriteTime(path); } catch { /* ignore */ }
        return new CaptureMetadata
        {
            Kind = CaptureKind.OpenedFile,
            CapturedAt = time,
            SourceProcess = "file",
            SourceTitle = Path.GetFileNameWithoutExtension(path),
            MonitorDpi = 96
        };
    }
}

public static class ShellHelper
{
    public static void OpenFolder(string? folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Open folder failed: " + ex.Message);
        }
    }

    public static void SelectFile(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                OpenFolder(Path.GetDirectoryName(path));
        }
        catch (Exception ex)
        {
            Log.Warn("Select file failed: " + ex.Message);
        }
    }

    public static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Open path failed: " + ex.Message);
        }
    }

    public static bool DeleteToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var op = new NativeMethods.SHFILEOPSTRUCT
            {
                wFunc = NativeMethods.FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = (ushort)(NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION |
                                 NativeMethods.FOF_SILENT | NativeMethods.FOF_NOERRORUI)
            };
            int result = NativeMethods.SHFileOperation(ref op);
            return result == 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Recycle bin delete failed: " + ex.Message);
            return false;
        }
    }
}
