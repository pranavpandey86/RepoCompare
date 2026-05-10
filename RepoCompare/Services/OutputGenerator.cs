using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RepoCompare.Models;

namespace RepoCompare.Services;

/// <summary>
/// Generates structured output files: JSON report, CSV files, text file lists,
/// and non-destructive dry-run/apply scripts.
/// </summary>
public class OutputGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes all output files to the specified directory.
    /// </summary>
    public void WriteAll(ComparisonSummary summary, string outputDir, bool includeApplyList = false, string? applyListPath = null)
    {
        Directory.CreateDirectory(outputDir);

        WriteJsonReport(summary, Path.Combine(outputDir, "report.json"));
        WriteHighRiskCsv(summary, Path.Combine(outputDir, "high-risk-files.csv"));
        WriteLinuxIssuesCsv(summary, Path.Combine(outputDir, "linux-issues.csv"));
        WriteContainerOnlyFiles(summary, Path.Combine(outputDir, "container-only-files.txt"));
        WriteDryRunScript(summary, Path.Combine(outputDir, "dry-run.sh"));

        if (includeApplyList && applyListPath != null && File.Exists(applyListPath))
        {
            var filesToApply = File.ReadAllLines(applyListPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Trim())
                .ToList();
            WriteApplyScript(summary, filesToApply, Path.Combine(outputDir, "apply-changes.sh"));
        }
    }

    /// <summary>
    /// Writes the full comparison summary as a JSON report.
    /// Excludes unified diffs from JSON to keep file size manageable.
    /// </summary>
    public void WriteJsonReport(ComparisonSummary summary, string outputPath)
    {
        var report = new
        {
            generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            source = new { path = summary.SourcePath, label = summary.SourceLabel },
            target = new { path = summary.TargetPath, label = summary.TargetLabel },
            totals = new
            {
                total = summary.TotalFiles,
                identical = summary.IdenticalCount,
                modified = summary.ModifiedCount,
                onlyInSource = summary.OnlyInSourceCount,
                onlyInTarget = summary.OnlyInTargetCount,
                caseCollisions = summary.CaseCollisions.Count,
                linuxIssues = summary.AllLinuxIssues.Count
            },
            byCategory = summary.CountByCategory.ToDictionary(
                kvp => kvp.Key.ToString(), kvp => kvp.Value),
            byRisk = summary.CountByRisk.ToDictionary(
                kvp => kvp.Key.ToString(), kvp => kvp.Value),
            caseCollisions = summary.CaseCollisions.Select(c => new
            {
                path1 = c.Path1,
                path2 = c.Path2,
                side = c.Side,
                impact = c.Impact
            }),
            files = summary.Results
                .Where(r => r.Status != FileStatus.Identical)
                .OrderBy(r => r.Risk)
                .ThenBy(r => r.Category)
                .ThenBy(r => r.RelativePath)
                .Select(r => new
                {
                    path = r.RelativePath,
                    status = r.Status.ToString(),
                    category = r.Category.ToString(),
                    risk = r.Risk.ToString(),
                    isBinary = r.IsBinary ? true : (bool?)null,
                    sourceSizeBytes = r.SourceSizeBytes > 0 ? r.SourceSizeBytes : (long?)null,
                    targetSizeBytes = r.TargetSizeBytes > 0 ? r.TargetSizeBytes : (long?)null,
                    riskReasons = r.RiskReasons,
                    linuxIssueCount = r.LinuxIssues.Count > 0 ? r.LinuxIssues.Count : (int?)null
                })
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(outputPath, json);
    }

    /// <summary>
    /// Writes a CSV of high-risk and review-required files.
    /// </summary>
    public void WriteHighRiskCsv(ComparisonSummary summary, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Path,Status,Category,Risk,RiskReasons,LinuxIssueCount");

        foreach (var r in summary.Results
            .Where(r => r.Risk is RiskLevel.HighRisk or RiskLevel.ReviewRequired)
            .OrderBy(r => r.Risk)
            .ThenBy(r => r.RelativePath))
        {
            var reasons = string.Join("; ", r.RiskReasons).Replace("\"", "\"\"");
            sb.AppendLine($"\"{r.RelativePath}\",{r.Status},{r.Category},{r.Risk},\"{reasons}\",{r.LinuxIssues.Count}");
        }

        File.WriteAllText(outputPath, sb.ToString());
    }

    /// <summary>
    /// Writes a CSV of all Linux-incompatibility issues.
    /// </summary>
    public void WriteLinuxIssuesCsv(ComparisonSummary summary, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FilePath,LineNumber,IssueType,Severity,Description,LineContent");

        foreach (var issue in summary.AllLinuxIssues
            .OrderBy(i => i.Severity == "High" ? 0 : i.Severity == "Medium" ? 1 : i.Severity == "Low" ? 2 : 3)
            .ThenBy(i => i.FilePath)
            .ThenBy(i => i.LineNumber))
        {
            var desc = issue.Description.Replace("\"", "\"\"");
            var content = issue.LineContent.Replace("\"", "\"\"");
            sb.AppendLine($"\"{issue.FilePath}\",{issue.LineNumber},{issue.IssueType},{issue.Severity},\"{desc}\",\"{content}\"");
        }

        File.WriteAllText(outputPath, sb.ToString());
    }

    /// <summary>
    /// Writes a plain text file listing only container-specific, safe-to-copy files (one per line).
    /// </summary>
    public void WriteContainerOnlyFiles(ComparisonSummary summary, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Container-specific files safe to copy");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# Review this list, then use: --apply-list container-only-files.txt");
        sb.AppendLine();

        foreach (var r in summary.Results
            .Where(r => r.Category == ChangeCategory.ContainerSpecific && r.Risk == RiskLevel.SafeToCopy)
            .OrderBy(r => r.RelativePath))
        {
            sb.AppendLine(r.RelativePath);
        }

        // Also list container-specific files that need review, commented out
        var reviewFiles = summary.Results
            .Where(r => r.Category == ChangeCategory.ContainerSpecific && r.Risk != RiskLevel.SafeToCopy)
            .OrderBy(r => r.RelativePath)
            .ToList();

        if (reviewFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("# ── Below files are container-specific but need review before copying ──");
            foreach (var r in reviewFiles)
            {
                sb.AppendLine($"# {r.RelativePath}  [{r.Risk}] {string.Join("; ", r.RiskReasons)}");
            }
        }

        File.WriteAllText(outputPath, sb.ToString());
    }

    /// <summary>
    /// Generates a NON-DESTRUCTIVE dry-run script that only prints what would be done.
    /// No files are copied, no git operations are performed.
    /// </summary>
    public void WriteDryRunScript(ComparisonSummary summary, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine("# RepoCompare — DRY RUN SCRIPT (non-destructive)");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("#");
        sb.AppendLine("# This script ONLY PRINTS what would be done. No files are copied.");
        sb.AppendLine("# To actually apply changes, use: --apply-list <file>");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("set -euo pipefail");
        sb.AppendLine();
        sb.AppendLine($"SOURCE_DIR={EscapeBashDouble(summary.SourcePath)}");
        sb.AppendLine($"TARGET_DIR={EscapeBashDouble(summary.TargetPath)}");
        sb.AppendLine();

        // Safe to copy
        var safeToCopy = summary.Results
            .Where(r => r.Risk == RiskLevel.SafeToCopy && r.Status != FileStatus.Identical)
            .OrderBy(r => r.RelativePath).ToList();

        if (safeToCopy.Count > 0)
        {
            sb.AppendLine($"echo \"\"");
            sb.AppendLine($"echo \"=== SAFE TO COPY ({safeToCopy.Count} files) ===\"");
            sb.AppendLine($"echo \"These files can be safely copied from source to target:\"");
            sb.AppendLine($"echo \"\"");
            foreach (var f in safeToCopy)
            {
                sb.AppendLine($"echo \"  ✅ {EscapeBashEcho(f.RelativePath)} [{f.Category}]\"");
            }
        }

        // Review required
        var reviewRequired = summary.Results
            .Where(r => r.Risk == RiskLevel.ReviewRequired && r.Status != FileStatus.Identical)
            .OrderBy(r => r.RelativePath).ToList();

        if (reviewRequired.Count > 0)
        {
            sb.AppendLine($"echo \"\"");
            sb.AppendLine($"echo \"=== REVIEW REQUIRED ({reviewRequired.Count} files) ===\"");
            sb.AppendLine($"echo \"These files need manual review before copying:\"");
            sb.AppendLine($"echo \"\"");
            foreach (var f in reviewRequired)
            {
                sb.AppendLine($"echo \"  ⚠️  {EscapeBashEcho(f.RelativePath)} [{f.Category}] {EscapeBashEcho(string.Join("; ", f.RiskReasons))}\"");
            }
        }

        // High risk
        var highRisk = summary.Results
            .Where(r => r.Risk == RiskLevel.HighRisk)
            .OrderBy(r => r.RelativePath).ToList();

        if (highRisk.Count > 0)
        {
            sb.AppendLine($"echo \"\"");
            sb.AppendLine($"echo \"=== HIGH RISK ({highRisk.Count} files) ===\"");
            sb.AppendLine($"echo \"These files MUST be manually reviewed — DO NOT auto-copy:\"");
            sb.AppendLine($"echo \"\"");
            foreach (var f in highRisk)
            {
                sb.AppendLine($"echo \"  ❌ {EscapeBashEcho(f.RelativePath)} [{f.Category}] {EscapeBashEcho(string.Join("; ", f.RiskReasons))}\"");
            }
        }

        sb.AppendLine($"echo \"\"");
        sb.AppendLine($"echo \"═══════════════════════════════════════════\"");
        sb.AppendLine($"echo \"  DRY RUN COMPLETE — no files were modified\"");
        sb.AppendLine($"echo \"═══════════════════════════════════════════\"");
        sb.AppendLine($"echo \"\"");
        sb.AppendLine($"echo \"Next steps:\"");
        sb.AppendLine($"echo \"  1. Review report.json and high-risk-files.csv\"");
        sb.AppendLine($"echo \"  2. Edit container-only-files.txt to curate the apply list\"");
        sb.AppendLine($"echo \"  3. Re-run with: --apply-list container-only-files.txt\"");

        File.WriteAllText(outputPath, sb.ToString());
    }

    /// <summary>
    /// Generates an apply script that copies ONLY the files listed in the curated apply list.
    /// </summary>
    public void WriteApplyScript(ComparisonSummary summary, List<string> filesToApply, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine("# RepoCompare — APPLY SCRIPT (from curated file list)");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"# Files to apply: {filesToApply.Count}");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("set -euo pipefail");
        sb.AppendLine();
        sb.AppendLine($"SOURCE_DIR={EscapeBashDouble(summary.SourcePath)}");
        sb.AppendLine($"TARGET_DIR={EscapeBashDouble(summary.TargetPath)}");
        sb.AppendLine();
        sb.AppendLine("COPIED=0");
        sb.AppendLine("SKIPPED=0");
        sb.AppendLine();

        // Create directories first
        var dirs = filesToApply
            .Select(f => Path.GetDirectoryName(f)?.Replace('\\', '/'))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (dirs.Count > 0)
        {
            sb.AppendLine("# ── Create directories ──");
            foreach (var dir in dirs)
            {
                sb.AppendLine($"mkdir -p {EscapeBashDouble("$TARGET_DIR/" + dir!)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("# ── Copy files ──");
        foreach (var file in filesToApply)
        {
            var escaped = EscapeBashDouble(file);
            sb.AppendLine($"if [ -f {EscapeBashDouble("$SOURCE_DIR/" + file)} ]; then");
            sb.AppendLine($"  cp {EscapeBashDouble("$SOURCE_DIR/" + file)} {EscapeBashDouble("$TARGET_DIR/" + file)}");
            sb.AppendLine($"  echo \"  ✅ {EscapeBashEcho(file)}\"");
            sb.AppendLine("  COPIED=$((COPIED+1))");
            sb.AppendLine("else");
            sb.AppendLine($"  echo \"  ⚠️  SKIPPED (not found in source): {EscapeBashEcho(file)}\"");
            sb.AppendLine("  SKIPPED=$((SKIPPED+1))");
            sb.AppendLine("fi");
        }

        sb.AppendLine();
        sb.AppendLine("echo \"\"");
        sb.AppendLine("echo \"═══════════════════════════════════════════\"");
        sb.AppendLine("echo \"  Apply complete: $COPIED copied, $SKIPPED skipped\"");
        sb.AppendLine("echo \"═══════════════════════════════════════════\"");
        sb.AppendLine("echo \"\"");
        sb.AppendLine("echo \"Next steps:\"");
        sb.AppendLine("echo \"  1. cd \\\"$TARGET_DIR\\\"\"");
        sb.AppendLine("echo \"  2. dotnet build       # Verify .NET solution integrity\"");
        sb.AppendLine("echo \"  3. git diff --stat    # Review what changed\"");

        File.WriteAllText(outputPath, sb.ToString());
    }

    // ── Bash Escaping ─────────────────────────────────────────────

    /// <summary>
    /// Escapes a value for use inside double-quoted bash strings.
    /// Handles $, backtick, \, ", and ! correctly.
    /// </summary>
    private static string EscapeBashDouble(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`")
            .Replace("!", "\\!");
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Escapes a value for safe inclusion in an echo statement.
    /// </summary>
    private static string EscapeBashEcho(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`");
    }
}
