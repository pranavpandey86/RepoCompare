namespace RepoCompare.Models;

/// <summary>
/// Represents a case-only collision between two file paths.
/// On Linux (case-sensitive filesystem), these paths resolve to different files;
/// on Windows/macOS-default, they resolve to the same file.
/// This is a critical issue when migrating to Linux containers.
/// </summary>
public record CaseCollision(
    /// <summary>First path variant (e.g., "Config/appsettings.json").</summary>
    string Path1,

    /// <summary>Second path variant (e.g., "config/appsettings.json").</summary>
    string Path2,

    /// <summary>"Source", "Target", or "CrossSide" indicating where the collision exists.</summary>
    string Side,

    /// <summary>Human-readable description of the impact.</summary>
    string Impact
);
