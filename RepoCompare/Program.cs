using RepoCompare.Services;

namespace RepoCompare;

/// <summary>
/// RepoCompare — Non-Destructive Migration Analysis Tool
///
/// Compares two directory trees (checked-out branches) and identifies truly
/// changed files by normalizing away encoding, BOM, and whitespace noise.
/// Classifies changes by category (Container/Business/Config/etc.) and risk level.
/// Detects Linux-incompatibility issues and case collisions.
///
/// Usage:
///   dotnet run -- --source /path/to/source --target /path/to/target [options]
///
/// Options:
///   --source, -s        Path to the source directory (e.g., container-broken checkout)
///   --target, -t        Path to the target directory (e.g., old-main checkout)
///   --source-label      Label for source (default: "Source")
///   --target-label      Label for target (default: "Target")
///   --output-dir, -o    Directory for all output files (default: ./output)
///   --known-files, -k   Path to a file listing specific files to compare (one per line).
///                       If empty or not provided, all files are compared.
///   --apply-list        Path to a curated file list; generates an apply script for only those files
///   --no-json           Don't generate report.json
///   --no-csv            Don't generate CSV files
///   --verbose, -v       Show detailed progress during comparison
///   --help, -h          Show this help message
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        var options = ParseArguments(args);

        if (options.ShowHelp || string.IsNullOrEmpty(options.SourcePath) || string.IsNullOrEmpty(options.TargetPath))
        {
            PrintUsage();
            return options.ShowHelp ? 0 : 1;
        }

        // ── Validate Paths ──
        if (!Directory.Exists(options.SourcePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ❌ Source directory not found: {options.SourcePath}");
            Console.ResetColor();
            return 1;
        }

        if (!Directory.Exists(options.TargetPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ❌ Target directory not found: {options.TargetPath}");
            Console.ResetColor();
            return 1;
        }

        // Resolve to absolute paths
        options.SourcePath = Path.GetFullPath(options.SourcePath);
        options.TargetPath = Path.GetFullPath(options.TargetPath);

        try
        {
            return RunComparison(options);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ❌ Fatal error: {ex.Message}");
            Console.ResetColor();

            if (options.Verbose)
            {
                Console.WriteLine();
                Console.WriteLine(ex.StackTrace);
            }

            return 1;
        }
    }

    private static int RunComparison(Options options)
    {
        // ── Setup components ──
        var scanner = new DirectoryScanner();
        var comparer = new FileComparer(scanner, verbose: true);
        var reporter = new ReportGenerator();
        var outputGen = new OutputGenerator();

        // ── Load known-files filter (if provided) ──
        HashSet<string>? knownFiles = null;
        if (!string.IsNullOrEmpty(options.KnownFilesPath))
        {
            if (!File.Exists(options.KnownFilesPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  ❌ Known-files list not found: {options.KnownFilesPath}");
                Console.ResetColor();
                return 1;
            }

            var lines = File.ReadAllLines(options.KnownFilesPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Trim().Replace('\\', '/'))
                .ToList();

            if (lines.Count > 0)
            {
                knownFiles = new HashSet<string>(lines, StringComparer.Ordinal);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n  📋 Known-files filter active: {knownFiles.Count} files loaded from {options.KnownFilesPath}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n  ⚠️  Known-files list is empty — comparing all files");
                Console.ResetColor();
            }
        }

        // ── Run comparison ──
        var summary = comparer.Compare(
            options.SourcePath!,
            options.TargetPath!,
            options.SourceLabel,
            options.TargetLabel,
            knownFiles);

        // ── Print console summary ──
        reporter.PrintConsoleSummary(summary);

        // ── Create output directory ──
        var outputDir = Path.GetFullPath(options.OutputDir);
        Directory.CreateDirectory(outputDir);

        // ── Write Markdown report ──
        var reportPath = Path.Combine(outputDir, "comparison_report.md");
        var reportContent = reporter.GenerateMarkdownReport(summary);
        File.WriteAllText(reportPath, reportContent);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  📄 Markdown report: {reportPath}");
        Console.ResetColor();

        // ── Write structured outputs ──
        bool hasApplyList = !string.IsNullOrEmpty(options.ApplyListPath);

        if (options.EmitJson || options.EmitCsv)
        {
            outputGen.WriteAll(
                summary,
                outputDir,
                includeApplyList: hasApplyList,
                applyListPath: options.ApplyListPath);

            Console.ForegroundColor = ConsoleColor.Cyan;
            if (options.EmitJson)
                Console.WriteLine($"  📊 JSON report:    {Path.Combine(outputDir, "report.json")}");
            if (options.EmitCsv)
            {
                Console.WriteLine($"  ⚠️  High-risk CSV:  {Path.Combine(outputDir, "high-risk-files.csv")}");
                Console.WriteLine($"  🐧 Linux issues:   {Path.Combine(outputDir, "linux-issues.csv")}");
                Console.WriteLine($"  🐳 Container list: {Path.Combine(outputDir, "container-only-files.txt")}");
            }
            Console.WriteLine($"  📜 Dry-run script: {Path.Combine(outputDir, "dry-run.sh")}");
            if (hasApplyList)
                Console.WriteLine($"  ✅ Apply script:   {Path.Combine(outputDir, "apply-changes.sh")}");
            Console.ResetColor();
        }

        Console.WriteLine();

        // ── Return code: 0 if identical, 2 if changes found ──
        return summary.ModifiedCount + summary.OnlyInSourceCount > 0 ? 2 : 0;
    }

    // ── Argument Parsing ──────────────────────────────────────────

    private static Options ParseArguments(string[] args)
    {
        var options = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--source" or "-s":
                    if (i + 1 < args.Length) options.SourcePath = args[++i];
                    break;
                case "--target" or "-t":
                    if (i + 1 < args.Length) options.TargetPath = args[++i];
                    break;
                case "--source-label":
                    if (i + 1 < args.Length) options.SourceLabel = args[++i];
                    break;
                case "--target-label":
                    if (i + 1 < args.Length) options.TargetLabel = args[++i];
                    break;
                case "--output-dir" or "-o":
                    if (i + 1 < args.Length) options.OutputDir = args[++i];
                    break;
                case "--known-files" or "-k":
                    if (i + 1 < args.Length) options.KnownFilesPath = args[++i];
                    break;
                case "--apply-list":
                    if (i + 1 < args.Length) options.ApplyListPath = args[++i];
                    break;
                case "--no-json":
                    options.EmitJson = false;
                    break;
                case "--no-csv":
                    options.EmitCsv = false;
                    break;
                case "--verbose" or "-v":
                    options.Verbose = true;
                    break;
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                default:
                    // Treat positional args as source, then target
                    if (string.IsNullOrEmpty(options.SourcePath))
                        options.SourcePath = args[i];
                    else if (string.IsNullOrEmpty(options.TargetPath))
                        options.TargetPath = args[i];
                    break;
            }
        }

        return options;
    }

    private static void PrintUsage()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║      RepoCompare — Non-Destructive Migration Analysis        ║");
        Console.WriteLine("  ╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("  Compare two directory trees (checked-out branches) and identify");
        Console.WriteLine("  truly changed files, classify them by category and risk level,");
        Console.WriteLine("  and detect Linux container migration issues.");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  USAGE:");
        Console.ResetColor();
        Console.WriteLine("    dotnet run -- --source <path> --target <path> [options]");
        Console.WriteLine("    dotnet run -- <source_path> <target_path> [options]");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  REQUIRED:");
        Console.ResetColor();
        Console.WriteLine("    --source, -s <path>    Source directory (e.g., container-broken checkout)");
        Console.WriteLine("    --target, -t <path>    Target directory (e.g., old-main checkout)");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  OPTIONS:");
        Console.ResetColor();
        Console.WriteLine("    --source-label <text>  Label for source (default: \"Source\")");
        Console.WriteLine("    --target-label <text>  Label for target (default: \"Target\")");
        Console.WriteLine("    --output-dir, -o <dir> Output directory (default: ./output)");
        Console.WriteLine("    --known-files, -k <file> File listing specific files to compare (one per line).");
        Console.WriteLine("                             If empty or not specified, all files are compared.");
        Console.WriteLine("    --apply-list <file>    Curated file list → generate apply script");
        Console.WriteLine("    --no-json              Don't generate report.json");
        Console.WriteLine("    --no-csv               Don't generate CSV files");
        Console.WriteLine("    --verbose, -v          Show detailed progress");
        Console.WriteLine("    --help, -h             Show this help");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  WORKFLOW:");
        Console.ResetColor();
        Console.WriteLine("    1. Run comparison → review report.json + CSV files");
        Console.WriteLine("    2. Edit container-only-files.txt to curate the apply list");
        Console.WriteLine("    3. Re-run with --apply-list container-only-files.txt");
        Console.WriteLine("    4. Execute apply-changes.sh to copy only curated files");
        Console.WriteLine("    5. dotnet build to verify integrity");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  OUTPUTS:");
        Console.ResetColor();
        Console.WriteLine("    comparison_report.md   Detailed Markdown report with diffs");
        Console.WriteLine("    report.json            Machine-readable JSON report");
        Console.WriteLine("    high-risk-files.csv    Files needing manual review");
        Console.WriteLine("    linux-issues.csv       Linux-incompatibility issues");
        Console.WriteLine("    container-only-files.txt  Container-specific safe-to-copy files");
        Console.WriteLine("    dry-run.sh             Non-destructive preview script");
        Console.WriteLine("    apply-changes.sh       Apply script (only with --apply-list)");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  WHAT IT DOES:");
        Console.ResetColor();
        Console.WriteLine("    1. Scans both directories recursively (skips .git, bin, obj)");
        Console.WriteLine("    2. Case-SENSITIVE path matching (critical for Linux containers)");
        Console.WriteLine("    3. Detects case-only path collisions");
        Console.WriteLine("    4. Normalizes content: strips BOM, CRLF→LF, trims whitespace");
        Console.WriteLine("    5. Compares using SHA-256 of normalized content");
        Console.WriteLine("    6. Classifies: ContainerSpecific | LinuxMigration | BusinessLogic");
        Console.WriteLine("       | Config | Test | BuildInfra | Unknown");
        Console.WriteLine("    7. Assesses risk: SafeToCopy | ReviewRequired | HighRisk");
        Console.WriteLine("    8. Scans for Linux issues: Windows paths, Registry, CRLF in .sh");
        Console.WriteLine("    9. Generates unified diffs for modified files");
        Console.WriteLine("   10. Outputs: console summary, Markdown, JSON, CSV, scripts");
        Console.WriteLine();
    }

    private class Options
    {
        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public string SourceLabel { get; set; } = "Source";
        public string TargetLabel { get; set; } = "Target";
        public string OutputDir { get; set; } = "./output";
        public string? KnownFilesPath { get; set; }
        public string? ApplyListPath { get; set; }
        public bool EmitJson { get; set; } = true;
        public bool EmitCsv { get; set; } = true;
        public bool Verbose { get; set; } = false;
        public bool ShowHelp { get; set; } = false;
    }
}
