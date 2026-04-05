using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using RestartHistory.Models;
using RestartHistory.Services;
using WinForms = System.Windows.Forms;

namespace RestartHistory.Views;

public partial class HistoryPopup : Window
{
    private static readonly Dictionary<RestartCause, System.Windows.Media.Color> CauseColors = new()
    {
        [RestartCause.WindowsUpdate] = System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4),
        [RestartCause.UserShutdown] = System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50),
        [RestartCause.SoftwareInstall] = System.Windows.Media.Color.FromRgb(0x9C, 0x27, 0xB0),
        [RestartCause.NormalBoot] = System.Windows.Media.Color.FromRgb(0x60, 0x7D, 0x8B),
        [RestartCause.UnexpectedShutdown] = System.Windows.Media.Color.FromRgb(0xFF, 0xC1, 0x07),
        [RestartCause.PowerLoss] = System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00),
        [RestartCause.Bsod] = System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36),
        [RestartCause.Unknown] = System.Windows.Media.Color.FromRgb(0x60, 0x7D, 0x8B),
    };

    private readonly List<RestartEvent> _history;
    private readonly CopilotInsightsService? _copilot;
    private int _periodDays = 14;
    private DispatcherTimer? _loadingTimer;
    private System.Drawing.Point? _anchor;

    public HistoryPopup(DateTime lastBootTime, List<RestartEvent> history,
        System.Drawing.Point? anchor = null, CopilotInsightsService? copilot = null)
    {
        _history = history;
        _copilot = copilot;
        _anchor = anchor;
        InitializeComponent();
        ApplyTheme();

        // Ensure main page visible
        MainPage.Visibility = Visibility.Visible;
        SummaryPage.Visibility = Visibility.Collapsed;

        var uptime = DateTime.Now - lastBootTime;
        var lastReboot = history.Count > 0 ? history[0] : null;
        var severity = lastReboot?.Severity ?? RestartSeverity.Green;

        // Stat tiles
        StatBootTime.Text = lastBootTime.ToString("HH:mm");
        StatCause.Text = lastReboot?.CauseShortLabel ?? "—";
        CauseTile.ToolTip = lastReboot?.CauseLabel ?? "Unknown";
        StatCauseIcon.Text = lastReboot?.FluentIcon ?? "\uE73E";
        StatCauseIcon.Foreground = GetSeverityBrush(severity);
        StatUptime.Text = BootInfoProvider.FormatUptime(uptime);

        // Show Copilot summary state (generating or cached)
        if (copilot != null)
        {
            SummaryPreviewCard.Visibility = Visibility.Visible;
            if (copilot.IsAnalyzing)
            {
                StartLoadingAnimation();
                ShowMoreButton.Visibility = Visibility.Collapsed;
                SummaryScrollViewer.Visibility = Visibility.Collapsed;
            }
            else if (copilot.CachedShortSummary != null)
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SummaryScrollViewer.Visibility = Visibility.Visible;
                SummaryPreviewText.Text = copilot.CachedShortSummary;
                SummaryFullText.Text = FormatSummaryText(copilot.CachedDetailedSummary ?? "");
                ShowMoreButton.Visibility = Visibility.Visible;
            }
            else
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                SummaryScrollViewer.Visibility = Visibility.Visible;
                SummaryPreviewText.Text = "Click Refresh in tray menu to generate insights.";
                SummaryFullText.Text = "Analysis has not run yet. Use Refresh to generate.";
                ShowMoreButton.Visibility = Visibility.Collapsed;
            }
            copilot.InsightsUpdated += OnInsightsUpdated;
        }

        // Apply period (populates activity, distribution, history, reboots count)
        ApplyPeriod(_periodDays);

        // Position after layout, then lock the size
        var capturedAnchor = anchor;
        Loaded += (_, _) =>
        {
            UpdateLayout();
            if (capturedAnchor.HasValue)
                PositionNearTray(capturedAnchor.Value);
            else
                PositionNearTaskbar();
            
            // Lock the window size/position to prevent shifting
            SizeToContent = SizeToContent.Manual;
            MinHeight = ActualHeight;
            Height = ActualHeight;

            // Reposition when display settings change (monitor resize, dock/undock)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        };

        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        };
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_anchor.HasValue)
                PositionNearTray(_anchor.Value);
            else
                PositionNearTaskbar();
        });
    }

    private void StartLoadingAnimation()
    {
        LoadingIndicator.Visibility = Visibility.Visible;
        SummaryScrollViewer.Visibility = Visibility.Collapsed;
        LoadingText.Text = "Analyzing restart history...";
        SummaryFullText.Text = "Generating insights...";
        
        // Animate loading bar
        double barWidth = 0;
        double direction = 1;
        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _loadingTimer.Tick += (_, _) =>
        {
            barWidth += direction * 3;
            if (barWidth >= 200) direction = -1;
            else if (barWidth <= 0) direction = 1;
            LoadingBar.Width = Math.Max(20, barWidth);
            LoadingBar.Margin = new Thickness(direction > 0 ? 0 : 200 - barWidth, 0, 0, 0);
        };
        _loadingTimer.Start();
    }

    private void StopLoadingAnimation()
    {
        _loadingTimer?.Stop();
        _loadingTimer = null;
        LoadingIndicator.Visibility = Visibility.Collapsed;
        SummaryScrollViewer.Visibility = Visibility.Visible;
    }

    private void OnInsightsUpdated(string shortSummary, string detailedSummary)
    {
        Dispatcher.Invoke(() =>
        {
            StopLoadingAnimation();
            SummaryPreviewCard.Visibility = Visibility.Visible;
            ShowMoreButton.Visibility = Visibility.Visible;
            SummaryPreviewText.Text = shortSummary;
            SummaryFullText.Text = FormatSummaryText(detailedSummary);
        });
    }

    // Convert bullet points to paragraph text for cleaner display
    private static string FormatSummaryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Remove bullet markers and convert to paragraphs
        var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var paragraphs = new List<string>();
        foreach (var line in lines)
        {
            var clean = line.TrimStart(' ', '\t', '-', '•', '*', '·');
            if (!string.IsNullOrWhiteSpace(clean))
                paragraphs.Add(clean.Trim());
        }
        return string.Join("\n\n", paragraphs);
    }

    // ── Navigation ──
    private void ShowSummaryPage_Click(object sender, RoutedEventArgs e)
    {
        MainPage.Visibility = Visibility.Collapsed;
        SummaryPage.Visibility = Visibility.Visible;
        SummaryPageHeading.Text = "Detailed Summary";
        SummaryPageIcon.Text = "\uE946";
        SummaryPageIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        if (_copilot?.CachedDetailedSummary != null)
            SummaryFullText.Text = FormatSummaryText(_copilot.CachedDetailedSummary);
    }

    private void BackToMain_Click(object sender, RoutedEventArgs e)
    {
        SummaryPage.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Visible;
    }

    // ── History item click (explain non-green events) ──
    private DispatcherTimer? _summaryPageLoadingTimer;

    private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not RestartEvent reboot)
            return;

        // Only explain non-green (concerning) events
        if (reboot.Severity == RestartSeverity.Green || _copilot == null || !_copilot.IsAvailable)
            return;

        // Update heading to show the event type
        var headingText = reboot.CauseLabel;
        var headingIcon = reboot.FluentIcon;
        var headingColor = new SolidColorBrush(CauseColors.GetValueOrDefault(reboot.Cause, System.Windows.Media.Color.FromRgb(0x60, 0x7D, 0x8B)));

        // Check if we already have a cached explanation
        var cached = _copilot.GetCachedExplanation(reboot);
        if (cached != null)
        {
            MainPage.Visibility = Visibility.Collapsed;
            SummaryPage.Visibility = Visibility.Visible;
            SummaryPageHeading.Text = headingText;
            SummaryPageIcon.Text = headingIcon;
            SummaryPageIcon.Foreground = headingColor;
            SummaryPageLoading.Visibility = Visibility.Collapsed;
            SummaryPageScroll.Visibility = Visibility.Visible;
            SummaryFullText.Text = cached;
            return;
        }

        // Show summary page with loading indicator
        MainPage.Visibility = Visibility.Collapsed;
        SummaryPage.Visibility = Visibility.Visible;
        SummaryPageHeading.Text = headingText;
        SummaryPageIcon.Text = headingIcon;
        SummaryPageIcon.Foreground = headingColor;
        SummaryPageLoading.Visibility = Visibility.Visible;
        SummaryPageScroll.Visibility = Visibility.Collapsed;
        SummaryPageLoadingText.Text = "Analyzing...";

        // Animate loading bar
        double barWidth = 0;
        double direction = 1;
        _summaryPageLoadingTimer?.Stop();
        _summaryPageLoadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _summaryPageLoadingTimer.Tick += (_, _) =>
        {
            barWidth += direction * 3;
            if (barWidth >= 200) direction = -1;
            else if (barWidth <= 0) direction = 1;
            SummaryPageLoadingBar.Width = Math.Max(20, barWidth);
            SummaryPageLoadingBar.Margin = new Thickness(direction > 0 ? 0 : 200 - barWidth, 0, 0, 0);
        };
        _summaryPageLoadingTimer.Start();

        var result = new System.Text.StringBuilder();
        _ = _copilot.ExplainRestartAsync(
            reboot,
            onToken: token => Dispatcher.Invoke(() =>
            {
                if (result.Length == 0)
                {
                    _summaryPageLoadingTimer?.Stop();
                    SummaryPageLoading.Visibility = Visibility.Collapsed;
                    SummaryPageScroll.Visibility = Visibility.Visible;
                    SummaryFullText.Text = "";
                }
                result.Append(token);
                SummaryFullText.Text = result.ToString();
            }),
            onComplete: () => Dispatcher.Invoke(() =>
            {
                _summaryPageLoadingTimer?.Stop();
                SummaryPageLoading.Visibility = Visibility.Collapsed;
                SummaryPageScroll.Visibility = Visibility.Visible;
                // Cache the result for future use
                _copilot.CacheExplanation(reboot, result.ToString());
            }),
            onError: err => Dispatcher.Invoke(() =>
            {
                _summaryPageLoadingTimer?.Stop();
                SummaryPageLoading.Visibility = Visibility.Collapsed;
                SummaryPageScroll.Visibility = Visibility.Visible;
                SummaryFullText.Text = $"Unable to analyze: {err}";
            })
        );
    }

    // ── Period ──
    private void Period_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && int.TryParse(btn.Tag?.ToString(), out int days))
        {
            _periodDays = days;
            ApplyPeriod(days);
        }
    }

    private void ApplyPeriod(int days)
    {
        _selectedActivityDate = null; // Reset selection when period changes
        var cutoff = DateTime.Now.AddDays(-days);
        var filtered = _history.Where(e => e.Timestamp >= cutoff).ToList();
        UpdatePeriodButtons(days);
        StatTotal.Text = filtered.Count.ToString();
        ActivityStrip.Children.Clear();
        BreakdownCanvas.Children.Clear();
        RenderActivityStrip(filtered);
        RenderBreakdown(filtered);
        HistoryList.ItemsSource = filtered;
    }

    private void UpdatePeriodButtons(int active)
    {
        var a = (SolidColorBrush)Resources["AccentBrush"];
        var s = (SolidColorBrush)Resources["SubtleBrush"];
        Btn7d.Foreground = active == 7 ? a : s; Btn7d.FontWeight = active == 7 ? FontWeights.Bold : FontWeights.Normal;
        Btn14d.Foreground = active == 14 ? a : s; Btn14d.FontWeight = active == 14 ? FontWeights.Bold : FontWeights.Normal;
        Btn30d.Foreground = active == 30 ? a : s; Btn30d.FontWeight = active == 30 ? FontWeights.Bold : FontWeights.Normal;
    }

    private SolidColorBrush GetSeverityBrush(RestartSeverity sev) => sev switch
    {
        RestartSeverity.Red => (SolidColorBrush)Resources["SevErrorBrush"],
        RestartSeverity.Yellow => (SolidColorBrush)Resources["SevWarnBrush"],
        _ => (SolidColorBrush)Resources["SevOkBrush"]
    };

    // ── Activity strip ──
    private void RenderActivityStrip(List<RestartEvent> history)
    {
        var today = DateTime.Today;
        void Render() { ActivityStrip.Children.Clear(); DoRenderActivityStrip(history, today); }
        if (ActivityStrip.IsLoaded && ActivityStrip.ActualWidth > 0) Render();
        else Dispatcher.BeginInvoke(new Action(Render), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private DateTime? _selectedActivityDate;
    private List<RestartEvent>? _filteredHistory;

    private void DoRenderActivityStrip(List<RestartEvent> history, DateTime today)
    {
        _filteredHistory = history;
        var parent = ActivityStrip.Parent as FrameworkElement;
        double availableWidth = parent?.ActualWidth ?? 340;
        if (availableWidth <= 0) availableWidth = 340;
        double gap = 3, squareSize = 20;
        int maxSquares = (int)Math.Floor(availableWidth / (squareSize + gap));
        if (maxSquares < 7) maxSquares = 7;
        int totalDays = Math.Min(maxSquares, _periodDays);

        var dayData = new Dictionary<DateTime, (int count, RestartSeverity worst)>();
        for (int i = 0; i < totalDays; i++)
            dayData[today.AddDays(-(totalDays - 1) + i)] = (0, RestartSeverity.Green);
        foreach (var evt in history)
        {
            var day = evt.Timestamp.Date;
            if (!dayData.ContainsKey(day)) continue;
            var (c, w) = dayData[day]; c++;
            if (evt.Severity == RestartSeverity.Red) w = RestartSeverity.Red;
            else if (evt.Severity == RestartSeverity.Yellow && w != RestartSeverity.Red) w = RestartSeverity.Yellow;
            dayData[day] = (c, w);
        }

        ActivityPeriodLabel.Text = $"Last {totalDays} days";
        squareSize = Math.Floor((availableWidth - gap * (totalDays - 1)) / totalDays);
        if (squareSize < 14) squareSize = 14;

        for (int i = 0; i < totalDays; i++)
        {
            var date = today.AddDays(-(totalDays - 1) + i);
            var (count, worst) = dayData[date];
            var fill = count == 0 ? (SolidColorBrush)Resources["SevNoneBrush"] : GetSeverityBrush(worst);
            bool isToday = date == today;
            bool isSelected = _selectedActivityDate == date;

            var border = new Border
            {
                Width = squareSize, Height = squareSize, CornerRadius = new CornerRadius(3),
                Background = fill,
                Margin = new Thickness(i == 0 ? 0 : gap / 2, 0, gap / 2, 0),
                ToolTip = $"{date:d}: {count} restart{(count != 1 ? "s" : "")}",
                BorderBrush = isSelected ? (SolidColorBrush)Resources["AccentBrush"]
                             : isToday ? (SolidColorBrush)Resources["HighlightBrush"] : null,
                BorderThickness = (isSelected || isToday) ? new Thickness(2) : new Thickness(0),
                Cursor = count > 0 ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                Tag = date,
            };

            if (count > 0)
            {
                border.MouseLeftButtonUp += ActivitySquare_Click;
            }

            border.Child = new TextBlock
            {
                Text = date.ToString("ddd")[..1], FontSize = 8,
                Foreground = count == 0 ? (SolidColorBrush)Resources["SubtleBrush"]
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ActivityStrip.Children.Add(border);
        }
    }

    private void ActivitySquare_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not DateTime clickedDate)
            return;

        // Toggle selection
        if (_selectedActivityDate == clickedDate)
        {
            // Deselect
            _selectedActivityDate = null;
        }
        else
        {
            // Select this day
            _selectedActivityDate = clickedDate;
        }

        // Update visual selection state
        UpdateActivitySelectionVisuals();
        
        // Highlight matching history items and scroll to first one
        HighlightHistoryItems();
    }

    private void HighlightHistoryItems()
    {
        if (_filteredHistory == null) return;

        // Get the ScrollViewer parent of the HistoryList
        var scrollViewer = FindParent<ScrollViewer>(HistoryList);
        
        bool scrolledToFirst = false;

        // Update highlighting on items
        var itemContainer = HistoryList.ItemContainerGenerator;
        for (int i = 0; i < _filteredHistory.Count; i++)
        {
            var item = _filteredHistory[i];
            var container = itemContainer.ContainerFromIndex(i) as ContentPresenter;
            if (container == null) continue;

            var itemBorder = FindChild<Border>(container);
            if (itemBorder == null) continue;

            bool matches = _selectedActivityDate.HasValue && item.Timestamp.Date == _selectedActivityDate.Value;
            
            // Set highlight background for matching items
            if (matches)
            {
                itemBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x00, 0x78, 0xD4));
                if (!scrolledToFirst && scrollViewer != null)
                {
                    // Calculate offset and scroll to this item
                    var transform = container.TransformToAncestor(HistoryList);
                    var point = transform.Transform(new System.Windows.Point(0, 0));
                    scrollViewer.ScrollToVerticalOffset(point.Y);
                    scrolledToFirst = true;
                }
            }
            else
            {
                itemBorder.Background = System.Windows.Media.Brushes.Transparent;
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null && parent is not T)
            parent = VisualTreeHelper.GetParent(parent);
        return parent as T;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void UpdateActivitySelectionVisuals()
    {
        var today = DateTime.Today;
        foreach (var child in ActivityStrip.Children)
        {
            if (child is Border border && border.Tag is DateTime date)
            {
                bool isToday = date == today;
                bool isSelected = _selectedActivityDate == date;
                
                border.BorderBrush = isSelected ? (SolidColorBrush)Resources["AccentBrush"]
                                   : isToday ? (SolidColorBrush)Resources["HighlightBrush"] : null;
                border.BorderThickness = (isSelected || isToday) ? new Thickness(2) : new Thickness(0);
            }
        }
    }

    // ── Distribution ──
    private void RenderBreakdown(List<RestartEvent> history)
    {
        if (history.Count == 0) return;
        var groups = history.GroupBy(e => e.Cause)
            .Select(g => new { Cause = g.Key, Count = g.Count(), Label = g.First().CauseLabel })
            .OrderByDescending(g => g.Count).ToList();
        int total = groups.Sum(g => g.Count);

        BreakdownCanvas.Loaded += (_, _) =>
        {
            double tw = BreakdownCanvas.ActualWidth; if (tw <= 0) tw = 340; double x = 0;
            foreach (var g in groups)
            {
                double w = Math.Max(2, (double)g.Count / total * tw);
                var color = CauseColors.TryGetValue(g.Cause, out var c) ? c : System.Windows.Media.Color.FromRgb(0x60, 0x7D, 0x8B);
                var r = new System.Windows.Shapes.Rectangle { Width = w, Height = 8, Fill = new SolidColorBrush(color), ToolTip = $"{g.Label}: {g.Count}" };
                Canvas.SetLeft(r, x); Canvas.SetTop(r, 0); BreakdownCanvas.Children.Add(r); x += w;
            }
        };

        var items = new List<UIElement>();
        foreach (var g in groups)
        {
            var color = CauseColors.TryGetValue(g.Cause, out var c) ? c : System.Windows.Media.Color.FromRgb(0x60, 0x7D, 0x8B);
            var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 12, 2) };
            sp.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = $"{g.Label} ({g.Count})", FontSize = 10.5, Foreground = (SolidColorBrush)Resources["SubtleBrush"], VerticalAlignment = VerticalAlignment.Center });
            items.Add(sp);
        }
        BreakdownLegend.ItemsSource = items;
    }

    // ── Positioning ──
    private void PositionNearTray(System.Drawing.Point cursorPos)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = WinForms.Screen.FromPoint(cursorPos);
        var wa = screen.WorkingArea;
        double wL = wa.Left / dpi.DpiScaleX, wT = wa.Top / dpi.DpiScaleY;
        double wR = wa.Right / dpi.DpiScaleX, wB = wa.Bottom / dpi.DpiScaleY;
        double cX = cursorPos.X / dpi.DpiScaleX;
        double left = cX - (ActualWidth / 2), top = wB - ActualHeight;
        if (wa.Top > screen.Bounds.Top) top = wT;
        left = Math.Max(wL, Math.Min(left, wR - ActualWidth));
        top = Math.Max(wT, top);
        Left = left; Top = top;
    }

    private void PositionNearTaskbar()
    {
        var screen = WinForms.Screen.PrimaryScreen;
        if (screen == null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = screen.WorkingArea.Right / dpi.DpiScaleX - ActualWidth - 12;
        Top = screen.WorkingArea.Bottom / dpi.DpiScaleY - ActualHeight - 12;
    }

    // ── Window events ──
    private void Window_Deactivated(object? sender, EventArgs e) => Hide();
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SummaryPage.Visibility == Visibility.Visible)
                BackToMain_Click(sender, e);
            else
                Hide();
            e.Handled = true;
        }
    }

    private void ApplyTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            if (key?.GetValue("AppsUseLightTheme") is int v && v == 1)
            {
                Resources["BackgroundBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF2, 0xF5, 0xF5, 0xF5));
                Resources["ForegroundBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));
                Resources["SubtleBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
                Resources["CardBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
                Resources["BorderBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0));
                Resources["SevNoneBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEB, 0xED, 0xF0));
                Resources["HighlightBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                Resources["ScrollThumbBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xBB, 0xBB, 0xBB));
                Resources["ScrollTrackBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0));
            }
        }
        catch { }
    }
}
