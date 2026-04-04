namespace RebootWatch.Models;

public enum RebootCause
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

public enum RebootSeverity
{
    Green,  // Normal/planned
    Yellow, // Unexpected
    Red     // BSOD/crash
}

public class RebootEvent
{
    public DateTime Timestamp { get; set; }
    public RebootCause Cause { get; set; }
    public string CauseDescription { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int EventId { get; set; }

    public RebootSeverity Severity => Cause switch
    {
        RebootCause.Bsod => RebootSeverity.Red,
        RebootCause.PowerLoss => RebootSeverity.Yellow,
        RebootCause.UnexpectedShutdown => RebootSeverity.Yellow,
        _ => RebootSeverity.Green
    };

    public string CauseLabel => Cause switch
    {
        RebootCause.WindowsUpdate => "Windows Update",
        RebootCause.UserShutdown => "User Shutdown/Restart",
        RebootCause.SoftwareInstall => "Software Install",
        RebootCause.UnexpectedShutdown => "Unexpected Shutdown",
        RebootCause.PowerLoss => "Power Loss",
        RebootCause.Bsod => "BSOD/Crash",
        RebootCause.NormalBoot => "Normal Boot",
        _ => "Unknown"
    };

    /// <summary>Short label that fits in a tile without truncation.</summary>
    public string CauseShortLabel => Cause switch
    {
        RebootCause.WindowsUpdate => "Update",
        RebootCause.UserShutdown => "User",
        RebootCause.SoftwareInstall => "Install",
        RebootCause.UnexpectedShutdown => "Unexpected",
        RebootCause.PowerLoss => "Power",
        RebootCause.Bsod => "BSOD",
        RebootCause.NormalBoot => "Normal",
        _ => "Unknown"
    };

    /// <summary>Segoe Fluent Icons glyph per cause type.</summary>
    public string FluentIcon => Cause switch
    {
        RebootCause.WindowsUpdate => "\uE777",    // Update
        RebootCause.UserShutdown => "\uE7E8",      // Contact (user)
        RebootCause.SoftwareInstall => "\uE74C",   // Accept/install
        RebootCause.UnexpectedShutdown => "\uE7BA", // Error
        RebootCause.PowerLoss => "\uEA6C",          // Lightning/Power
        RebootCause.Bsod => "\uE783",               // Error badge
        RebootCause.NormalBoot => "\uE73E",         // CheckMark
        _ => "\uE9CE"                                // Unknown
    };

    /// <summary>Color hex string for the cause icon.</summary>
    public string SeverityColor => Severity switch
    {
        RebootSeverity.Red => "#F44336",
        RebootSeverity.Yellow => "#FFC107",
        _ => "#4CAF50"
    };

    /// <summary>Timestamp formatted using system regional settings.</summary>
    public string FormattedTimestamp => Timestamp.ToString("dd MMM") + "  " + Timestamp.ToString("HH:mm");

    // Keep legacy for any remaining usage
    public string SeverityIcon => Severity switch
    {
        RebootSeverity.Red => "\u274C",
        RebootSeverity.Yellow => "\u26A0",
        _ => "\u2705"
    };
}
