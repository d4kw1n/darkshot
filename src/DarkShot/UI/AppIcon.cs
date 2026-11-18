using System.Drawing;
using System.Reflection;

namespace DarkShot.UI;

public static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon != null) return _icon;

        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("DarkShot.darkshot.ico");
            if (stream != null)
                _icon = new Icon(stream);
        }
        catch { /* fall through */ }

        if (_icon == null)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "darkshot.ico");
                if (File.Exists(path)) _icon = new Icon(path);
            }
            catch { /* ignore */ }
        }

        _icon ??= SystemIcons.Application;
        return _icon;
    }
}
