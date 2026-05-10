using System.Text;
using RepoCompare.Models;

namespace RepoCompare.Services;

/// <summary>
/// Generates console output and Markdown reports from a ComparisonSummary.
/// No destructive scripts are generated — use OutputGenerator for those.
/// </summary>
public class ReportGenerator
{
    /// <summary>
    /// Prints the summary to the console with colored output.
    /// </summary>
    public void PrintConsoleSummary(ComparisonSummary summary)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║      RepoCompare — Migration Analysis Report                 ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"  📁 Source: {summary.SourcePath}");
        Console.WriteLine($"     Label:  {summary.SourceLabel}");
        Console.WriteLine($"  📁 Target: {summary.TargetPath}");
        Console.WriteLine($"     Label:  {summary.TargetLabel}");
        Console.WriteLine();

        // ── File Status Summary ──
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  ════════════════════════════════════════════════════════");
        Console.WriteLine("  📊 File Status Summary");
        Console.WriteLine("  ════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();

        PrintCount("Total files scanned:       ", summary.TotalFiles, ConsoleColor.White);
        PrintCount("✅ Identical (noise):      ", summary.IdenticalCount, ConsoleColor.DarkGray);
        PrintCount("🔄 Modified (real diff):   ", summary.ModifiedCount, ConsoleColor.Yellow);
        PrintCount("➕ Only in Source:          ", summary.OnlyInSourceCount, ConsoleColor.Green);
        PrintCount("➕ Only in Target:          ", summary.OnlyInTargetCount, ConsoleColor.Blue);
        Console.WriteLine();

        // ── Risk Summary ──
        var riskCounts = summary.CountByRisk;
        if (riskCounts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  🎯 Risk Assessment");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            if (riskCounts.TryGetValue(RiskLevel.SafeToCopy, out int safe))
                PrintCount("✅ Safe to copy:           ", safe, ConsoleColor.Green);
            if (riskCounts.TryGetValue(RiskLevel.ReviewRequired, out int review))
                PrintCount("⚠️  Review required:       ", review, ConsoleColor.Yellow);
            if (riskCounts.TryGetValue(RiskLevel.HighRisk, out int high))
                PrintCount("❌ High risk:              ", high, ConsoleColor.Red);
            Console.WriteLine();
        }

        // ── Category Summary ──
        var categoryCounts = summary.CountByCategory;
        if (categoryCounts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  📂 Change Categories");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var kvp in categoryCounts.OrderByDescending(k => k.Value))
            {
                var emoji = kvp.Key switch
                {
                    ChangeCategory.ContainerSpecific => "🐳",
                    ChangeCategory.LinuxMigration => "🐧",
                    ChangeCategory.BusinessLogic => "💼",
                    ChangeCategory.Config => "⚙️ ",
                    ChangeCategory.Test => "🧪",
                    ChangeCategory.BuildInfra => "🏗️ ",
                    _ => "❓"
                };
                PrintCount($"{emoji} {kvp.Key,-24}", kvp.Value, ConsoleColor.Cyan);
            }
            Console.WriteLine();
        }

        // ── Case Collisions ──
        if (summary.CaseCollisions.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine($"  ⚠️  CASE COLLISIONS ({summary.CaseCollisions.Count} detected)");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var c in summary.CaseCollisions)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("   ❌ ");
                Console.ResetColor();
                Console.WriteLine($"{c.Path1}");
                Console.Write("      vs ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{c.Path2}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"      [{c.Side}] {c.Impact}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // ── Linux Issues Summary ──
        if (summary.AllLinuxIssues.Count > 0)
        {
            var bySeverity = summary.AllLinuxIssues.GroupBy(i => i.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine($"  🐧 Linux Compatibility Issues ({summary.AllLinuxIssues.Count} total)");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            if (bySeverity.TryGetValue("High", out int highSev))
                PrintCount("🔴 High:                  ", highSev, ConsoleColor.Red);
            if (bySeverity.TryGetValue("Medium", out int medSev))
                PrintCount("🟡 Medium:                ", medSev, ConsoleColor.Yellow);
            if (bySeverity.TryGetValue("Low", out int lowSev))
                PrintCount("🔵 Low:                   ", lowSev, ConsoleColor.Cyan);
            if (bySeverity.TryGetValue("Info", out int infoSev))
                PrintCount("⚪ Info:                  ", infoSev, ConsoleColor.DarkGray);
            Console.WriteLine();

            // Show top 10 high-severity issues
            var topIssues = summary.AllLinuxIssues
                .Where(i => i.Severity == "High")
                .Take(10)
                .ToList();

            if (topIssues.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Top high-severity issues:");
                Console.ResetColor();
                foreach (var issue in topIssues)
                {
                    Console.Write("    ");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"[{issue.IssueType}] ");
                    Console.ResetColor();
                    Console.Write($"{issue.FilePath}");
                    if (issue.LineNumber > 0)
                        Console.Write($":{issue.LineNumber}");
                    Console.WriteLine();
                }
                if (summary.AllLinuxIssues.Count(i => i.Severity == "High") > 10)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    ... and {summary.AllLinuxIssues.Count(i => i.Severity == "High") - 10} more. See linux-issues.csv for full list.");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
        }

        // ── High Risk Files ──
        var highRiskFiles = summary.HighRisk.OrderBy(f => f.RelativePath).ToList();
        if (highRiskFiles.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine($"  ❌ HIGH RISK FILES ({highRiskFiles.Count}) — manual review required");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            int idx = 1;
            foreach (var file in highRiskFiles.Take(20))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"   {idx,3}. ");
                Console.ResetColor();
                Console.Write(file.RelativePath);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" [{file.Category}]");
                Console.ResetColor();
                Console.WriteLine();
                idx++;
            }
            if (highRiskFiles.Count > 20)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"        ... and {highRiskFiles.Count - 20} more. See high-risk-files.csv.");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // ── Modified Files ──
        if (summary.ModifiedCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  🔄 MODIFIED FILES (genuine differences)");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();

            int idx = 1;
            foreach (var file in summary.Modified.OrderBy(f => f.Risk).ThenBy(f => f.RelativePath))
            {
                var riskIcon = file.Risk switch
                {
                    RiskLevel.SafeToCopy => "✅",
                    RiskLevel.ReviewRequired => "⚠️ ",
                    RiskLevel.HighRisk => "❌",
                    _ => "❓"
                };
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"   {idx,3}. ");
                Console.ResetColor();
                Console.Write($"{riskIcon} {file.RelativePath}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" [{file.Category}]");
                if (file.IsBinary) Console.Write(" [binary]");
                Console.ResetColor();
                Console.WriteLine();
                idx++;
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Generates a comprehensive Markdown report.
    /// </summary>
    public string GenerateMarkdownReport(ComparisonSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# RepoCompare — Migration Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // ── Summary table ──
        sb.AppendLine("## 📊 Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| **Source** | `{summary.SourceLabel}` |");
        sb.AppendLine($"| **Target** | `{summary.TargetLabel}` |");
        sb.AppendLine($"| Total files scanned | {summary.TotalFiles} |");
        sb.AppendLine($"| ✅ Identical (noise) | {summary.IdenticalCount} |");
        sb.AppendLine($"| 🔄 Modified (real diff) | {summary.ModifiedCount} |");
        sb.AppendLine($"| ➕ Only in Source | {summary.OnlyInSourceCount} |");
        sb.AppendLine($"| ➕ Only in Target | {summary.OnlyInTargetCount} |");
        sb.AppendLine($"| ⚠️ Case Collisions | {summary.CaseCollisions.Count} |");
        sb.AppendLine($"| 🐧 Linux Issues | {summary.AllLinuxIssues.Count} |");
        sb.AppendLine();

        // ── Risk breakdown ──
        var riskCounts = summary.CountByRisk;
        if (riskCounts.Count > 0)
        {
            sb.AppendLine("### 🎯 Risk Assessment");
            sb.AppendLine();
            sb.AppendLine("| Risk Level | Count |");
            sb.AppendLine("|---|---:|");
            foreach (var kvp in riskCounts.OrderBy(k => k.Key))
            {
                var icon = kvp.Key switch
                {
                    RiskLevel.SafeToCopy => "✅",
                    RiskLevel.ReviewRequired => "⚠️",
                    RiskLevel.HighRisk => "❌",
                    _ => "❓"
                };
                sb.AppendLine($"| {icon} {kvp.Key} | {kvp.Value} |");
            }
            sb.AppendLine();
        }

        // ── Category breakdown ──
        var categoryCounts = summary.CountByCategory;
        if (categoryCounts.Count > 0)
        {
            sb.AppendLine("### 📂 Change Categories");
            sb.AppendLine();
            sb.AppendLine("| Category | Count |");
            sb.AppendLine("|---|---:|");
            foreach (var kvp in categoryCounts.OrderByDescending(k => k.Value))
            {
                sb.AppendLine($"| {kvp.Key} | {kvp.Value} |");
            }
            sb.AppendLine();
        }

        // ── Case Collisions ──
        if (summary.CaseCollisions.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## ⚠️ Case Collisions");
            sb.AppendLine();
            sb.AppendLine("> **CRITICAL for Linux containers:** These paths differ only by casing.");
            sb.AppendLine("> On Windows they refer to the same file; on Linux they are different files.");
            sb.AppendLine();
            sb.AppendLine("| Path 1 | Path 2 | Side |");
            sb.AppendLine("|---|---|---|");
            foreach (var c in summary.CaseCollisions)
            {
                sb.AppendLine($"| `{c.Path1}` | `{c.Path2}` | {c.Side} |");
            }
            sb.AppendLine();
        }

        // ── Linux Issues ──
        if (summary.AllLinuxIssues.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 🐧 Linux Compatibility Issues");
            sb.AppendLine();

            var bySeverity = summary.AllLinuxIssues.GroupBy(i => i.Severity)
                .OrderBy(g => g.Key == "High" ? 0 : g.Key == "Medium" ? 1 : 2);

            foreach (var group in bySeverity)
            {
                sb.AppendLine($"### {group.Key} Severity ({group.Count()})");
                sb.AppendLine();
                sb.AppendLine("| File | Line | Type | Description |");
                sb.AppendLine("|---|---:|---|---|");
                foreach (var issue in group.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).Take(50))
                {
                    var lineStr = issue.LineNumber > 0 ? issue.LineNumber.ToString() : "-";
                    var desc = issue.Description.Length > 80
                        ? issue.Description[..77] + "..."
                        : issue.Description;
                    sb.AppendLine($"| `{issue.FilePath}` | {lineStr} | {issue.IssueType} | {desc} |");
                }
                if (group.Count() > 50)
                    sb.AppendLine($"\n*... and {group.Count() - 50} more. See `linux-issues.csv` for full list.*");
                sb.AppendLine();
            }
        }

        // ── Modified by extension ──
        if (summary.ModifiedCount > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("### Modified by Extension");
            sb.AppendLine();
            sb.AppendLine("| Extension | Count |");
            sb.AppendLine("|---|---:|");
            foreach (var kvp in summary.ModifiedByExtension.OrderByDescending(k => k.Value))
            {
                sb.AppendLine($"| `{kvp.Key}` | {kvp.Value} |");
            }
            sb.AppendLine();
        }

        // ── Modified files with diffs ──
        if (summary.ModifiedCount > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 🔄 Modified Files — Full Diffs");
            sb.AppendLine();

            int idx = 1;
            foreach (var file in summary.Modified
                .OrderBy(f => f.Risk)
                .ThenBy(f => f.Category)
                .ThenBy(f => f.RelativePath))
            {
                var riskBadge = file.Risk switch
                {
                    RiskLevel.SafeToCopy => "✅ Safe",
                    RiskLevel.ReviewRequired => "⚠️ Review",
                    RiskLevel.HighRisk => "❌ High Risk",
                    _ => "❓"
                };

                sb.AppendLine($"### {idx}. `{file.RelativePath}`");
                sb.AppendLine();
                sb.AppendLine($"| | Value |");
                sb.AppendLine($"|---|---|");
                sb.AppendLine($"| Category | {file.Category} |");
                sb.AppendLine($"| Risk | {riskBadge} |");
                sb.AppendLine($"| Source Size | {FormatBytes(file.SourceSizeBytes)} |");
                sb.AppendLine($"| Target Size | {FormatBytes(file.TargetSizeBytes)} |");
                if (file.RiskReasons.Count > 0)
                    sb.AppendLine($"| Risk Reasons | {string.Join("; ", file.RiskReasons)} |");
                sb.AppendLine();

                if (file.IsBinary)
                {
                    sb.AppendLine("> ⚠️ **Binary file** — content differs but cannot display inline diff.");
                }
                else if (!string.IsNullOrEmpty(file.UnifiedDiff))
                {
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Click to expand diff</summary>");
                    sb.AppendLine();
                    sb.AppendLine("```diff");
                    sb.AppendLine(file.UnifiedDiff);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    sb.AppendLine("</details>");
                }
                sb.AppendLine();
                idx++;
            }
        }

        // ── Files only in source ──
        if (summary.OnlyInSourceCount > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## ➕ Files Only in Source");
            sb.AppendLine();
            sb.AppendLine("| # | File | Category | Risk | Size |");
            sb.AppendLine("|---:|---|---|---|---:|");
            int idx = 1;
            foreach (var file in summary.OnlyInSource
                .OrderBy(f => f.Risk).ThenBy(f => f.RelativePath))
            {
                sb.AppendLine($"| {idx} | `{file.RelativePath}` | {file.Category} | {file.Risk} | {FormatBytes(file.SourceSizeBytes)} |");
                idx++;
            }
            sb.AppendLine();
        }

        // ── Files only in target ──
        if (summary.OnlyInTargetCount > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## ➕ Files Only in Target");
            sb.AppendLine();
            sb.AppendLine("| # | File | Category | Risk | Size |");
            sb.AppendLine("|---:|---|---|---|---:|");
            int idx = 1;
            foreach (var file in summary.OnlyInTarget
                .OrderBy(f => f.Risk).ThenBy(f => f.RelativePath))
            {
                sb.AppendLine($"| {idx} | `{file.RelativePath}` | {file.Category} | {file.Risk} | {FormatBytes(file.TargetSizeBytes)} |");
                idx++;
            }
            sb.AppendLine();
        }

        // ── Next Steps ──
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## ✅ Recommended Next Steps");
        sb.AppendLine();
        sb.AppendLine("1. **Review `high-risk-files.csv`** — these files need manual attention.");
        sb.AppendLine("2. **Review `linux-issues.csv`** — fix Windows-specific patterns before container deployment.");
        sb.AppendLine("3. **Review `container-only-files.txt`** — curate the list of files safe to apply.");
        sb.AppendLine("4. **Run the dry-run script** (`bash dry-run.sh`) to preview what would happen.");
        sb.AppendLine("5. **Apply curated changes** — re-run with `--apply-list container-only-files.txt`.");
        sb.AppendLine("6. **Build and test** — `dotnet build` to verify .NET solution integrity.");
        sb.AppendLine();

        return sb.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static void PrintCount(string label, int count, ConsoleColor color)
    {
        Console.Write($"  {label}");
        Console.ForegroundColor = color;
        Console.WriteLine($"{count,6}");
        Console.ResetColor();
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.#} {sizes[order]}";
    }
}
