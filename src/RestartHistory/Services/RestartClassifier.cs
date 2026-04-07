using System.Diagnostics.Eventing.Reader;
using RestartHistory.Models;

namespace RestartHistory.Services;

public static class RestartClassifier
{
    public static List<RestartEvent> GetRestartHistory(int maxResults = 10)
    {
        var events = new List<RestartEvent>();

        // Gather raw events: 1074, 6008, 41 (Kernel-Power), 1001 (BugCheck), 6009
        var rawEvents = QueryEvents();

        // Sort by time descending
        rawEvents.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        // Build a set of BugCheck (1001) timestamps for BSOD correlation
        var bugCheckTimes = new HashSet<DateTime>();
        foreach (var evt in rawEvents.Where(e => e.EventId == 1001))
        {
            bugCheckTimes.Add(evt.Timestamp.Date); // correlate by date (within same day)
        }

        // Classify events
        foreach (var evt in rawEvents)
        {
            RestartEvent? classified = evt.EventId switch
            {
                1074 => Classify1074(evt),
                6008 => new RestartEvent
                {
                    Timestamp = evt.Timestamp,
                    Cause = RestartCause.UnexpectedShutdown,
                    CauseDescription = "Unexpected Shutdown",
                    Detail = "The previous system shutdown was unexpected (Event 6008)",
                    EventId = 6008
                },
                41 => ClassifyKernelPower41(evt, bugCheckTimes),
                _ => null
            };

            if (classified != null)
            {
                events.Add(classified);
                if (events.Count >= maxResults)
                    break;
            }
        }

        return events;
    }

    private static List<RawEvent> QueryEvents()
    {
        var results = new List<RawEvent>();

        // Query for Event IDs: 1074, 6008, 41, 1001 — with 90-day time filter
        string query = @"*[System[(EventID=1074 or EventID=6008 or EventID=41 or EventID=1001) and TimeCreated[timediff(@SystemTime) <= 7776000000]]]";

        try
        {
            using var logReader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query));
            EventRecord? record;
            while ((record = logReader.ReadEvent()) != null)
            {
                using (record)
                {
                    results.Add(new RawEvent
                    {
                        EventId = record.Id,
                        Timestamp = record.TimeCreated ?? DateTime.MinValue,
                        ProviderName = record.ProviderName ?? string.Empty,
                        Properties = record.Properties?.Select(p => p.Value?.ToString() ?? string.Empty).ToList()
                            ?? new List<string>(),
                        FormatDescription = TryGetDescription(record)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error querying event log: {ex.Message}");
        }

        return results;
    }

    private static string TryGetDescription(EventRecord record)
    {
        try
        {
            return record.FormatDescription() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static RestartEvent Classify1074(RawEvent evt)
    {
        var description = evt.FormatDescription.ToLowerInvariant();
        var props = evt.Properties;

        // Extract process name from properties (typically index 0)
        var processName = props.Count > 0 ? props[0].ToLowerInvariant() : string.Empty;
        // Reason string is typically in the description or properties
        var reason = props.Count > 4 ? props[4] : string.Empty;

        // Check for Windows Update indicators
        if (processName.Contains("trustedinstaller") ||
            processName.Contains("mousocoreworker") ||
            description.Contains("operating system: upgrade") ||
            description.Contains("service pack") ||
            reason.ToLowerInvariant().Contains("operating system: upgrade") ||
            reason.ToLowerInvariant().Contains("service pack"))
        {
            return new RestartEvent
            {
                Timestamp = evt.Timestamp,
                Cause = RestartCause.WindowsUpdate,
                CauseDescription = "Windows Update",
                Detail = TrimDetail(description),
                EventId = 1074
            };
        }

        // Check for user-initiated shutdown
        if (processName.Contains("shutdown.exe"))
        {
            return new RestartEvent
            {
                Timestamp = evt.Timestamp,
                Cause = RestartCause.UserShutdown,
                CauseDescription = "User Shutdown/Restart",
                Detail = TrimDetail(description),
                EventId = 1074
            };
        }

        // Check for software install
        if (processName.Contains("setup.exe") || processName.Contains("msiexec"))
        {
            return new RestartEvent
            {
                Timestamp = evt.Timestamp,
                Cause = RestartCause.SoftwareInstall,
                CauseDescription = "Software Install",
                Detail = TrimDetail(description),
                EventId = 1074
            };
        }

        // Default 1074 — planned shutdown by SYSTEM or other process
        // Check reason fields for update-related content
        if (description.Contains("update") || description.Contains("upgrade"))
        {
            return new RestartEvent
            {
                Timestamp = evt.Timestamp,
                Cause = RestartCause.WindowsUpdate,
                CauseDescription = "Windows Update",
                Detail = TrimDetail(description),
                EventId = 1074
            };
        }

        return new RestartEvent
        {
            Timestamp = evt.Timestamp,
            Cause = RestartCause.UserShutdown,
            CauseDescription = "Planned Shutdown/Restart",
            Detail = TrimDetail(description),
            EventId = 1074
        };
    }

    private static RestartEvent ClassifyKernelPower41(RawEvent evt, HashSet<DateTime> bugCheckDates)
    {
        // If a BugCheck event (1001) occurred on the same day, classify as BSOD
        bool hasBugCheck = bugCheckDates.Contains(evt.Timestamp.Date);

        if (hasBugCheck)
        {
            return new RestartEvent
            {
                Timestamp = evt.Timestamp,
                Cause = RestartCause.Bsod,
                CauseDescription = "BSOD/Crash",
                Detail = "Kernel-Power 41 with BugCheck — system crash detected",
                EventId = 41
            };
        }

        return new RestartEvent
        {
            Timestamp = evt.Timestamp,
            Cause = RestartCause.PowerLoss,
            CauseDescription = "Power Loss",
            Detail = "Kernel-Power 41 without BugCheck — likely power loss",
            EventId = 41
        };
    }

    private static string TrimDetail(string description)
    {
        if (description.Length > 200)
            return description[..200] + "...";
        return description;
    }

    private class RawEvent
    {
        public int EventId { get; set; }
        public DateTime Timestamp { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public List<string> Properties { get; set; } = new();
        public string FormatDescription { get; set; } = string.Empty;
    }
}
