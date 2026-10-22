using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static BottleneckHunter.Core.Optimize.NativeMethods;

namespace BottleneckHunter.Core.Optimize;

/// <summary>Pega o ícone de um arquivo (o mesmo que o Explorer mostra) como imagem do WPF.</summary>
public static class FileIcon
{
    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var info = new SHFILEINFO();
        try
        {
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;
            var img = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon);
        }
    }
}
