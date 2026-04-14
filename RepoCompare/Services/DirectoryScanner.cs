namespace RepoCompare.Services;

/// <summary>
/// Recursively scans a directory for files, excluding common non-source directories
/// like .git, bin, obj, node_modules, etc.
/// </summary>
public class DirectoryScanner
{
    /// <summary>
    /// Default directory names to exclude from scanning.
    /// These are build artifacts, version control, and IDE directories.
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
    /// </summary>
    /// <param name="rootPath">Absolute path to the directory to scan.</param>
    /// <returns>Set of relative file paths (using forward slashes).</returns>
    public HashSet<string> Scan(string rootPath)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
        }

        ScanRecursive(rootPath, rootPath, results);
        return results;
    }

    private void ScanRecursive(string rootPath, string currentPath, HashSet<string> results)
    {
        // Process files in current directory
        foreach (var filePath in Directory.GetFiles(currentPath))
        {
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);

            // Skip excluded file names/extensions
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

            // Skip excluded directories
            if (_excludedDirs.Contains(dirName))
                continue;

            ScanRecursive(rootPath, dirPath, results);
        }
    }
}
