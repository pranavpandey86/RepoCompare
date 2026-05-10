namespace RepoCompare.Models;

/// <summary>
/// Classification of a file after comparison.
/// </summary>
public enum FileStatus
{
    /// <summary>Content is identical after normalization (noise — ignore).</summary>
    Identical,

    /// <summary>Content has genuine differences (real change).</summary>
    Modified,

    /// <summary>File exists only in the source directory.</summary>
    OnlyInSource,

    /// <summary>File exists only in the target directory.</summary>
    OnlyInTarget
}

/// <summary>
/// Result of comparing a single file between source and target directories.
/// </summary>
public class ComparisonResult
{
    /// <summary>Path relative to the repository root (e.g., "Services/OrderService.cs").</summary>
    public required string RelativePath { get; set; }

    /// <summary>Classification after comparison.</summary>
    public FileStatus Status { get; set; }

    /// <summary>Unified diff output for Modified files. Null for other statuses.</summary>
    public string? UnifiedDiff { get; set; }

    /// <summary>True if the file was detected as binary.</summary>
    public bool IsBinary { get; set; }

    /// <summary>File size in bytes in the source directory (0 if not present).</summary>
    public long SourceSizeBytes { get; set; }

    /// <summary>File size in bytes in the target directory (0 if not present).</summary>
    public long TargetSizeBytes { get; set; }

    /// <summary>File extension (e.g., ".cs", ".csproj").</summary>
    public string Extension => Path.GetExtension(RelativePath).ToLowerInvariant();

    /// <summary>Heuristic classification of what kind of change this represents.</summary>
    public ChangeCategory Category { get; set; } = ChangeCategory.Unknown;

    /// <summary>Risk level for applying this change.</summary>
    public RiskLevel Risk { get; set; } = RiskLevel.ReviewRequired;

    /// <summary>Linux-incompatibility issues detected in this file.</summary>
    public List<LinuxIssue> LinuxIssues { get; set; } = [];

    /// <summary>Human-readable explanations of why this file has its current risk level.</summary>
    public List<string> RiskReasons { get; set; } = [];
}
