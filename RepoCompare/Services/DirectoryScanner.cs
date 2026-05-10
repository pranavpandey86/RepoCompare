using RepoCompare.Models;

namespace RepoCompare.Services;

/// <summary>
/// Recursively scans a directory for files, excluding common non-source directories
/// like .git, bin, obj, node_modules, etc.
///
/// IMPORTANT: Scans use CASE-SENSITIVE path storage (StringComparer.Ordinal) so that
/// case-only differences between files are preserved and detectable. This is critical
/// for Linux container migration where the filesystem is case-sensitive.
/// </summary>
public class DirectoryScanner
{
    /// <summary>
    /// Default directory names to exclude from scanning.
    /// These are build artifacts, version control, and IDE directories.
    /// Note: exclusion matching is case-INSENSITIVE (you never want bin vs Bin to be scanned).
    /// </summary>
    private static readonly HashSet<string> DefaultExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "TestResults",
        ".nuget"
    };

    /// <summary>
    /// Default file patterns to exclude from scanning.
    /// </summary>
    private static readonly HashSet<string> DefaultExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".user",
        ".suo",
        ".cache",
        ".DS_Store"
    };

    private readonly HashSet<string> _excludedDirs;
    private readonly HashSet<string> _excludedExtensions;

    public DirectoryScanner(
        IEnumerable<string>? additionalExcludedDirs = null,
        IEnumerable<string>? additionalExcludedExtensions = null)
    {
        _excludedDirs = new HashSet<string>(DefaultExcludedDirs, StringComparer.OrdinalIgnoreCase);
        _excludedExtensions = new HashSet<string>(DefaultExcludedExtensions, StringComparer.OrdinalIgnoreCase);

        if (additionalExcludedDirs != null)
        {
            foreach (var dir in additionalExcludedDirs)
                _excludedDirs.Add(dir);
        }

        if (additionalExcludedExtensions != null)
        {
            foreach (var ext in additionalExcludedExtensions)
                _excludedExtensions.Add(ext.StartsWith('.') ? ext : $".{ext}");
        }
    }

    /// <summary>
    /// Scans a directory recursively and returns all file paths relative to the root.
    /// Uses CASE-SENSITIVE comparison (StringComparer.Ordinal) so that paths differing
    /// only by casing are treated as separate entries.
    /// </summary>
    /// <param name="rootPath">Absolute path to the directory to scan.</param>
    /// <returns>Set of relative file paths (using forward slashes), case-sensitive.</returns>
    public HashSet<string> Scan(string rootPath)
    {
        // Case-SENSITIVE: "Config/file.json" and "config/file.json" are separate entries
        var results = new HashSet<string>(StringComparer.Ordinal);

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
        }

        ScanRecursive(rootPath, rootPath, results);
        return results;
    }

    /// <summary>
    /// Detects case-only collisions within a single set of file paths.
    /// These are paths that differ only by casing — they resolve to the same file on
    /// Windows/macOS (case-insensitive) but different files on Linux (case-sensitive).
    /// </summary>
    /// <param name="files">Set of file paths to check (case-sensitive).</param>
    /// <param name="side">Label for where these files came from ("Source" or "Target").</param>
    /// <returns>List of case collision records.</returns>
    public static List<CaseCollision> DetectCaseCollisions(HashSet<string> files, string side)
    {
        var collisions = new List<CaseCollision>();

        // Group by lowercased path — groups with >1 member have case collisions
        var groups = files.GroupBy(f => f.ToLowerInvariant())
                          .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var paths = group.OrderBy(p => p).ToList();
            for (int i = 0; i < paths.Count - 1; i++)
            {
                collisions.Add(new CaseCollision(
                    Path1: paths[i],
                    Path2: paths[i + 1],
                    Side: side,
                    Impact: $"These paths differ only by casing. On Linux (case-sensitive FS), " +
                            $"they are two different files. On Windows, they are the same file. " +
                            $"This WILL cause issues in containers."
                ));
            }
        }

        return collisions;
    }

    /// <summary>
    /// Detects cross-side case collisions: files that exist in both source and target
    /// but with different casing. For example, source has "Config/app.json" and target
    /// has "config/app.json".
    /// </summary>
    public static List<CaseCollision> DetectCrossSideCollisions(
        HashSet<string> sourceFiles, HashSet<string> targetFiles)
    {
        var collisions = new List<CaseCollision>();

        // Build a case-insensitive lookup of target paths
        var targetByLower = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targetFiles)
        {
            var key = t.ToLowerInvariant();
            if (!targetByLower.ContainsKey(key))
                targetByLower[key] = new List<string>();
            targetByLower[key].Add(t);
        }

        foreach (var sourcePath in sourceFiles)
        {
            var key = sourcePath.ToLowerInvariant();
            if (targetByLower.TryGetValue(key, out var targetMatches))
            {
                foreach (var targetPath in targetMatches)
                {
                    // Only flag if the casing is actually different
                    if (!string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
                    {
                        collisions.Add(new CaseCollision(
                            Path1: sourcePath,
                            Path2: targetPath,
                            Side: "CrossSide",
                            Impact: $"Source has '{sourcePath}' but Target has '{targetPath}'. " +
                                    $"These refer to the same file on Windows but different files on Linux. " +
                                    $"The path casing must be unified before container deployment."
                        ));
                    }
                }
            }
        }

        return collisions;
    }

    private void ScanRecursive(string rootPath, string currentPath, HashSet<string> results)
    {
        // Process files in current directory
        foreach (var filePath in Directory.GetFiles(currentPath))
        {
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);

            // Skip excluded file names/extensions (case-insensitive exclusion is fine here)
            if (_excludedExtensions.Contains(fileName) || _excludedExtensions.Contains(extension))
                continue;

            // Compute relative path with forward slashes
            var relativePath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            results.Add(relativePath);
        }

        // Recurse into subdirectories
        foreach (var dirPath in Directory.GetDirectories(currentPath))
        {
            var dirName = Path.GetFileName(dirPath);

            // Skip excluded directories (case-insensitive exclusion)
            if (_excludedDirs.Contains(dirName))
                continue;

            ScanRecursive(rootPath, dirPath, results);
        }
    }
}
