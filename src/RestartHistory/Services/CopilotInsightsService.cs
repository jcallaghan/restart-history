using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using GitHub.Copilot.SDK;
using RestartHistory.Models;

namespace RestartHistory.Services;

/// <summary>
/// Provides AI-powered reboot insights via the GitHub Copilot SDK.
/// Opt-in: only activates if Copilot CLI is detected in PATH.
/// </summary>
public class CopilotInsightsService : IAsyncDisposable
{
    private CopilotClient? _client;
    private bool _available;
    private bool _initialized;
    private string? _cachedShortSummary;
    private string? _cachedDetailedSummary;
    private int _analyzing; // 0=idle, 1=active (Interlocked)
    private readonly ConcurrentDictionary<string, string> _eventExplanationCache = new();

    public bool IsAvailable => _available;
    public string? CachedShortSummary => _cachedShortSummary;
    public string? CachedDetailedSummary => _cachedDetailedSummary;
    public bool IsAnalyzing => _analyzing != 0;

    /// <summary>Event raised when cached insights are updated (short, detailed).</summary>
    public event Action<string, string>? InsightsUpdated;

    /// <summary>Clears all cached summaries and explanations.</summary>
    public void ClearCache()
    {
        _cachedShortSummary = null;
        _cachedDetailedSummary = null;
        _eventExplanationCache.Clear();
    }

    /// <summary>Gets a cached explanation for a restart event, or null if not cached.</summary>
    public string? GetCachedExplanation(RestartEvent evt) =>
        _eventExplanationCache.TryGetValue(GetEventKey(evt), out var explanation) ? explanation : null;

    /// <summary>Caches an explanation for a restart event.</summary>
    public void CacheExplanation(RestartEvent evt, string explanation) =>
        _eventExplanationCache[GetEventKey(evt)] = explanation;

    private static string GetEventKey(RestartEvent evt) => $"{evt.Timestamp:o}_{evt.EventId}";

    public static bool IsCopilotAvailable()
    {
        try
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var name in new[] { "copilot.exe", "copilot", "copilot.cmd", "copilot.bat" })
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, name))) return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initialize the Copilot client. Call once at startup.
    /// Returns true if Copilot is available and ready.
    /// </summary>
    public async Task<bool> TryInitializeAsync()
    {
        if (_initialized) return _available;
        _initialized = true;

        if (!IsCopilotAvailable())
        {
            _available = false;
            return false;
        }

        try
        {
            _client = new CopilotClient(new CopilotClientOptions
            {
                UseStdio = true,
                AutoStart = true,
            });
            await _client.StartAsync();
            _available = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copilot SDK init failed: {ex.Message}");
            _available = false;
            return false;
        }
    }

    /// <summary>
    /// Analyze a single reboot event and return a plain-English explanation.
    /// Streams tokens via the onToken callback.
    /// </summary>
    public async Task ExplainRestartAsync(
        RestartEvent reboot,
        Action<string> onToken,
        Action onComplete,
        Action<string> onError)
    {
        if (!_available || _client == null)
        {
            onError("Copilot is not available");
            return;
        }

        try
        {
            await using var session = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = "gpt-4o",
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = @"You are a Windows system analyst embedded in a system tray utility called Restart History.
When given a Windows restart event, explain what caused it and whether it's concerning.
Use plain English. Do NOT use markdown formatting, headers, or bullet points.
Write in 2-3 short paragraphs.

If the event includes error codes (BugCheck codes, Event IDs, stop codes):
- Look up their meaning and explain what the code indicates
- Suggest potential causes and fixes

Do NOT mention the OS version or build number in your response - focus on the event itself."
                },
                InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            });

            var done = new TaskCompletionSource();

            session.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        onToken(delta.Data.DeltaContent ?? "");
                        break;
                    case SessionIdleEvent:
                        onComplete();
                        done.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        onError(err.Data.Message ?? "Unknown error");
                        done.TrySetResult();
                        break;
                }
            });

            var prompt = $@"Explain this Windows restart event (no markdown, no OS version mentions):
- Date: {reboot.Timestamp:g}
- Classification: {reboot.CauseLabel}
- Severity: {reboot.Severity}
- Event ID: {reboot.EventId}
- Detail: {reboot.Detail}

