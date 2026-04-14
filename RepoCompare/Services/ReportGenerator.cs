using System.Text;
using RepoCompare.Models;

namespace RepoCompare.Services;

/// <summary>
/// Generates console output, Markdown reports, and executable sync/git scripts
/// from a ComparisonSummary.
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
        Console.WriteLine("║          RepoCompare — Branch Synchronization Analysis       ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"  📁 Source: {summary.SourcePath}");
        Console.WriteLine($"     Label:  {summary.SourceLabel}");
        Console.WriteLine($"  📁 Target: {summary.TargetPath}");
        Console.WriteLine($"     Label:  {summary.TargetLabel}");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  ════════════════════════════════════════════════════════");
        Console.WriteLine("  📊 Results Summary");
        Console.WriteLine("  ════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("  Total files scanned:       ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{summary.TotalFiles,6}");
        Console.ResetColor();

        Console.Write("  ✅ Identical (noise):      ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"{summary.IdenticalCount,6}");
        Console.ResetColor();

        Console.Write("  🔄 Modified (real diff):   ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{summary.ModifiedCount,6}");
        Console.ResetColor();

        Console.Write("  ➕ Only in Source:          ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"{summary.OnlyInSourceCount,6}");
        Console.ResetColor();

        Console.Write("  ➕ Only in Target:          ");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"{summary.OnlyInTargetCount,6}");
        Console.ResetColor();

        Console.WriteLine();

        // Modified files breakdown by extension
        if (summary.ModifiedCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  🔄 MODIFIED FILES (genuine differences — bring these)");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();

            int idx = 1;
            foreach (var file in summary.Modified.OrderBy(f => f.RelativePath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"   {idx,3}. ");
                Console.ResetColor();
                Console.Write(file.RelativePath);
                if (file.IsBinary)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(" [binary]");
                    Console.ResetColor();
                }
                Console.WriteLine();
                idx++;
            }
            Console.WriteLine();
        }

        // Files only in source
        if (summary.OnlyInSourceCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  ➕ ONLY IN SOURCE (need to bring over)");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();

            int idx = 1;
            foreach (var file in summary.OnlyInSource.OrderBy(f => f.RelativePath))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"   {idx,3}. ");
                Console.ResetColor();
                Console.WriteLine(file.RelativePath);
                idx++;
            }
            Console.WriteLine();
        }

        // Files only in target (preserve)
        if (summary.OnlyInTargetCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.WriteLine("  ➕ ONLY IN TARGET (keep — your new work)");
            Console.WriteLine("  ════════════════════════════════════════════════════════");
            Console.ResetColor();

            int idx = 1;
            foreach (var file in summary.OnlyInTarget.OrderBy(f => f.RelativePath))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($"   {idx,3}. ");
                Console.ResetColor();
                Console.WriteLine(file.RelativePath);
                idx++;
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Generates a comprehensive Markdown report with full diffs.
    /// </summary>
    public string GenerateMarkdownReport(ComparisonSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# RepoCompare — Branch Synchronization Report");
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
        sb.AppendLine();

        // ── Modified by extension ──
        if (summary.ModifiedCount > 0)
        {
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

        // ── Modified files with full diffs ──
        if (summary.ModifiedCount > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## 🔄 Modified Files — Full Diffs");
            sb.AppendLine();
            sb.AppendLine("> These files have genuine content differences after normalizing ");
            sb.AppendLine("> line endings, BOM markers, and trailing whitespace.");
            sb.AppendLine();

            int idx = 1;
            foreach (var file in summary.Modified.OrderBy(f => f.RelativePath))
            {
                sb.AppendLine($"### {idx}. `{file.RelativePath}`");
                sb.AppendLine();
                sb.AppendLine($"| | Source | Target |");
                sb.AppendLine($"|---|---|---|");
                sb.AppendLine($"| Size | {FormatBytes(file.SourceSizeBytes)} | {FormatBytes(file.TargetSizeBytes)} |");
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
            sb.AppendLine("## ➕ Files Only in Source (need to bring over)");
            sb.AppendLine();
            sb.AppendLine("| # | File | Size |");
            sb.AppendLine("|---:|---|---:|");
            int idx = 1;
            foreach (var file in summary.OnlyInSource.OrderBy(f => f.RelativePath))
            {
                sb.AppendLine($"| {idx} | `{file.RelativePath}` | {FormatBytes(file.SourceSizeBytes)} |");
                idx++;
            }
            sb.AppendLine();
        }

        // ── Files only in target ──
        if (summary.OnlyInTargetCount > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## ➕ Files Only in Target (preserve — your new work)");
            sb.AppendLine();
            sb.AppendLine("| # | File | Size |");
            sb.AppendLine("|---:|---|---:|");
            int idx = 1;
            foreach (var file in summary.OnlyInTarget.OrderBy(f => f.RelativePath))
            {
                sb.AppendLine($"| {idx} | `{file.RelativePath}` | {FormatBytes(file.TargetSizeBytes)} |");
                idx++;
            }
            sb.AppendLine();
        }

        // ── Action items ──
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## ✅ Recommended Actions");
        sb.AppendLine();
        sb.AppendLine("1. **Review Modified files** above — examine each diff to confirm they are genuine business logic changes.");
        sb.AppendLine("2. **Run `sync_changes.sh`** — copies modified + source-only files to your target working directory.");
        sb.AppendLine("3. **Run `dotnet build`** — verify .NET solution integrity after applying changes.");
        sb.AppendLine("4. **Run `git_workflow.sh`** — creates a feature branch, commits, and pushes for a clean PR.");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Generates a bash script to copy truly changed files from source to target.
    /// </summary>
    public string GenerateSyncScript(ComparisonSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine("# RepoCompare — Sync Script");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("#");
        sb.AppendLine("# This script copies only the truly changed files from source");
        sb.AppendLine("# (source) to your target working directory.");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("set -euo pipefail");
        sb.AppendLine();
        sb.AppendLine($"SOURCE_DIR=\"{EscapeBash(summary.SourcePath)}\"");
        sb.AppendLine($"TARGET_DIR=\"{EscapeBash(summary.TargetPath)}\"");
        sb.AppendLine();

        // Modified files
        var modifiedFiles = summary.Modified.OrderBy(f => f.RelativePath).ToList();
        if (modifiedFiles.Count > 0)
        {
            sb.AppendLine("# ── MODIFIED FILES ──────────────────────────────────");
            sb.AppendLine($"# {modifiedFiles.Count} files with genuine content differences.");
            sb.AppendLine("# WARNING: This overwrites the target version with the source version.");
            sb.AppendLine("# Review the comparison report to ensure this is what you want.");
            sb.AppendLine();
            sb.AppendLine("echo \"\"");
            sb.AppendLine("echo \"=== Copying MODIFIED files ===\"");
            sb.AppendLine();
            foreach (var file in modifiedFiles)
            {
                var escaped = EscapeBash(file.RelativePath);
                sb.AppendLine($"echo \"  → {escaped}\"");
                sb.AppendLine($"cp \"$SOURCE_DIR/{escaped}\" \"$TARGET_DIR/{escaped}\"");
            }
            sb.AppendLine();
        }

        // Files only in source
        var sourceOnlyFiles = summary.OnlyInSource.OrderBy(f => f.RelativePath).ToList();
        if (sourceOnlyFiles.Count > 0)
        {
            sb.AppendLine("# ── FILES ONLY IN SOURCE ───────────────────────────");
            sb.AppendLine($"# {sourceOnlyFiles.Count} files that exist in source but not in target.");
            sb.AppendLine("# These will be added to the target.");
            sb.AppendLine();
            sb.AppendLine("echo \"\"");
            sb.AppendLine("echo \"=== Copying files ONLY IN SOURCE ===\"");
            sb.AppendLine();

            // Create directories first
            var dirs = sourceOnlyFiles
                .Select(f => Path.GetDirectoryName(f.RelativePath)?.Replace('\\', '/'))
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .OrderBy(d => d);

            foreach (var dir in dirs)
            {
                sb.AppendLine($"mkdir -p \"$TARGET_DIR/{EscapeBash(dir!)}\"");
            }
            sb.AppendLine();

            foreach (var file in sourceOnlyFiles)
            {
                var escaped = EscapeBash(file.RelativePath);
                sb.AppendLine($"echo \"  + {escaped}\"");
                sb.AppendLine($"cp \"$SOURCE_DIR/{escaped}\" \"$TARGET_DIR/{escaped}\"");
            }
            sb.AppendLine();
        }

        sb.AppendLine("echo \"\"");
        sb.AppendLine("echo \"═══════════════════════════════════════════════\"");
        sb.AppendLine($"echo \"  ✅ Sync complete: {modifiedFiles.Count} modified + {sourceOnlyFiles.Count} new files copied.\"");
        sb.AppendLine("echo \"═══════════════════════════════════════════════\"");
        sb.AppendLine("echo \"\"");
        sb.AppendLine("echo \"Next steps:\"");
        sb.AppendLine("echo \"  1. cd \\\"$TARGET_DIR\\\"\"");
        sb.AppendLine("echo \"  2. dotnet build       # Verify .NET solution integrity\"");
        sb.AppendLine("echo \"  3. git diff --stat    # Review what changed\"");
        sb.AppendLine("echo \"  4. Run git_workflow.sh to create a clean PR\"");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Generates a git workflow script to create a feature branch and PR.
    /// </summary>
    public string GenerateGitWorkflowScript(ComparisonSummary summary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine("# RepoCompare — Git Workflow Script");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("#");
        sb.AppendLine("# Creates a feature branch with only the truly changed files");
        sb.AppendLine("# and prepares it for a clean Pull Request.");
        sb.AppendLine("# ═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("set -euo pipefail");
        sb.AppendLine();
        sb.AppendLine($"TARGET_DIR=\"{EscapeBash(summary.TargetPath)}\"");
        sb.AppendLine("BRANCH_NAME=\"feature/sync-source-branch-changes\"");
        sb.AppendLine();
        sb.AppendLine("cd \"$TARGET_DIR\"");
        sb.AppendLine();
        sb.AppendLine("# ── Step 1: Ensure we're on main and up-to-date ──");
        sb.AppendLine("echo \"=== Step 1: Updating main ===\"");
        sb.AppendLine("git checkout main");
        sb.AppendLine("git pull origin main");
        sb.AppendLine();
        sb.AppendLine("# ── Step 2: Create feature branch ──");
        sb.AppendLine("echo \"=== Step 2: Creating feature branch ===\"");
        sb.AppendLine("git checkout -b \"$BRANCH_NAME\"");
        sb.AppendLine();
        sb.AppendLine("# ── Step 3: Apply the sync script ──");
        sb.AppendLine("echo \"=== Step 3: Applying sync changes ===\"");

        var scriptDir = Path.GetDirectoryName(summary.TargetPath) ?? ".";
        sb.AppendLine($"bash \"{EscapeBash(scriptDir)}/sync_changes.sh\"");
        sb.AppendLine();
        sb.AppendLine("# ── Step 4: Verify .NET solution builds ──");
        sb.AppendLine("echo \"=== Step 4: Verifying build ===\"");
        sb.AppendLine("dotnet build || {");
        sb.AppendLine("  echo \"❌ Build failed! Review changes before committing.\"");
        sb.AppendLine("  exit 1");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("# ── Step 5: Stage and commit ──");
        sb.AppendLine("echo \"=== Step 5: Committing changes ===\"");
        sb.AppendLine("git add -A");

        int modCount = summary.ModifiedCount;
        int srcOnlyCount = summary.OnlyInSourceCount;
        int totalActionable = modCount + srcOnlyCount;

        sb.AppendLine($"git commit -m \"sync: bring business logic changes from source branch");
        sb.AppendLine();
        sb.AppendLine($"Synchronized {totalActionable} files with genuine business logic changes from");
        sb.AppendLine("the source branch. Whitespace/encoding noise");
        sb.AppendLine("was excluded using RepoCompare analysis tool.");
        sb.AppendLine();
        sb.AppendLine($"Modified: {modCount} files");
        sb.AppendLine($"Added:    {srcOnlyCount} files");
        sb.AppendLine("\"");
        sb.AppendLine();
        sb.AppendLine("# ── Step 6: Push ──");
        sb.AppendLine("echo \"=== Step 6: Pushing branch ===\"");
        sb.AppendLine("git push origin \"$BRANCH_NAME\"");
        sb.AppendLine();
        sb.AppendLine("echo \"\"");
        sb.AppendLine("echo \"═══════════════════════════════════════════════════════════\"");
        sb.AppendLine("echo \"  ✅ Branch pushed! Create a PR on GitHub:\"");
        sb.AppendLine("echo \"     $BRANCH_NAME → main\"");
        sb.AppendLine($"echo \"     The PR will show only {totalActionable} truly changed files.\"");
        sb.AppendLine("echo \"═══════════════════════════════════════════════════════════\"");
        sb.AppendLine();

        return sb.ToString();
    }

    // ── Helpers ─────────────────────────────────────────────────

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

    private static string EscapeBash(string value)
    {
        return value.Replace("'", "'\\''");
    }
}
