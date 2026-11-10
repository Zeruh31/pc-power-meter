using System.Windows;
using PcPowerMeter.Localization;
using Forms = System.Windows.Forms;

namespace PcPowerMeter.Services;

/// <summary>Notification area icon with a live watt tooltip and a small menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int MaxTooltip = 127; // NotifyIcon.Text limit on .NET 6+

    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _show = new();
    private readonly Forms.ToolStripMenuItem _reset = new();
    private readonly Forms.ToolStripMenuItem _exit = new();
    private bool _balloonShown;
    private int _disposed;

    public TrayIcon(Action show, Action reset, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        _show.Click += (_, _) => show();
        _reset.Click += (_, _) => reset();
        _exit.Click += (_, _) => exit();
        menu.Items.Add(_show);
        menu.Items.Add(_reset);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_exit);

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Text = Loc.T("AppTitle"),
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                show();
            }
        };
        UpdateTexts();
    }

    /// <summary>Re-reads the menu texts after a language change.</summary>
    public void UpdateTexts()
    {
        _show.Text = Loc.T("TrayShow");
        _reset.Text = Loc.T("Reset");
        _exit.Text = Loc.T("TrayExit");
    }

    public void SetTooltip(string text)
    {
        if (_disposed != 0)
        {
            return;
        }
        string full = Loc.T("AppTitle") + "\n" + text;
        _icon.Text = full.Length > MaxTooltip ? full[..MaxTooltip] : full;
    }

    /// <summary>Tells the user once per run that the app is still running.</summary>
    public void ShowStillRunningOnce()
    {
        if (_balloonShown)
        {
            return;
        }
        _balloonShown = true;
        _icon.ShowBalloonTip(3000, Loc.T("AppTitle"), Loc.T("TrayStillRunning"), Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using Stream stream = info!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
