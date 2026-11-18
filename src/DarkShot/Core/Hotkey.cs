using System.Windows.Forms;

namespace DarkShot.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

public enum HotkeyAction
{
    Region = 1,
    Fullscreen = 2,
    ActiveWindow = 3,
    RepeatLast = 4
}

public readonly record struct HotkeySpec(HotkeyModifiers Modifiers, Keys Key)
{
    public bool IsEmpty => Key == Keys.None;

    public static HotkeySpec Empty => new(HotkeyModifiers.None, Keys.None);

    public static bool TryParse(string? text, out HotkeySpec spec, out string error)
    {
        spec = Empty;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
            return true;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Hotkey is empty.";
            return false;
        }

        var mods = HotkeyModifiers.None;
        string? keyToken = null;

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var mod))
            {
                mods |= mod;
                continue;
            }

            if (keyToken != null)
            {
                error = "Only one non-modifier key is allowed.";
                return false;
            }

            keyToken = part;
        }

        if (keyToken == null)
        {
            error = "Missing key (for example PrintScreen or F8).";
            return false;
        }

        if (!TryParseKey(keyToken, out var key))
        {
            error = $"Unknown key: {keyToken}";
            return false;
        }

        if (key == Keys.None)
        {
            error = "Invalid key.";
            return false;
        }

        if (IsBareTypingKey(mods, key))
        {
            error = "Use Ctrl, Alt or Shift with letters/numbers so DarkShot does not block normal typing.";
            return false;
        }

        spec = new HotkeySpec(mods, key);
        return true;
    }

    public static HotkeySpec ParseOrEmpty(string? text)
        => TryParse(text, out var spec, out _) ? spec : Empty;

    public int ToWin32Modifiers()
    {
        int value = NativeMethods.MOD_NOREPEAT;
        if ((Modifiers & HotkeyModifiers.Alt) != 0) value |= NativeMethods.MOD_ALT;
        if ((Modifiers & HotkeyModifiers.Control) != 0) value |= NativeMethods.MOD_CONTROL;
        if ((Modifiers & HotkeyModifiers.Shift) != 0) value |= NativeMethods.MOD_SHIFT;
        if ((Modifiers & HotkeyModifiers.Win) != 0) value |= NativeMethods.MOD_WIN;
        return value;
    }

    public string ToDisplayString()
    {
        if (IsEmpty) return "Not set";

        var parts = new List<string>();
        if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(KeyDisplayName(Key));
        return string.Join("+", parts);
    }

    public override string ToString() => ToDisplayString();

    private static bool TryParseModifier(string token, out HotkeyModifiers mod)
    {
        switch (token.Trim().ToUpperInvariant())
        {
            case "CTRL":
            case "CONTROL":
                mod = HotkeyModifiers.Control;
                return true;
            case "ALT":
                mod = HotkeyModifiers.Alt;
                return true;
            case "SHIFT":
                mod = HotkeyModifiers.Shift;
                return true;
            case "WIN":
            case "WINDOWS":
            case "SUPER":
            case "META":
                mod = HotkeyModifiers.Win;
                return true;
            default:
                mod = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        var t = token.Trim();
        var upper = t.ToUpperInvariant();

        switch (upper)
        {
            case "PRINTSCREEN":
            case "PRTSC":
            case "PRTSCN":
            case "PRINT":
                key = Keys.PrintScreen;
                return true;
            case "ESC":
            case "ESCAPE":
                key = Keys.Escape;
                return true;
            case "DEL":
            case "DELETE":
                key = Keys.Delete;
                return true;
            case "INS":
            case "INSERT":
                key = Keys.Insert;
                return true;
            case "ENTER":
            case "RETURN":
                key = Keys.Enter;
                return true;
            case "SPACE":
            case "SPACEBAR":
                key = Keys.Space;
                return true;
            case "BACKSPACE":
            case "BACK":
                key = Keys.Back;
                return true;
            case "TAB":
                key = Keys.Tab;
                return true;
            case "UP":
                key = Keys.Up;
                return true;
            case "DOWN":
                key = Keys.Down;
                return true;
            case "LEFT":
                key = Keys.Left;
                return true;
            case "RIGHT":
                key = Keys.Right;
                return true;
            case "PAGEUP":
            case "PGUP":
                key = Keys.PageUp;
                return true;
            case "PAGEDOWN":
            case "PGDN":
                key = Keys.PageDown;
                return true;
            case "HOME":
                key = Keys.Home;
                return true;
            case "END":
                key = Keys.End;
                return true;
            case "PAUSE":
            case "BREAK":
                key = Keys.Pause;
                return true;
            case "SCROLLLOCK":
                key = Keys.Scroll;
                return true;
            case "PLUS":
                key = Keys.Oemplus;
                return true;
            case "MINUS":
                key = Keys.OemMinus;
                return true;
        }

        if (t.Length == 1)
        {
            char c = char.ToUpperInvariant(t[0]);
            if (c >= 'A' && c <= 'Z')
            {
                key = Keys.A + (c - 'A');
                return true;
            }
            if (c >= '0' && c <= '9')
            {
                key = Keys.D0 + (c - '0');
                return true;
            }
        }

        if (upper.StartsWith('F') && int.TryParse(upper[1..], out int f) && f >= 1 && f <= 24)
        {
            key = Keys.F1 + (f - 1);
            return true;
        }

        if (Enum.TryParse<Keys>(t, ignoreCase: true, out key) && key != Keys.None)
        {
            if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin
                or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
                or Keys.LMenu or Keys.RMenu)
            {
                key = Keys.None;
                return false;
            }
            return true;
        }

        key = Keys.None;
        return false;
    }

    private static bool IsBareTypingKey(HotkeyModifiers mods, Keys key)
    {
        if (mods != HotkeyModifiers.None) return false;
        int k = (int)key;
        if (k >= (int)Keys.A && k <= (int)Keys.Z) return true;
        if (k >= (int)Keys.D0 && k <= (int)Keys.D9) return true;
        if (k >= (int)Keys.NumPad0 && k <= (int)Keys.NumPad9) return true;
        if (key is Keys.Space or Keys.Enter or Keys.Back or Keys.Tab) return true;
        return false;
    }

    public static string KeyDisplayName(Keys key)
    {
        return key switch
        {
            Keys.PrintScreen => "PrintScreen",
            Keys.Escape => "Esc",
            Keys.Delete => "Del",
            Keys.Insert => "Ins",
            Keys.Enter => "Enter",
            Keys.Back => "Backspace",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Scroll => "ScrollLock",
            Keys.Oemplus => "Plus",
            Keys.OemMinus => "Minus",
            _ when (int)key >= (int)Keys.D0 && (int)key <= (int)Keys.D9 => ((char)('0' + ((int)key - (int)Keys.D0))).ToString(),
            _ when (int)key >= (int)Keys.A && (int)key <= (int)Keys.Z => ((char)('A' + ((int)key - (int)Keys.A))).ToString(),
            _ => key.ToString()
        };
    }
}

