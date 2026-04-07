namespace RestartHistory.Models;

public enum RestartCause
{
    WindowsUpdate,
    UserShutdown,
    SoftwareInstall,
    UnexpectedShutdown,
    PowerLoss,
    Bsod,
    NormalBoot,
    Unknown
}

public enum RestartSeverity
{
    Green,  // Normal/planned
    Yellow, // Unexpected
    Red     // BSOD/crash
}

public class RestartEvent
{
    public DateTime Timestamp { get; set; }
    public RestartCause Cause { get; set; }
    public string CauseDescription { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int EventId { get; set; }

    public RestartSeverity Severity => Cause switch
    {
        RestartCause.Bsod => RestartSeverity.Red,
        RestartCause.PowerLoss => RestartSeverity.Yellow,
        RestartCause.UnexpectedShutdown => RestartSeverity.Yellow,
        _ => RestartSeverity.Green
    };

    public string CauseLabel => Cause switch
    {
        RestartCause.WindowsUpdate => "Windows Update",
        RestartCause.UserShutdown => "User Restart",
        RestartCause.SoftwareInstall => "Software Install",
        RestartCause.UnexpectedShutdown => "Unexpected Shutdown",
        RestartCause.PowerLoss => "Power Loss",
        RestartCause.Bsod => "Blue Screen / Crash",
        RestartCause.NormalBoot => "Normal Boot",
        _ => "Unknown"
    };

    /// <summary>Short label that fits in a tile without truncation.</summary>
    public string CauseShortLabel => Cause switch
    {
        RestartCause.WindowsUpdate => "Update",
        RestartCause.UserShutdown => "User",
        RestartCause.SoftwareInstall => "Install",
        RestartCause.UnexpectedShutdown => "Unexpected",
        RestartCause.PowerLoss => "Power",
        RestartCause.Bsod => "Crash",
        RestartCause.NormalBoot => "Normal",
        _ => "Unknown"
    };

    /// <summary>Segoe Fluent Icons glyph per cause type.</summary>
    public string FluentIcon => Cause switch
    {
        RestartCause.WindowsUpdate => "\uE777",    // Update
        RestartCause.UserShutdown => "\uE7E8",      // Contact (user)
        RestartCause.SoftwareInstall => "\uE74C",   // Accept/install
        RestartCause.UnexpectedShutdown => "\uE7BA", // Error
        RestartCause.PowerLoss => "\uEA6C",          // Lightning/Power
        RestartCause.Bsod => "\uE783",               // Error badge
        RestartCause.NormalBoot => "\uE73E",         // CheckMark
        _ => "\uE9CE"                                // Unknown
    };

    /// <summary>Color hex string for the cause icon.</summary>
    public string SeverityColor => Severity switch
    {
        RestartSeverity.Red => "#F44336",
        RestartSeverity.Yellow => "#FFC107",
        _ => "#4CAF50"
    };

    /// <summary>Timestamp formatted using system regional settings.</summary>
    public string FormattedTimestamp => Timestamp.ToString("d MMM  HH:mm");

    // Keep legacy for any remaining usage
    public string SeverityIcon => Severity switch
    {
        RestartSeverity.Red => "\u274C",
        RestartSeverity.Yellow => "\u26A0",
        _ => "\u2705"
    };
}
