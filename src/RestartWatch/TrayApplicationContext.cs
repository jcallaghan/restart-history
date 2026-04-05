using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using RestartWatch.Models;
using RestartWatch.Services;
using RestartWatch.Views;

namespace RestartWatch;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _uptimeTimer;
    private readonly CopilotInsightsService _copilot = new();
    private HistoryPopup? _popup;
    private List<RestartEvent> _history = new();
    private DateTime _lastBootTime;
    private RestartEvent? _lastReboot;
    private bool _aiInsightsEnabled;
    private ToolStripMenuItem? _aiMenuItem;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RestartWatch", "settings.json");

    public TrayApplicationContext()
    {
        LoadSettings();
        _lastBootTime = BootInfoProvider.GetLastBootTime();
        RefreshData();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        UpdateIcon();
        UpdateTooltip();

        _notifyIcon.MouseClick += OnTrayClick;

        _uptimeTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _uptimeTimer.Tick += (_, _) => UpdateTooltip();
        _uptimeTimer.Start();

        ShowBootToast();

        // Initialize Copilot SDK in background
        _ = Task.Run(async () =>
        {
            try
            {
                var available = await _copilot.TryInitializeAsync();
                if (available && _aiMenuItem != null)
                {
                    // Show the menu item on the UI thread
                    _aiMenuItem.Visible = true;
                    // If AI was already enabled from settings, run analysis
                    if (_aiInsightsEnabled) RunAutoAnalysis();
                }
            }
            catch { /* Copilot is optional */ }
        });
    }

    private void RefreshData()
    {
        _lastBootTime = BootInfoProvider.GetLastBootTime();
        _history = RestartClassifier.GetRestartHistory(30);
        _lastReboot = _history.Count > 0 ? _history[0] : null;
    }

    private void UpdateIcon()
    {
        var severity = _lastReboot?.Severity ?? RestartSeverity.Green;
        _notifyIcon.Icon = severity switch
        {
            RestartSeverity.Red => GenerateIcon(Color.FromArgb(244, 67, 54)),
            RestartSeverity.Yellow => GenerateIcon(Color.FromArgb(255, 193, 7)),
            _ => GenerateIcon(Color.FromArgb(76, 175, 80))
        };
    }

    private void UpdateTooltip()
    {
        var uptime = DateTime.Now - _lastBootTime;
        var uptimeStr = BootInfoProvider.FormatUptime(uptime);
        var cause = _lastReboot?.CauseDescription ?? "Unknown";
        // Use system regional date/time format
        var bootStr = _lastBootTime.ToString("g"); // short date + short time per regional settings

        var tooltip = $"Last restart: {bootStr}\nUptime: {uptimeStr}\nCause: {cause}";
        if (tooltip.Length > 127)
            tooltip = tooltip[..127];

        _notifyIcon.Text = tooltip;
    }

    private void ShowBootToast()
    {
        try
        {
            var uptime = DateTime.Now - _lastBootTime;
            var cause = _lastReboot?.CauseDescription ?? "Unknown";
            var bootStr = _lastBootTime.ToString("g");
            var uptimeStr = BootInfoProvider.FormatUptime(uptime);

            // Format relative time
            string relative;
            if (uptime.TotalDays >= 1)
                relative = $"{(int)uptime.TotalDays}d {uptime.Hours}h ago";
            else if (uptime.TotalHours >= 1)
                relative = $"{(int)uptime.TotalHours}h {uptime.Minutes}m ago";
            else
                relative = $"{uptime.Minutes}m ago";

            ToastNotificationManagerCompat.History.Clear();

            new ToastContentBuilder()
                .AddArgument("action", "viewRestart")
                .AddText($"Last restart: {cause}")
                .AddText($"Booted: {bootStr} ({relative})")
                .Show();
        }
        catch
        {
            // Toast may fail in some environments — not critical
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Refresh", null, (_, _) =>
        {
            RefreshData();
            UpdateIcon();
            UpdateTooltip();
            if (_aiInsightsEnabled) RunAutoAnalysis();
        });

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupManager.IsStartupEnabled(),
            CheckOnClick = true
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            StartupManager.SetStartupEnabled(startupItem.Checked);
        };
        menu.Items.Add(startupItem);

        // AI Insights toggle — hidden until Copilot SDK is detected
        _aiMenuItem = new ToolStripMenuItem("AI Insights (Copilot)")
        {
            Visible = false, // shown once Copilot is confirmed available
            CheckOnClick = true,
            Checked = _aiInsightsEnabled
        };
        _aiMenuItem.CheckedChanged += (_, _) =>
        {
            _aiInsightsEnabled = _aiMenuItem.Checked;
            SaveSettings();
            if (_aiInsightsEnabled)
                RunAutoAnalysis();
            else
                _copilot.ClearCache();
        };
        menu.Items.Add(_aiMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, (_, _) =>
        {
            _notifyIcon.Visible = false;
            _uptimeTimer.Stop();
            System.Windows.Application.Current?.Shutdown();
            Application.Exit();
        });

        return menu;
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_popup != null && _popup.IsVisible)
        {
            _popup.Hide();
            return;
        }

        RefreshData();
        UpdateIcon();
        UpdateTooltip();

        // Always create a fresh popup with current data
        _popup?.Close();
        var cursorPos = Cursor.Position;
        _popup = new HistoryPopup(_lastBootTime, _history, cursorPos,
            _aiInsightsEnabled ? _copilot : null);
        _popup.Show();
    }

    private void RunAutoAnalysis()
    {
        if (!_copilot.IsAvailable || _copilot.IsAnalyzing) return;
        _ = Task.Run(async () =>
        {
            try { await _copilot.AnalyzeAndCacheAsync(_history); }
            catch { /* Non-critical */ }
        });
    }

    /// <summary>
    /// Generates a 16x16 tray icon: colored circle with a reboot arrow symbol.
    /// </summary>
    private static Icon GenerateIcon(Color accentColor)
    {
        // Render at 32x32 for crisp display on high-DPI taskbars
        int sz = 32;
        using var bmp = new Bitmap(sz, sz);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        // Filled circle
        using (var bgBrush = new SolidBrush(accentColor))
        {
            g.FillEllipse(bgBrush, 1, 1, sz - 2, sz - 2);
        }

        // Circular arrow — scaled for 32px
        float penW = 2.8f;
        using var pen = new Pen(Color.White, penW) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float arcInset = sz * 0.25f;
        float arcSize = sz * 0.5f;
        g.DrawArc(pen, arcInset, arcInset, arcSize, arcSize, -60, 270);

        // Arrowhead
        var tip = new PointF(sz * 0.66f, sz * 0.27f);
        g.DrawLine(pen, tip, new PointF(tip.X, tip.Y + 4.5f));
        g.DrawLine(pen, tip, new PointF(tip.X - 4f, tip.Y + 1.5f));

        return Icon.FromHandle(bmp.GetHicon());
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                _aiInsightsEnabled = settings?.AiInsightsEnabled ?? false;
            }
        }
        catch { /* Settings file corrupt or inaccessible — use defaults */ }
    }

    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var settings = new AppSettings { AiInsightsEnabled = _aiInsightsEnabled };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* Non-critical */ }
    }

    private class AppSettings
    {
        public bool AiInsightsEnabled { get; set; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uptimeTimer.Stop();
            _uptimeTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
