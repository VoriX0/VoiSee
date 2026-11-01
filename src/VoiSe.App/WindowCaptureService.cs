using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace VoiSe.App;

internal sealed class WindowCaptureService
{
    private const int DwmwaCloaked = 14;
    private const int PwRenderFullContent = 2;
    private const int Srccopy = 0x00CC0020;
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);

    public IReadOnlyList<CapturableWindowInfo> EnumerateCapturableWindows()
    {
        var currentProcessId = Environment.ProcessId;
        var windows = new List<CapturableWindowInfo>();

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsWindowCloaked(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || processId == currentProcessId)
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new CapturableWindowInfo
                {
                    Handle = handle,
                    ProcessId = (int)processId,
                    ProcessName = process.ProcessName,
                    ExecutablePath = TryGetExecutablePath(process),
                    WindowTitle = title.Trim()
                });
            }
            catch
            {
                // The process may have exited between EnumWindows and GetProcessById.
            }

            return true;
        }, IntPtr.Zero);

        return windows
            .GroupBy(window => window.Handle)
            .Select(group => group.First())
            .OrderBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.WindowTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool IsAvailable(CapturableWindowInfo? window)
    {
        return window is not null && IsWindow(window.Handle);
    }

    public async Task<byte[]?> CapturePreviewPngAsync(CapturableWindowInfo window, int maxWidth = 1280, int maxHeight = 720)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        CapturedPreview? preview;
        var previousDpiContext = SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        try { preview = CapturePreviewPixels(window, maxWidth, maxHeight); }
        finally
        {
            // Restore on this same thread, before the first await.
            if (previousDpiContext != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpiContext);
        }
        if (preview is null) return null;
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)preview.Width, (uint)preview.Height, 96, 96, preview.Pixels);
        await encoder.FlushAsync();
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var png = new byte[checked((int)stream.Size)];
        reader.ReadBytes(png);
        return png;
    }

    private sealed record CapturedPreview(byte[] Pixels, int Width, int Height);
    private static CapturedPreview? CapturePreviewPixels(CapturableWindowInfo window, int maxWidth, int maxHeight)
    {
        if (!IsWindow(window.Handle) || !GetWindowRect(window.Handle, out var rect)) return null;
        var sourceWidth = Math.Max(1, rect.Right - rect.Left);
        var sourceHeight = Math.Max(1, rect.Bottom - rect.Top);
        // Accept up to 8K, but never allocate an unbounded GDI surface for a malformed window rectangle.
        if ((long)sourceWidth * sourceHeight > 32 * 1024 * 1024) return null;
        var scale = Math.Min(1.0, Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var windowDc = GetWindowDC(window.Handle);
        if (windowDc == IntPtr.Zero) return null;
        IntPtr sourceDc = IntPtr.Zero, targetDc = IntPtr.Zero, sourceBitmap = IntPtr.Zero, targetBitmap = IntPtr.Zero;
        IntPtr previousSource = IntPtr.Zero, previousTarget = IntPtr.Zero;
        try
        {
            sourceDc = CreateCompatibleDC(windowDc); targetDc = CreateCompatibleDC(windowDc);
            sourceBitmap = CreateCompatibleBitmap(windowDc, sourceWidth, sourceHeight);
            targetBitmap = CreateCompatibleBitmap(windowDc, width, height);
            if (sourceDc == IntPtr.Zero || targetDc == IntPtr.Zero || sourceBitmap == IntPtr.Zero || targetBitmap == IntPtr.Zero) return null;
            previousSource = SelectObject(sourceDc, sourceBitmap);
            previousTarget = SelectObject(targetDc, targetBitmap);
            if (previousSource == IntPtr.Zero || previousSource == new IntPtr(-1) || previousTarget == IntPtr.Zero || previousTarget == new IntPtr(-1)) return null;
            if (!PrintWindow(window.Handle, sourceDc, PwRenderFullContent) &&
                !BitBlt(sourceDc, 0, 0, sourceWidth, sourceHeight, windowDc, 0, 0, Srccopy)) return null;
            SetStretchBltMode(targetDc, 4); // HALFTONE; resize before copying into managed memory.
            SetBrushOrgEx(targetDc, 0, 0, IntPtr.Zero);
            if (!StretchBlt(targetDc, 0, 0, width, height, sourceDc, 0, 0, sourceWidth, sourceHeight, Srccopy)) return null;
            SelectObject(targetDc, previousTarget); previousTarget = IntPtr.Zero;
            var pixels = new byte[checked(width * height * 4)];
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitsPerPixel = 32 };
            if (GetDIBits(targetDc, targetBitmap, 0, (uint)height, pixels, ref info, 0) != height) return null;
            return new CapturedPreview(pixels, width, height);
        }
        finally
        {
            if (previousSource != IntPtr.Zero && previousSource != new IntPtr(-1)) SelectObject(sourceDc, previousSource);
            if (previousTarget != IntPtr.Zero && previousTarget != new IntPtr(-1)) SelectObject(targetDc, previousTarget);
            if (sourceBitmap != IntPtr.Zero) DeleteObject(sourceBitmap);
            if (targetBitmap != IntPtr.Zero) DeleteObject(targetBitmap);
            if (sourceDc != IntPtr.Zero) DeleteDC(sourceDc);
            if (targetDc != IntPtr.Zero) DeleteDC(targetDc);
            ReleaseDC(window.Handle, windowDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitsPerPixel;
        public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")]
    private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowText(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private static bool IsWindowCloaked(IntPtr handle)
    {
        try
        {
            return DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr handle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr destination,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        int rasterOperation);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr handle, IntPtr destination, int flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
