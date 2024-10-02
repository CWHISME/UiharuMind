using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;
using Clowd.Clipboard;
using Rectangle = System.Drawing.Rectangle;

namespace UiharuMind.Utils.Clipboard;

public class ClipboardAvaloniaCustom : ClipboardStaticBase<ClipboardHandleAvalonia, Bitmap>
{
}

[SupportedOSPlatform("windows")]
public class ClipboardHandleAvalonia : ClipboardHandleGdiBase, IClipboardHandlePlatform<Bitmap>
{
    /// <inheritdoc/>
    public virtual Bitmap? GetImage()
    {
        using var gdi = GetImageImpl();

        if (gdi == null)
            return null;

        var bitmapData = gdi.LockBits(
            new Rectangle(0, 0, gdi.Width, gdi.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);

        var bmp = new Bitmap(
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul,
            bitmapData.Scan0,
            new Avalonia.PixelSize(bitmapData.Width, bitmapData.Height),
            new Avalonia.Vector(gdi.HorizontalResolution, gdi.VerticalResolution),
            bitmapData.Stride);

        gdi.UnlockBits(bitmapData);

        return bmp;
    }

    /// <inheritdoc/>
    public virtual void SetImage(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms);

        using var gdi = new System.Drawing.Bitmap(ms);

        SetImageImpl(gdi);
    }
}