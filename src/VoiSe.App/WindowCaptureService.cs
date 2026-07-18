using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiSe.App;

internal sealed class WindowCaptureService
{
    private const int DwmwaCloaked = 14;
    private const int PwRenderFullContent = 2;
    private const int Srccopy = 0x00CC0020;

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

    public byte[]? CapturePreviewPng(CapturableWindowInfo window, int maxWidth = 1280, int maxHeight = 720)
    {
        if (!IsWindow(window.Handle) || !GetWindowRect(window.Handle, out var rect))
        {
            return null;
        }

        var sourceWidth = Math.Max(1, rect.Right - rect.Left);
        var sourceHeight = Math.Max(1, rect.Bottom - rect.Top);
        var windowDc = GetWindowDC(window.Handle);
        if (windowDc == IntPtr.Zero)
        {
            return null;
        }

        var memoryDc = CreateCompatibleDC(windowDc);
        var bitmapHandle = CreateCompatibleBitmap(windowDc, sourceWidth, sourceHeight);
        if (memoryDc == IntPtr.Zero || bitmapHandle == IntPtr.Zero)
        {
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(window.Handle, windowDc);
            return null;
        }

        var previousObject = SelectObject(memoryDc, bitmapHandle);
        try
        {
            var captured = PrintWindow(window.Handle, memoryDc, PwRenderFullContent);
            if (!captured)
            {
                captured = BitBlt(memoryDc, 0, 0, sourceWidth, sourceHeight, windowDc, 0, 0, Srccopy);
            }

            if (!captured)
            {
                return null;
            }

            using var sourceBitmap = Image.FromHbitmap(bitmapHandle);
            var scale = Math.Min(1.0, Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight));
            var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            using var previewBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(previewBitmap))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(sourceBitmap, 0, 0, width, height);
            }

            using var stream = new MemoryStream();
            previewBitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            SelectObject(memoryDc, previousObject);
            DeleteObject(bitmapHandle);
            DeleteDC(memoryDc);
            ReleaseDC(window.Handle, windowDc);
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