public sealed record HotkeyRegistrationFailure(HotkeyAction Action, string Spec, string Reason)
{
    public string FriendlyAction => Action switch
    {
        HotkeyAction.Region => "Region capture",
        HotkeyAction.Fullscreen => "Full screen",
        HotkeyAction.ActiveWindow => "Active window",
        HotkeyAction.RepeatLast => "Repeat last region",
        _ => Action.ToString()
    };
}

public sealed class HotkeyManager : IDisposable
{
    private readonly HotkeySink _sink;
    private readonly HashSet<int> _registered = new();
    private bool _disposed;

    public event EventHandler<HotkeyAction>? Pressed;

    public HotkeyManager()
    {
        _sink = new HotkeySink(this);
        _sink.CreateHandle(new CreateParams
        {
            Caption = "DarkShot.HotkeySink",
            Parent = NativeMethods.HWND_MESSAGE
        });
    }

    public IReadOnlyList<HotkeyRegistrationFailure> RegisterAll(HotkeySettings settings)
    {
        UnregisterAll();

        var failures = new List<HotkeyRegistrationFailure>();
        var entries = new (HotkeyAction Action, string Text)[]
        {
            (HotkeyAction.Region, settings.Region),
            (HotkeyAction.Fullscreen, settings.Fullscreen),
            (HotkeyAction.ActiveWindow, settings.ActiveWindow),
            (HotkeyAction.RepeatLast, settings.RepeatLast)
        };

        foreach (var (action, text) in entries)
        {
            if (!HotkeySpec.TryParse(text, out var spec, out var error))
            {
                failures.Add(new HotkeyRegistrationFailure(action, text, error));
                continue;
            }

            if (spec.IsEmpty) continue;

            int id = (int)action;
            if (NativeMethods.RegisterHotKey(_sink.Handle, id, spec.ToWin32Modifiers(), (int)spec.Key))
            {
                _registered.Add(id);
            }
            else
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                string reason = err == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                    ? "Already used by another application."
                    : $"Windows error {err}.";
                failures.Add(new HotkeyRegistrationFailure(action, spec.ToDisplayString(), reason));
            }
        }

        return failures;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered)
        {
            try { NativeMethods.UnregisterHotKey(_sink.Handle, id); } catch { /* ignore */ }
        }
        _registered.Clear();
    }

    internal void OnHotkey(int id)
    {
        if (Enum.IsDefined(typeof(HotkeyAction), id))
            Pressed?.Invoke(this, (HotkeyAction)id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _sink.DestroyHandle();
    }

    private sealed class HotkeySink : NativeWindow
    {
        private readonly HotkeyManager _owner;

        public HotkeySink(HotkeyManager owner) => _owner = owner;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                _owner.OnHotkey(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }
}
