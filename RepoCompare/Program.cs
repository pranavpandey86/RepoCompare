using RepoCompare.Services;

namespace RepoCompare;

/// <summary>
/// RepoCompare — Git Branch Synchronization Analysis Tool
///
/// Compares two directory trees (checked-out branches) and identifies truly
/// changed files by normalizing away encoding, BOM, and whitespace noise.
///
/// Usage:
///   dotnet run -- --source /path/to/source --target /path/to/target [options]
///
/// Options:
///   --source, -s       Path to the source directory (e.g., old repo checkout)
///   --target, -t       Path to the target directory (e.g., main checkout)
///   --source-label     Label for source (default: "Source")
///   --target-label     Label for target (default: "Target")
///   --output, -o       Output path for the Markdown report (default: ./comparison_report.md)
///   --generate-script  Generate sync_changes.sh and git_workflow.sh (default: true)
///   --verbose, -v      Show detailed progress during comparison
///   --help, -h         Show this help message
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        // ── Parse Arguments ──
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
        var comparer = new FileComparer(scanner, verbose: true); // Always show progress
        var reporter = new ReportGenerator();

        // ── Run comparison ──
        var summary = comparer.Compare(
            options.SourcePath!,
            options.TargetPath!,
            options.SourceLabel,
            options.TargetLabel);

        // ── Print console summary ──
        reporter.PrintConsoleSummary(summary);

        // ── Write Markdown report ──
        var reportPath = options.OutputPath;
        var reportContent = reporter.GenerateMarkdownReport(summary);
        File.WriteAllText(reportPath, reportContent);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  📄 Report saved to: {reportPath}");
        Console.ResetColor();

        // ── Generate scripts ──
        if (options.GenerateScript)
        {
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? ".";

            // Sync script
            var syncPath = Path.Combine(outputDir, "sync_changes.sh");
            var syncContent = reporter.GenerateSyncScript(summary);
            File.WriteAllText(syncPath, syncContent);

            // Git workflow script
            var gitPath = Path.Combine(outputDir, "git_workflow.sh");
            var gitContent = reporter.GenerateGitWorkflowScript(summary);
            File.WriteAllText(gitPath, gitContent);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  📜 Sync script saved to: {syncPath}");
            Console.WriteLine($"  🔧 Git workflow saved to: {gitPath}");
            Console.ResetColor();
        }

        Console.WriteLine();

        // ── Return code: 0 if no changes needed, 1 if changes found ──
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
                case "--output" or "-o":
                    if (i + 1 < args.Length) options.OutputPath = args[++i];
                    break;
                case "--generate-script":
                    options.GenerateScript = true;
                    break;
                case "--no-script":
                    options.GenerateScript = false;
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
        Console.WriteLine("  ║          RepoCompare — Branch Synchronization Tool           ║");
        Console.WriteLine("  ╚═══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("  Compare two directory trees (checked-out branches) and identify");
        Console.WriteLine("  truly changed files, filtering out encoding/whitespace noise.");
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
        Console.WriteLine("    --source, -s <path>   Source directory (e.g., source branch checkout)");
        Console.WriteLine("    --target, -t <path>   Target directory (e.g., main checkout)");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  OPTIONS:");
        Console.ResetColor();
        Console.WriteLine("    --source-label <text>  Label for source (default: \"Source\")");
        Console.WriteLine("    --target-label <text>  Label for target (default: \"Target\")");
        Console.WriteLine("    --output, -o <path>    Report output path (default: ./comparison_report.md)");
        Console.WriteLine("    --generate-script      Generate sync + git scripts (default)");
        Console.WriteLine("    --no-script            Don't generate scripts");
        Console.WriteLine("    --verbose, -v          Show detailed progress");
        Console.WriteLine("    --help, -h             Show this help");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  EXAMPLE:");
        Console.ResetColor();
        Console.WriteLine("    dotnet run -- \\");
        Console.WriteLine("      --source /repos/old-repo \\");
        Console.WriteLine("      --target /repos/new-repo \\");
        Console.WriteLine("      --source-label \"OldRepo / feature-branch\" \\");
        Console.WriteLine("      --target-label \"NewRepo / main\" \\");
        Console.WriteLine("      --output ./comparison_report.md \\");
        Console.WriteLine("      --generate-script");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  WHAT IT DOES:");
        Console.ResetColor();
        Console.WriteLine("    1. Scans both directories recursively (skips .git, bin, obj)");
        Console.WriteLine("    2. Normalizes content: strips BOM, CRLF→LF, trims whitespace");
        Console.WriteLine("    3. Compares using SHA-256 of normalized content");
        Console.WriteLine("    4. Categorizes: IDENTICAL | MODIFIED | ONLY_IN_SOURCE | ONLY_IN_TARGET");
        Console.WriteLine("    5. Generates unified diffs for modified files");
        Console.WriteLine("    6. Outputs: console summary, Markdown report, sync script, git script");
        Console.WriteLine();
    }

    private class Options
    {
        public string? SourcePath { get; set; }
        public string? TargetPath { get; set; }
        public string SourceLabel { get; set; } = "Source";
        public string TargetLabel { get; set; } = "Target";
        public string OutputPath { get; set; } = "./comparison_report.md";
        public bool GenerateScript { get; set; } = true;
        public bool Verbose { get; set; } = false;
        public bool ShowHelp { get; set; } = false;
    }
}
