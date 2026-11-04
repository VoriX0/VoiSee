using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiSe.App;

// Uses the existing WinUI HWND and its UI message loop. No WinForms window,
// Windows Desktop runtime, extra thread or permanent polling timer is needed.
internal sealed class TrayIconService : IDisposable
{
    private const uint CallbackMessage = 0x8071;
    private const uint IconId = 1;
    private static readonly UIntPtr SubclassId = new(0x564f4953);
    private readonly IntPtr _window;
    private readonly SubclassProc _callback;
    private readonly Action _openWindow, _exitApplication;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _added, _subclassInstalled, _disposed;
    internal bool IsAvailable => _added && !_disposed;
    public event Action<bool>? PowerModeChanged;

    public TrayIconService(IntPtr window, string iconPath, Action openWindow, Action exitApplication)
    {
        if (window == IntPtr.Zero) throw new ArgumentException("A window handle is required.", nameof(window));
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);
        _window = window; _openWindow = openWindow; _exitApplication = exitApplication;
        _callback = WindowProcedure;
        try
        {
            // LR_LOADFROMFILE | LR_DEFAULTSIZE; this owned icon must be destroyed.
            _icon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x10 | 0x40);
            if (_icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            if (_taskbarCreated == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!SetWindowSubclass(_window, _callback, SubclassId, UIntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _subclassInstalled = true;
            AddIcon();
        }
        catch { Dispose(); throw; }
    }

    private NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = IconId,
        Flags = 1 | 2 | 4 | 0x80, Callback = CallbackMessage, Icon = _icon,
        Tip = "VoiSee", Info = "", InfoTitle = "", Version = 4
    };
    private void AddIcon()
    {
        var data = IconData();
        if (!ShellNotifyIcon(0, ref data)) throw new InvalidOperationException("Notification area icon could not be added.");
        _added = true;
        if (!ShellNotifyIcon(4, ref data))
        {
            ShellNotifyIcon(2, ref data); _added = false;
            throw new InvalidOperationException("Notification area icon version could not be set.");
        }
    }
    private IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        try
        {
            if (!_disposed && message == 0x218 && (wParam.ToUInt64() is 4 or 18)) // WM_POWERBROADCAST
            {
                PowerModeChanged?.Invoke(wParam.ToUInt64() == 4);
                return new IntPtr(1);
            }
            if (message == 0x82) Dispose(); // WM_NCDESTROY: never leave a managed callback on a destroyed HWND.
            else if (!_disposed && message == _taskbarCreated)
            {
                _added = false; // Explorer restarted; the previous icon no longer exists.
                AddIcon();
            }
            else if (!_disposed && message == CallbackMessage && ((lParam.ToInt64() >> 16) & 0xffff) == IconId)
            {
                var notification = (uint)(lParam.ToInt64() & 0xffff);
                if (notification is 0x400 or 0x401) _openWindow(); // NIN_SELECT / NIN_KEYSELECT
                else if (notification == 0x7b) ShowMenu(wParam); // WM_CONTEXTMENU, including keyboard activation.
                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write("Notification area callback error: " + ex);
            // If Explorer could not restore the icon, leave the application accessible.
            if (!_added && !_disposed) { try { _openWindow(); } catch { } }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private void ShowMenu(UIntPtr position)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!AppendMenu(menu, 0, new UIntPtr(1), UiText.L("Open_VoiSee_a0579752")) ||
                !AppendMenu(menu, 0x800, UIntPtr.Zero, null) ||
                !AppendMenu(menu, 0, new UIntPtr(2), UiText.L("Exit_VoiSee_523d0efa")))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var packed = position.ToUInt64();
            var x = (short)(packed & 0xffff); var y = (short)((packed >> 16) & 0xffff);
            var point = new NativePoint { X = x, Y = y };
            if (x == -1 && y == -1) GetCursorPos(out point);
            SetForegroundWindow(_window);
            // Return the command instead of dispatching WM_COMMAND into WinUI.
            var command = TrackPopupMenuEx(menu, 0x100 | 0x80 | 2, point.X, point.Y, _window, IntPtr.Zero);
            PostMessage(_window, 0, UIntPtr.Zero, IntPtr.Zero); // Allows an outside click to dismiss subsequent menus.
            if (command == 1) _openWindow();
            else if (command == 2) _exitApplication();
            else { var data = IconData(); ShellNotifyIcon(3, ref data); } // Restore taskbar focus on cancellation.
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PowerModeChanged = null;
        if (_added) { var data = IconData(); ShellNotifyIcon(2, ref data); _added = false; }
        if (_subclassInstalled) { RemoveWindowSubclass(_window, _callback, SubclassId); _subclassInstalled = false; }
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
}