If there are error codes, explain what they mean and potential causes.";

            await session.SendAsync(new MessageOptions { Prompt = prompt });
            await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            onError("Copilot analysis timed out");
        }
        catch (Exception ex)
        {
            onError($"Copilot error: {ex.Message}");
        }
    }

    /// <summary>
    /// Analyze the full reboot history and identify patterns.
    /// Generates both a short summary (1-2 sentences) and detailed analysis.
    /// </summary>
    public async Task AnalyzePatternsAsync(
        List<RestartEvent> history,
        Action<string> onShortSummary,
        Action<string> onDetailedSummary,
        Action onComplete,
        Action<string> onError)
    {
        if (!_available || _client == null)
        {
            onError("Copilot is not available");
            return;
        }

        try
        {
            // First: Generate short summary focused on most recent restart
            await using var shortSession = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = "gpt-4o",
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = @"You are a Windows system analyst. Write 1-2 sentences about the MOST RECENT restart event.
Explain what caused the most recent restart and whether it's normal or concerning.
You may briefly mention the overall pattern (e.g., 'part of regular update cycle') for context.
No bullet points, markdown, or headers. Plain English only.
Do NOT mention OS version or build number."
                },
                InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            });

            var shortSb = new StringBuilder();
            if (history.Count > 0)
            {
                var recent = history[0];
                shortSb.AppendLine($"Most recent restart: {recent.Timestamp:g} | {recent.CauseLabel} | {recent.Severity}");
                shortSb.AppendLine($"Detail: {recent.Detail}");
                shortSb.AppendLine();
                shortSb.AppendLine($"Context: {history.Count} total restarts in period:");
                foreach (var evt in history.Skip(1).Take(5))
                    shortSb.AppendLine($"- {evt.Timestamp:g} | {evt.CauseLabel}");
                if (history.Count > 6)
                    shortSb.AppendLine($"...and {history.Count - 6} more");
            }
            shortSb.AppendLine();
            shortSb.AppendLine("Summarize the most recent restart in 1-2 sentences with brief context:");

            var shortDone = new TaskCompletionSource();
            var shortResult = new StringBuilder();
            shortSession.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        shortResult.Append(delta.Data.DeltaContent ?? "");
                        break;
                    case SessionIdleEvent:
                        shortDone.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        Debug.WriteLine($"Copilot short summary session error: {err.Data.Message}");
                        shortDone.TrySetResult();
                        break;
                }
            });
            await shortSession.SendAsync(new MessageOptions { Prompt = shortSb.ToString() });
            await shortDone.Task.WaitAsync(TimeSpan.FromSeconds(30));
            onShortSummary(shortResult.ToString().Trim());

            // Second: Generate detailed holistic summary
            await using var detailSession = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = "gpt-4o",
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Replace,
                    Content = @"You are a Windows system analyst embedded in a system tray utility called Restart History.
Provide a HOLISTIC analysis of the entire restart history in 2-4 short paragraphs.
Cover: overall health trends, recurring patterns, frequency analysis, problematic event clusters, and general recommendations.
If you see crash events with error codes, explain what they mean and potential causes.
Use plain English. Do NOT use bullet points, numbered lists, markdown headers (##), or any formatting.
Write naturally in flowing paragraph form. Keep paragraphs short (2-3 sentences each).
Do NOT mention the operating system version, build number, or hardware specs - focus only on holistic restart patterns."
                },
                InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            });

            var detailDone = new TaskCompletionSource();
            var detailResult = new StringBuilder();

            detailSession.On(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        detailResult.Append(delta.Data.DeltaContent ?? "");
                        break;
                    case SessionIdleEvent:
                        detailDone.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        Debug.WriteLine($"Copilot detail session error: {err.Data.Message}");
                        detailDone.TrySetResult();
                        break;
                }
            });

            var detailSb = new StringBuilder();
            detailSb.AppendLine("Provide a holistic analysis of these Windows restart events (no markdown):");
            detailSb.AppendLine();
            foreach (var evt in history)
                detailSb.AppendLine($"- {evt.Timestamp:g} | {evt.CauseLabel} | Severity: {evt.Severity} | EventID: {evt.EventId} | {evt.Detail}");

            await detailSession.SendAsync(new MessageOptions { Prompt = detailSb.ToString() });
            await detailDone.Task.WaitAsync(TimeSpan.FromSeconds(30));
            onDetailedSummary(detailResult.ToString().Trim());
            onComplete();
        }
        catch (Exception ex)
        {
            onError($"Copilot error: {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-analyze and cache. Fires InsightsUpdated when done.
    /// Safe to call from background thread.
    /// </summary>
    public async Task AnalyzeAndCacheAsync(List<RestartEvent> history, CancellationToken cancellationToken = default)
    {
        if (!_available || _client == null) return;
        // Atomic compare-and-swap to prevent concurrent analysis
        if (Interlocked.CompareExchange(ref _analyzing, 1, 0) != 0)
            return;

        try
        {
            var done = new TaskCompletionSource();
            string shortSummary = "";
            string detailedSummary = "";

            await AnalyzePatternsAsync(
                history,
                onShortSummary: s => shortSummary = s,
                onDetailedSummary: d => detailedSummary = d,
                onComplete: () =>
                {
                    _cachedShortSummary = shortSummary;
                    _cachedDetailedSummary = detailedSummary;
                    InsightsUpdated?.Invoke(shortSummary, detailedSummary);
                    done.TrySetResult();
                },
                onError: error =>
                {
                    // Signal completion so any listening popup can exit loading state
                    InsightsUpdated?.Invoke("Unable to generate insights.", "");
                    done.TrySetResult();
                }
            );

            await done.Task;

            // Pre-fetch explanations for all non-green events
            var concerningEvents = history.Where(e => e.Severity != RestartSeverity.Green).ToList();
            foreach (var evt in concerningEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (GetCachedExplanation(evt) != null) continue;
                try
                {
                    var result = new StringBuilder();
                    var explainDone = new TaskCompletionSource();
                    await ExplainRestartAsync(
                        evt,
                        onToken: token => result.Append(token),
                        onComplete: () =>
                        {
                            CacheExplanation(evt, result.ToString());
                            explainDone.TrySetResult();
                        },
                        onError: _ => explainDone.TrySetResult()
                    );
                    await explainDone.Task;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* Non-critical — user can still fetch on demand */ }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _analyzing, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
            await _client.DisposeAsync();
    }
}
