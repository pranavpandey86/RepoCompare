namespace RepoCompare.Models;

/// <summary>
/// Aggregate summary of the full directory comparison.
/// </summary>
public class ComparisonSummary
{
    /// <summary>Absolute path to the source directory.</summary>
    public required string SourcePath { get; set; }

    /// <summary>Absolute path to the target directory.</summary>
    public required string TargetPath { get; set; }

    /// <summary>Human-readable label for the source (e.g., "OldRepo / feature-branch").</summary>
    public string SourceLabel { get; set; } = "Source";

    /// <summary>Human-readable label for the target (e.g., "NewRepo / main").</summary>
    public string TargetLabel { get; set; } = "Target";

    /// <summary>All individual file comparison results.</summary>
    public List<ComparisonResult> Results { get; set; } = [];

    /// <summary>Case-only path collisions detected across both directories.</summary>
    public List<CaseCollision> CaseCollisions { get; set; } = [];

    /// <summary>All Linux-incompatibility issues detected across all files.</summary>
    public List<LinuxIssue> AllLinuxIssues { get; set; } = [];

    // ── Basic Counts ──────────────────────────────────────

    public int TotalFiles => Results.Count;
    public int IdenticalCount => Results.Count(r => r.Status == FileStatus.Identical);
    public int ModifiedCount => Results.Count(r => r.Status == FileStatus.Modified);
    public int OnlyInSourceCount => Results.Count(r => r.Status == FileStatus.OnlyInSource);
    public int OnlyInTargetCount => Results.Count(r => r.Status == FileStatus.OnlyInTarget);

    // ── Status Filters ────────────────────────────────────

    public IEnumerable<ComparisonResult> Identical => Results.Where(r => r.Status == FileStatus.Identical);
    public IEnumerable<ComparisonResult> Modified => Results.Where(r => r.Status == FileStatus.Modified);
    public IEnumerable<ComparisonResult> OnlyInSource => Results.Where(r => r.Status == FileStatus.OnlyInSource);
    public IEnumerable<ComparisonResult> OnlyInTarget => Results.Where(r => r.Status == FileStatus.OnlyInTarget);

    // ── Category/Risk Filters ─────────────────────────────

    public IEnumerable<ComparisonResult> ContainerSpecific =>
        Results.Where(r => r.Category == ChangeCategory.ContainerSpecific);

    public IEnumerable<ComparisonResult> HighRisk =>
        Results.Where(r => r.Risk == RiskLevel.HighRisk);

    public IEnumerable<ComparisonResult> SafeToCopy =>
        Results.Where(r => r.Risk == RiskLevel.SafeToCopy);

    public IEnumerable<ComparisonResult> ActionableFiles =>
        Results.Where(r => r.Status != FileStatus.Identical);

    // ── Grouped Counts ────────────────────────────────────

    /// <summary>Breakdown of modified files by extension.</summary>
    public Dictionary<string, int> ModifiedByExtension =>
        Modified.GroupBy(r => r.Extension)
                .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Breakdown of all non-identical files by category.</summary>
    public Dictionary<ChangeCategory, int> CountByCategory =>
        ActionableFiles.GroupBy(r => r.Category)
                       .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Breakdown of all non-identical files by risk level.</summary>
    public Dictionary<RiskLevel, int> CountByRisk =>
        ActionableFiles.GroupBy(r => r.Risk)
                       .ToDictionary(g => g.Key, g => g.Count());
}
