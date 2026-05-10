using RepoCompare.Models;
using RepoCompare.Utils;

namespace RepoCompare.Services;

/// <summary>
/// Core comparison engine. Compares all files between two directories,
/// normalizing content to filter out encoding/whitespace noise,
/// and categorizes each file as Identical, Modified, OnlyInSource, or OnlyInTarget.
///
/// Uses CASE-SENSITIVE path matching so that files differing only by casing
/// are treated as separate entries (critical for Linux container migration).
///
/// Also runs ChangeClassifier and LinuxScanner on every file.
/// </summary>
public class FileComparer
{
    private readonly DirectoryScanner _scanner;
    private readonly ChangeClassifier _classifier;
    private readonly LinuxScanner _linuxScanner;
    private readonly bool _verbose;

    public FileComparer(DirectoryScanner scanner, bool verbose = false)
    {
        _scanner = scanner;
        _classifier = new ChangeClassifier();
        _linuxScanner = new LinuxScanner();
        _verbose = verbose;
    }

    /// <summary>
    /// Compares all files between source and target directories.
    /// </summary>
    public ComparisonSummary Compare(string sourcePath, string targetPath, string sourceLabel, string targetLabel)
    {
        var summary = new ComparisonSummary
        {
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SourceLabel = sourceLabel,
            TargetLabel = targetLabel
        };

        // ── Step 1: Scan both directories (case-sensitive) ──
        WriteProgress("Scanning source directory...");
        var sourceFiles = _scanner.Scan(sourcePath);
        WriteProgress($"  Found {sourceFiles.Count} files in source");

        WriteProgress("Scanning target directory...");
        var targetFiles = _scanner.Scan(targetPath);
        WriteProgress($"  Found {targetFiles.Count} files in target");

        // ── Step 2: Detect case collisions ──
        WriteProgress("Detecting case collisions...");
        var sourceCollisions = DirectoryScanner.DetectCaseCollisions(sourceFiles, "Source");
        var targetCollisions = DirectoryScanner.DetectCaseCollisions(targetFiles, "Target");
        var crossCollisions = DirectoryScanner.DetectCrossSideCollisions(sourceFiles, targetFiles);

        summary.CaseCollisions.AddRange(sourceCollisions);
        summary.CaseCollisions.AddRange(targetCollisions);
        summary.CaseCollisions.AddRange(crossCollisions);

        if (summary.CaseCollisions.Count > 0)
        {
            WriteProgress($"  ⚠️  Found {summary.CaseCollisions.Count} case collisions!");
        }
        else
        {
            WriteProgress("  ✅ No case collisions detected");
        }

        // Build a set of paths that have case collisions (for risk elevation)
        var collisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in summary.CaseCollisions)
        {
            collisionPaths.Add(c.Path1);
            collisionPaths.Add(c.Path2);
        }

        // ── Step 3: Determine the union of all file paths (CASE-SENSITIVE) ──
        var allFiles = new SortedSet<string>(sourceFiles, StringComparer.Ordinal);
        allFiles.UnionWith(targetFiles);

        // Also build case-insensitive lookups for cross-side matching
        var sourceLowerMap = BuildLowerCaseMap(sourceFiles);
        var targetLowerMap = BuildLowerCaseMap(targetFiles);

        WriteProgress($"\nComparing {allFiles.Count} unique files...\n");

        int processed = 0;
        int total = allFiles.Count;

        // ── Step 4: Compare each file ──
        foreach (var relativePath in allFiles)
        {
            processed++;

            // Case-sensitive membership check
            bool inSource = sourceFiles.Contains(relativePath);
            bool inTarget = targetFiles.Contains(relativePath);

            // If not in target with exact case, check if there's a case-insensitive match
            // (this means a case-rename happened — flag it but still compare)
            string? sourceMatchPath = inSource ? relativePath : FindCaseInsensitiveMatch(relativePath, sourceLowerMap);
            string? targetMatchPath = inTarget ? relativePath : FindCaseInsensitiveMatch(relativePath, targetLowerMap);

            var result = new ComparisonResult { RelativePath = relativePath };
            string? sourceContent = null;
            string? targetContent = null;

            if (inSource && !inTarget)
            {
                result.Status = FileStatus.OnlyInSource;
                result.SourceSizeBytes = new FileInfo(Path.Combine(sourcePath, relativePath)).Length;

                // Scan for Linux issues
                var linuxIssues = _linuxScanner.ScanFile(relativePath, Path.Combine(sourcePath, relativePath), true);
                result.LinuxIssues.AddRange(linuxIssues);

                // Read content for classification
                sourceContent = TryReadNormalized(Path.Combine(sourcePath, relativePath));
            }
            else if (!inSource && inTarget)
            {
                result.Status = FileStatus.OnlyInTarget;
                result.TargetSizeBytes = new FileInfo(Path.Combine(targetPath, relativePath)).Length;

                var linuxIssues = _linuxScanner.ScanFile(relativePath, Path.Combine(targetPath, relativePath), false);
                result.LinuxIssues.AddRange(linuxIssues);

                targetContent = TryReadNormalized(Path.Combine(targetPath, relativePath));
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
                    sourceContent = sourceNormalized;
                    targetContent = targetNormalized;

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

                // Scan for Linux issues (scan both sides)
                var srcLinuxIssues = _linuxScanner.ScanFile(relativePath, sourceFilePath, true);
                var tgtLinuxIssues = _linuxScanner.ScanFile(relativePath, targetFilePath, false);
                result.LinuxIssues.AddRange(srcLinuxIssues);
                // Add target issues that aren't duplicates
                foreach (var ti in tgtLinuxIssues)
                {
                    if (!result.LinuxIssues.Any(li =>
                        li.IssueType == ti.IssueType && li.LineNumber == ti.LineNumber))
                    {
                        result.LinuxIssues.Add(ti);
                    }
                }
            }

            // ── Classify the result ──
            bool hasCaseCollision = collisionPaths.Contains(relativePath);
            _classifier.Classify(result, sourceContent, targetContent, hasCaseCollision);

            summary.Results.Add(result);
            summary.AllLinuxIssues.AddRange(result.LinuxIssues);

            // Progress indicator
            if (processed % 100 == 0 || processed == total)
            {
                WriteProgress($"  [{processed}/{total}] files compared...", overwrite: true);
            }
        }

        WriteProgress($"\n  Done. {total} files compared.\n");
        return summary;
    }

    private static string? TryReadNormalized(string filePath)
    {
        try
        {
            if (ContentNormalizer.IsBinaryFile(filePath))
                return null;
            return ContentNormalizer.ReadAndNormalize(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, List<string>> BuildLowerCaseMap(HashSet<string> files)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var key = f.ToLowerInvariant();
            if (!map.ContainsKey(key))
                map[key] = new List<string>();
            map[key].Add(f);
        }
        return map;
    }

    private static string? FindCaseInsensitiveMatch(string path, Dictionary<string, List<string>> lowerMap)
    {
        var key = path.ToLowerInvariant();
        if (lowerMap.TryGetValue(key, out var matches))
        {
            return matches.FirstOrDefault(m => !string.Equals(m, path, StringComparison.Ordinal));
        }
        return null;
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
