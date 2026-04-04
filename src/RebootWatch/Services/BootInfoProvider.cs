using System.Management;

namespace RebootWatch.Services;

public static class BootInfoProvider
{
    public static DateTime GetLastBootTime()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var bootTimeStr = obj["LastBootUpTime"]?.ToString();
                if (bootTimeStr != null)
                {
                    return ManagementDateTimeConverter.ToDateTime(bootTimeStr);
                }
            }
        }
        catch
        {
            // Fallback: use Environment.TickCount64
        }

        return DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    public static TimeSpan GetUptime()
    {
        return DateTime.Now - GetLastBootTime();
    }

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m";
    }
}
