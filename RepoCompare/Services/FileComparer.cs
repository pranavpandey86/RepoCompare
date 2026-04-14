using RepoCompare.Models;
using RepoCompare.Utils;

namespace RepoCompare.Services;

/// <summary>
/// Core comparison engine. Compares all files between two directories,
/// normalizing content to filter out encoding/whitespace noise,
/// and categorizes each file as Identical, Modified, OnlyInSource, or OnlyInTarget.
/// </summary>
public class FileComparer
{
    private readonly DirectoryScanner _scanner;
    private readonly bool _verbose;

    public FileComparer(DirectoryScanner scanner, bool verbose = false)
    {
        _scanner = scanner;
        _verbose = verbose;
    }

    /// <summary>
    /// Compares all files between source and target directories.
    /// </summary>
    /// <param name="sourcePath">Absolute path to the source directory (e.g., source branch checkout).</param>
    /// <param name="targetPath">Absolute path to the target directory (e.g., main checkout).</param>
    /// <param name="sourceLabel">Human-readable label for the source.</param>
    /// <param name="targetLabel">Human-readable label for the target.</param>
    /// <returns>A ComparisonSummary with all results.</returns>
    public ComparisonSummary Compare(string sourcePath, string targetPath, string sourceLabel, string targetLabel)
    {
        var summary = new ComparisonSummary
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLabel = sourceLabel,
            TargetLabel = targetLabel
        };

        // ── Step 1: Scan both directories ──
        WriteProgress("Scanning source directory...");
        var sourceFiles = _scanner.Scan(sourcePath);
        WriteProgress($"  Found {sourceFiles.Count} files in source");

        WriteProgress("Scanning target directory...");
        var targetFiles = _scanner.Scan(targetPath);
        WriteProgress($"  Found {targetFiles.Count} files in target");

        // ── Step 2: Determine the union of all file paths ──
        var allFiles = new SortedSet<string>(sourceFiles, StringComparer.OrdinalIgnoreCase);
        allFiles.UnionWith(targetFiles);

        WriteProgress($"\nComparing {allFiles.Count} unique files...\n");

        int processed = 0;
        int total = allFiles.Count;

        // ── Step 3: Compare each file ──
        foreach (var relativePath in allFiles)
        {
            processed++;
            bool inSource = sourceFiles.Contains(relativePath);
            bool inTarget = targetFiles.Contains(relativePath);

            var result = new ComparisonResult { RelativePath = relativePath };

            if (inSource && !inTarget)
            {
                // File only exists in source
                result.Status = FileStatus.OnlyInSource;
                result.SourceSizeBytes = new FileInfo(Path.Combine(sourcePath, relativePath)).Length;
            }
            else if (!inSource && inTarget)
            {
                // File only exists in target
                result.Status = FileStatus.OnlyInTarget;
                result.TargetSizeBytes = new FileInfo(Path.Combine(targetPath, relativePath)).Length;
            }
            else
            {
                // File exists in both — compare content
                var sourceFilePath = Path.Combine(sourcePath, relativePath);
                var targetFilePath = Path.Combine(targetPath, relativePath);

                result.SourceSizeBytes = new FileInfo(sourceFilePath).Length;
                result.TargetSizeBytes = new FileInfo(targetFilePath).Length;

                // Check if binary
                if (ContentNormalizer.IsBinaryFile(sourceFilePath) || ContentNormalizer.IsBinaryFile(targetFilePath))
                {
                    result.IsBinary = true;
                    // For binary files, compare raw bytes
                    var sourceBytes = File.ReadAllBytes(sourceFilePath);
                    var targetBytes = File.ReadAllBytes(targetFilePath);
                    result.Status = sourceBytes.AsSpan().SequenceEqual(targetBytes.AsSpan())
                        ? FileStatus.Identical
                        : FileStatus.Modified;
                }
                else
                {
                    // Text file: normalize then compare by hash
                    var sourceNormalized = ContentNormalizer.ReadAndNormalize(sourceFilePath);
                    var targetNormalized = ContentNormalizer.ReadAndNormalize(targetFilePath);

                    var sourceHash = ContentNormalizer.ComputeHash(sourceNormalized);
                    var targetHash = ContentNormalizer.ComputeHash(targetNormalized);

                    if (sourceHash == targetHash)
                    {
                        result.Status = FileStatus.Identical;
                    }
                    else
                    {
                        result.Status = FileStatus.Modified;

                        // Generate unified diff
                        var sourceLines = ContentNormalizer.SplitLines(sourceNormalized);
                        var targetLines = ContentNormalizer.SplitLines(targetNormalized);
                        var diffLines = DiffEngine.ComputeDiff(sourceLines, targetLines);
                        result.UnifiedDiff = DiffEngine.FormatUnifiedDiff(
                            diffLines, relativePath, sourceLabel, targetLabel);
                    }
                }
            }

            summary.Results.Add(result);

            // Progress indicator
            if (processed % 100 == 0 || processed == total)
            {
                WriteProgress($"  [{processed}/{total}] files compared...", overwrite: true);
            }
        }

        WriteProgress($"\n  Done. {total} files compared.\n");
        return summary;
    }

    private void WriteProgress(string message, bool overwrite = false)
    {
        if (!_verbose && !overwrite) return;

        if (overwrite)
        {
            Console.Write($"\r{message}");
            if (message.Length < 60)
                Console.Write(new string(' ', 60 - message.Length)); // Clear trailing chars
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
