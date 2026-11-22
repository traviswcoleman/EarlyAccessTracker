using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Sync;

namespace EarlyAccessTracker.App;

/// <summary>
/// Owns the tray icon. Left-click (or the toast) opens the dashboard in the default browser.
/// Windows 10/11 render NotifyIcon balloon tips as standard toast notifications.
/// </summary>
public sealed class TrayApplicationContext(
    SyncCoordinator coordinator,
    IConfiguration configuration,
    ILogger<TrayApplicationContext> logger) : ApplicationContext
{
    private NotifyIcon? _icon;
    private Icon? _iconImage;
    private Control? _invoker;
    private RegisteredWaitHandle? _showWait;
    private ToolStripMenuItem? _syncItem;
    private string _balloonTarget = "";

    private string DashboardUrl => $"http://127.0.0.1:{configuration.GetValue("Dashboard:Port", 5179)}/";

    public void Initialize(EventWaitHandle showDashboardSignal, bool openDashboardOnStart)
    {
        // A handle-backed control gives us a reliable way to marshal onto the UI thread.
        _invoker = new Control();
        _invoker.CreateControl();

        _syncItem = new ToolStripMenuItem("Sync now", null, async (_, _) => await SyncNowAsync());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenDashboard()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_syncItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _iconImage = CreateTrayIcon();
        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Text = "Early Access Tracker",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenDashboard(); };
        _icon.BalloonTipClicked += (_, _) => OpenDashboard(_balloonTarget);

        coordinator.Changed += () => OnUi(UpdateSyncMenuState);
        coordinator.SyncFinished += OnSyncFinished;

        _showWait = ThreadPool.RegisterWaitForSingleObject(showDashboardSignal,
            (_, _) => OnUi(() => OpenDashboard()), null, Timeout.Infinite, executeOnlyOnce: false);

        if (openDashboardOnStart) OpenDashboard();
    }

    private void OnSyncFinished(SyncRun run) => OnUi(() =>
    {
        if (_icon is null) return;
        _icon.Text = Truncate($"Early Access Tracker — last sync {run.FinishedAt?.ToLocalTime():g} ({run.Outcome})");

        if (run.Outcome == SyncOutcome.Failed)
        {
            ShowToast("Early Access Tracker", "Steam sync failed. Open the dashboard for details.", ToolTipIcon.Warning, "syncs");
        }
        else if (run.NewAlertCount > 0)
        {
            var text = run.NewAlertCount == 1
                ? "1 game has come out of early access"
                : $"{run.NewAlertCount} games have come out of early access";
            ShowToast("Early Access Tracker", text, ToolTipIcon.Info, "?filter=toplay");
        }
    });

    private async Task SyncNowAsync()
    {
        // Run off the UI thread so DB/HTTP continuations don't land on the tray's message loop.
        var (_, refusal) = await Task.Run(() => coordinator.RunManualAsync(CancellationToken.None));
        if (refusal is not null)
            ShowToast("Sync not started", refusal, ToolTipIcon.Info, "");
    }

    private void UpdateSyncMenuState()
    {
        if (_syncItem is null) return;
        _syncItem.Enabled = !coordinator.IsRunning;
        _syncItem.Text = coordinator.IsRunning ? "Sync running…" : "Sync now";
    }

    private void ShowToast(string title, string text, ToolTipIcon icon, string target)
    {
        if (_icon is null) return;
        _balloonTarget = target;
        _icon.ShowBalloonTip(10_000, title, text, icon);
    }

    private void OpenDashboard(string relative = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(DashboardUrl + relative) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open the dashboard");
        }
    }

    private void OnUi(Action action)
    {
        if (_invoker is null || _invoker.IsDisposed) return;
        try
        {
            if (_invoker.InvokeRequired) _invoker.BeginInvoke(action);
            else action();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { } // handle gone during shutdown
    }

    private static string Truncate(string s) => s.Length <= 127 ? s : s[..127];

    protected override void ExitThreadCore()
    {
        if (_icon is not null) _icon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showWait?.Unregister(null);
            _icon?.Dispose();
            if (_iconImage is not null) { DestroyIcon(_iconImage.Handle); _iconImage.Dispose(); }
            _invoker?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Draws a simple "EA" badge so the app ships without binary assets.</summary>
    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var path = new GraphicsPath();
            const int r = 8;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(31 - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(31 - r * 2, 31 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, 31 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(0x1B, 0x6E, 0xC2));
            g.FillPath(fill, path);
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("EA", font, Brushes.White, new RectangleF(0, 1, 32, 32), fmt);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
