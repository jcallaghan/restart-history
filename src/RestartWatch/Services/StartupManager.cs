using Microsoft.Win32;

namespace RestartWatch.Services;

public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "RestartWatch";

    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) != null;
    }

    public static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "RestartWatch.exe");
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}
