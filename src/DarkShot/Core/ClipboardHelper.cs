using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DarkShot.Core;

public static class ClipboardHelper
{
    public static DataObject CreateImageDataObject(Bitmap bitmap)
    {
        var data = new DataObject();
        data.SetImage(bitmap);
        try
        {
            var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            data.SetData("PNG", false, ms);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not add PNG flavour to clipboard: " + ex.Message);
        }
        return data;
    }

    public static bool CopyImage(Bitmap bitmap)
    {
        if (bitmap == null) return false;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var data = CreateImageDataObject(bitmap);
                Clipboard.SetDataObject(data, copy: true, retryTimes: 8, retryDelay: 40);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"Clipboard copy failed (attempt {attempt + 1}): {ex.Message}");
                Thread.Sleep(60);
            }
        }

        return false;
    }

    public static Bitmap? TryGetImage()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsImage()) return null;
                using var img = Clipboard.GetImage();
                if (img == null) return null;
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                }
                bmp.SetResolution(96f, 96f);
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Warn($"Clipboard read failed (attempt {attempt + 1}): {ex.Message}");
                Thread.Sleep(50);
            }
        }
        return null;
    }

    public static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard text copy failed: " + ex.Message);
            return false;
        }
    }
}
