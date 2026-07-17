using System;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace VoiSe.App;

internal sealed class TrayIconService : IDisposable
{
    private readonly Icon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIconService(string iconPath, Action openWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        _icon = new Icon(iconPath);
        _menu = new WinForms.ContextMenuStrip();

        var openItem = new WinForms.ToolStripMenuItem("Open VoiSee");
        openItem.Click += (_, _) => openWindow();
        _menu.Items.Add(openItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit VoiSee");
        exitItem.Click += (_, _) => exitApplication();
        _menu.Items.Add(exitItem);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "VoiSee",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left)
            {
                openWindow();
            }
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
