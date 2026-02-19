using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Atsumare;

public static class IconUtil
{
    public static WriteableBitmap? TryGetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        // まずは Jumbo/ExtraLarge を試す
        IntPtr hIcon = TryGetHiconFromSystemImageList(exePath, ImageListSize.Jumbo);

        if (hIcon == IntPtr.Zero)
            hIcon = TryGetHiconFromSystemImageList(exePath, ImageListSize.ExtraLarge);

        if (hIcon == IntPtr.Zero)
            hIcon = GetHiconFromShGetFileInfo(exePath);


        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            return HiconToWriteableBitmap(hIcon);
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    // ---- High-res via system image list ----

    private enum ImageListSize : int
    {
        Large = 0,       // 32
        Small = 1,       // 16
        ExtraLarge = 2,  // 48
        SysSmall = 3,    // 16 (system)
        Jumbo = 4        // 256
    }

    private static IntPtr TryGetHiconFromSystemImageList(string path, ImageListSize size)
    {
        int iIcon = GetSystemIconIndex(path);
        if (iIcon < 0) return IntPtr.Zero;

        var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
        int hr = SHGetImageList((int)size, ref iid, out var iml);
        if (hr != 0 || iml == null) return IntPtr.Zero;

        IntPtr hIcon = IntPtr.Zero;
        const int ILD_TRANSPARENT = 0x00000001;
        hr = iml.GetIcon(iIcon, ILD_TRANSPARENT, ref hIcon);
        Marshal.ReleaseComObject(iml);
        return hr == 0 ? hIcon : IntPtr.Zero;
    }

    private static int GetSystemIconIndex(string path)
    {
        SHFILEINFO sfi;
        IntPtr r = SHGetFileInfo(path, 0, out sfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_SYSICONINDEX);
        if (r == IntPtr.Zero) return -1;
        return sfi.iIcon;
    }

    // Fallback: old large icon (often 32px)
    private static IntPtr GetHiconFromShGetFileInfo(string path)
    {
        SHFILEINFO sfi;
        IntPtr r = SHGetFileInfo(path, 0, out sfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON);
        return r == IntPtr.Zero ? IntPtr.Zero : sfi.hIcon;
    }

    // ---- HICON -> WriteableBitmap (same as before) ----

    private static WriteableBitmap HiconToWriteableBitmap(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out ICONINFO iconInfo))
            throw new InvalidOperationException("GetIconInfo failed.");

        if (iconInfo.hbmColor == IntPtr.Zero)
        {
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            throw new NotSupportedException("Icon has no color bitmap.");
        }

        try
        {
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bmp) == 0)
                throw new InvalidOperationException("GetObject failed.");

            int width = bmp.bmWidth;
            int height = Math.Abs(bmp.bmHeight);

            var bi = new BITMAPINFO();
            bi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bi.bmiHeader.biWidth = width;
            bi.bmiHeader.biHeight = -height; // top-down
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            bi.bmiHeader.biCompression = BI_RGB;

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            IntPtr hdc = GetDC(IntPtr.Zero);
            IntPtr memdc = CreateCompatibleDC(hdc);

            try
            {
                int lines = GetDIBits(memdc, iconInfo.hbmColor, 0, (uint)height, pixels, ref bi, DIB_RGB_COLORS);
                if (lines == 0)
                    throw new InvalidOperationException("GetDIBits failed.");
            }
            finally
            {
                DeleteDC(memdc);
                ReleaseDC(IntPtr.Zero, hdc);
            }

            var wb = new WriteableBitmap(width, height);
            using (var stream = wb.PixelBuffer.AsStream())
            {
                stream.Write(pixels, 0, pixels.Length);
            }
            return wb;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }

    // ---- Win32/COM ----

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        int SetOverlayImage(int iImage, int iOverlay);
        int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        int Draw(ref IMAGELISTDRAWPARAMS pimldp);
        int Remove(int i);
        int GetIcon(int i, int flags, ref IntPtr picon);
        // 以降は未使用なので省略してもOKだが、順序が重要なのでダミーで埋める
        int GetImageInfo(int i, out IntPtr pImageInfo);
        int Copy(int iDst, IImageList punkSrc, int iSrc, int uFlags);
        int Merge(int i1, IImageList punk2, int i2, int dx, int dy, ref Guid riid, out IntPtr ppv);
        int Clone(ref Guid riid, out IntPtr ppv);
        int GetImageRect(int i, out IntPtr prc);
        int GetIconSize(out int cx, out int cy);
        int SetIconSize(int cx, int cy);
        int GetImageCount(out int pi);
        int SetImageCount(int uNewCount);
        int SetBkColor(int clrBk, out int pclr);
        int GetBkColor(out int pclr);
        int BeginDrag(int iTrack, int dxHotspot, int dyHotspot);
        int EndDrag();
        int DragEnter(IntPtr hwndLock, int x, int y);
        int DragLeave(IntPtr hwndLock);
        int DragMove(int x, int y);
        int SetDragCursorImage(ref IImageList punk, int iDrag, int dxHotspot, int dyHotspot);
        int DragShowNolock(int fShow);
        int GetDragImage(out IntPtr ppt, out IntPtr pptHotspot, ref Guid riid, out IntPtr ppv);
        int GetItemFlags(int i, out int dwFlags);
        int GetOverlayImage(int iOverlay, out int piIndex);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGELISTDRAWPARAMS
    {
        public int cbSize;
        public IntPtr himl;
        public int i;
        public IntPtr hdcDst;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public int xBitmap;
        public int yBitmap;
        public int rgbBk;
        public int rgbFg;
        public int fStyle;
        public int dwRop;
        public int fState;
        public int Frame;
        public int crEffect;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr h, int c, out BITMAP pv);

    private const int DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors; // unused
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbmp,
        uint uStartScan,
        uint cScanLines,
        [Out] byte[] lpvBits,
        ref BITMAPINFO lpbi,
        uint uUsage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
