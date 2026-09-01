using System.Drawing;
using System.Diagnostics;
using System.Windows;

namespace ScreenDemoRecorder;

public partial class MainWindow
{
    private System.Windows.Forms.NotifyIcon? trayIcon;
    private Icon? trayIconImage;
    private bool trayNoticeShown;
    private bool exitRequested;
    private bool minimizingToTray;
    private string? trayNotificationPath;

    private void InitializeTray()
    {
        trayIconImage = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ??
            (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = trayIconImage,
            Text = "Screen Demo Recorder",
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Screen Demo Recorder", null, (_, _) => Dispatcher.InvokeAsync(ShowFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.InvokeAsync(ExitFromTray));
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => Dispatcher.InvokeAsync(ShowFromTray);
        trayIcon.BalloonTipClicked += (_, _) => Dispatcher.InvokeAsync(OpenTrayNotification);
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        if (trayIcon is not null) trayIcon.Visible = profile.Application.MinimizeToTray;
    }

    private async Task<bool> TryCloseToTrayAsync()
    {
        if (PreviewMode || exitRequested || !profile.Application.MinimizeToTray || trayIcon is null) return false;
        if (minimizingToTray) return true;
        minimizingToTray = true;
        try
        {
            await SaveNowAsync();
            trayIcon.Visible = true;
            Hide();
            if (!trayNoticeShown)
            {
                trayNoticeShown = true;
                trayIcon.ShowBalloonTip(2500, "Screen Demo Recorder", "The recorder is still running in the notification area.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            return true;
        }
        finally
        {
            minimizingToTray = false;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void NotifyRecordingSaved(string path)
    {
        if (trayIcon is not { Visible: true }) return;
        trayNotificationPath = path;
        trayIcon.ShowBalloonTip(4000, "Screen Demo Recorder", $"Saved {Path.GetFileName(path)}",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void OpenTrayNotification()
    {
        if (trayNotificationPath is { } path && File.Exists(path))
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return; }
            catch { }
        }
        ShowFromTray();
    }

    private void ExitFromTray()
    {
        exitRequested = true;
        if (trayIcon is not null) trayIcon.Visible = false;
        ShowFromTray();
        Close();
    }

    private void DisposeTray()
    {
        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();
            trayIcon = null;
        }
        trayIconImage?.Dispose();
        trayIconImage = null;
        trayNotificationPath = null;
    }

    internal bool CheckTrayLifecycleForSmoke()
    {
        if (!PreviewMode || trayIcon is not null) return false;
        InitializeTray();
        var valid = trayIcon is { Visible: false, ContextMenuStrip.Items.Count: 3 };
        DisposeTray();
        return valid;
    }
}
