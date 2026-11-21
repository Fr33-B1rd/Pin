using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PinApp;

/// <summary>A top-down 32-bit DIB shared by GDI+ drawing and UpdateLayeredWindow.</summary>
internal sealed class HighlightBitmap : SafeHandleZeroOrMinusOneIsInvalid
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    private readonly IntPtr _bits;
    public Size Size { get; }
    public long Bytes => (long)Size.Width * Size.Height * 4;
    public IntPtr Handle => handle;

    public HighlightBitmap(Size size) : base(true)
    {
        Size = size;
        var info = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = size.Width, Height = -size.Height,
            Planes = 1, BitCount = 32
        };
        SetHandle(CreateDIBSection(IntPtr.Zero, ref info, 0, out _bits, IntPtr.Zero, 0));
        if (IsInvalid) throw new Win32Exception();
    }

    public Bitmap OpenCanvas() => new(Size.Width, Size.Height, checked(Size.Width * 4),
        PixelFormat.Format32bppPArgb, _bits);

    protected override bool ReleaseHandle() => Native.DeleteObject(handle);
}
