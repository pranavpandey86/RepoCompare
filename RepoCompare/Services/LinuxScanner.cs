using System.Text.RegularExpressions;
using RepoCompare.Models;

namespace RepoCompare.Services;

/// <summary>
/// Scans file content and paths for Linux-incompatible patterns.
/// Detects Windows paths, registry usage, CRLF in scripts, missing shebangs,
/// csproj reference casing issues, and other container-migration risks.
/// </summary>
public class LinuxScanner
{
    private static readonly Regex WindowsAbsolutePathRegex = new(
        @"[A-Za-z]:\\[^\s""'<>|*?]*", RegexOptions.Compiled);

    private static readonly Regex UncPathRegex = new(
        @"\\\\[A-Za-z0-9_\-\.]+\\[^\s""']*", RegexOptions.Compiled);

    private static readonly Regex BackslashInStringRegex = new(
        @"""[^""]*\\[^""nrt\\0abfvuUx][^""]*""", RegexOptions.Compiled);

    private static readonly Regex RegistryRegex = new(
        @"Microsoft\.Win32\.Registry|RegistryKey|Registry\s*\.\s*(LocalMachine|CurrentUser|ClassesRoot|GetValue|SetValue)",
        RegexOptions.Compiled);

    private static readonly Regex GetFolderPathRegex = new(
        @"Environment\.GetFolderPath\s*\(", RegexOptions.Compiled);

    private static readonly Regex DriveLetterRegex = new(
        @"Path\.GetPathRoot|[""'][A-Za-z]:\\", RegexOptions.Compiled);

    private static readonly Regex HardcodedContainerPathRegex = new(
        @"/app/wwwroot|/app/publish|/home/site/wwwroot", RegexOptions.Compiled);

    private static readonly Regex CertPathRegex = new(
        @"\.(pfx|cer|crt|pem|key)[""'\s,)]", RegexOptions.Compiled);

    private static readonly Regex CsprojReferenceRegex = new(
        @"<ProjectReference\s+Include=""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// Scans a single file for Linux-incompatibility issues.
    /// </summary>
    /// <param name="relativePath">Relative path of the file.</param>
    /// <param name="absolutePath">Absolute path to read the file from.</param>
    /// <param name="isSource">True if scanning source side, false for target.</param>
    /// <returns>List of Linux issues found in this file.</returns>
    public List<LinuxIssue> ScanFile(string relativePath, string absolutePath, bool isSource)
    {
        var issues = new List<LinuxIssue>();
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();

        // Path-level checks
        ScanPathIssues(relativePath, issues);

        // Content-level checks for text files
        if (IsTextFileExtension(ext) && File.Exists(absolutePath))
        {
            try
            {
                var rawBytes = File.ReadAllBytes(absolutePath);
                var lines = File.ReadAllLines(absolutePath);

                // Shell script checks
                if (ext == ".sh" || ext == ".bash")
                {
                    ScanShellScript(relativePath, rawBytes, lines, issues);
                }

                // .csproj reference casing checks
                if (ext == ".csproj")
                {
                    ScanCsprojReferences(relativePath, lines, issues);
                }

                // General content checks (for .cs, .json, .xml, .config, etc.)
                ScanContentForWindowsPatterns(relativePath, lines, issues);
            }
            catch (Exception)
            {
                // Skip unreadable files silently
            }
        }

        return issues;
    }

    private static void ScanPathIssues(string relativePath, List<LinuxIssue> issues)
    {
        // Check for spaces in path (not an error but worth noting)
        if (relativePath.Contains(' '))
        {
            issues.Add(new LinuxIssue(
                relativePath, 0, "SpaceInPath", "Low",
                "Path contains spaces — ensure scripts and configs properly quote this path",
                ""));
        }

        // Check for backslash in stored path (shouldn't happen after normalization, but just in case)
        if (relativePath.Contains('\\'))
        {
            issues.Add(new LinuxIssue(
                relativePath, 0, "BackslashInPath", "High",
                "Path contains backslash — this will not resolve correctly on Linux",
                ""));
        }
    }

    private static void ScanShellScript(string relativePath, byte[] rawBytes, string[] lines, List<LinuxIssue> issues)
    {
        // Check for CRLF line endings
        bool hasCrlf = false;
        for (int i = 0; i < rawBytes.Length - 1; i++)
        {
            if (rawBytes[i] == 0x0D && rawBytes[i + 1] == 0x0A) // \r\n
            {
                hasCrlf = true;
                break;
            }
        }
        if (hasCrlf)
        {
            issues.Add(new LinuxIssue(
                relativePath, 0, "CRLFInScript", "High",
                "Shell script has CRLF line endings — will fail with '/bin/bash^M: bad interpreter' on Linux. " +
                "Convert to LF before deployment.",
                ""));
        }

        // Check for missing shebang
        if (lines.Length > 0 && !lines[0].StartsWith("#!/"))
        {
            issues.Add(new LinuxIssue(
                relativePath, 1, "MissingShebang", "Medium",
                "Shell script is missing shebang (#!/bin/bash or #!/bin/sh) on the first line. " +
                "May not execute correctly on Linux.",
                lines.Length > 0 ? lines[0] : ""));
        }
    }

    private static void ScanCsprojReferences(string relativePath, string[] lines, List<LinuxIssue> issues)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var match = CsprojReferenceRegex.Match(lines[i]);
            if (match.Success)
            {
                var refPath = match.Groups[1].Value;

                // Check for backslashes in project references
                if (refPath.Contains('\\'))
                {
                    issues.Add(new LinuxIssue(
                        relativePath, i + 1, "CsprojBackslashRef", "High",
                        $"Project reference uses backslash path '{refPath}' — change to forward slashes for Linux compatibility",
                        lines[i].Trim()));
                }

                // Check if reference path casing might not match actual file
                // (We can only flag the pattern — actual verification needs directory scanning)
                if (refPath.Contains("..\\") || refPath.Contains("..//"))
                {
                    issues.Add(new LinuxIssue(
                        relativePath, i + 1, "CsprojRelativeRef", "Medium",
                        $"Project reference uses relative parent path '{refPath}' — verify casing matches actual file path on Linux",
                        lines[i].Trim()));
                }
            }
        }
    }

    private static void ScanContentForWindowsPatterns(string relativePath, string[] lines, List<LinuxIssue> issues)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        // Skip scanning binary-ish extensions and very large files
        if (!IsCodeOrConfigExtension(ext))
            return;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Windows absolute paths
            if (WindowsAbsolutePathRegex.IsMatch(line))
            {
                issues.Add(new LinuxIssue(
                    relativePath, lineNum, "WindowsPath", "High",
                    "Contains Windows absolute path (drive letter + backslash)",
                    line.Trim()));
            }

            // UNC paths
            if (UncPathRegex.IsMatch(line))
            {
                issues.Add(new LinuxIssue(
                    relativePath, lineNum, "UNCPath", "High",
                    "Contains UNC network path (\\\\server\\share) — not available on Linux",
                    line.Trim()));
            }

            // Registry usage
            if (RegistryRegex.IsMatch(line))
            {
                issues.Add(new LinuxIssue(
                    relativePath, lineNum, "RegistryUsage", "High",
                    "Uses Windows Registry — not available on Linux containers",
                    line.Trim()));
            }

            // Environment.GetFolderPath
            if (GetFolderPathRegex.IsMatch(line))
            {
                issues.Add(new LinuxIssue(
                    relativePath, lineNum, "EnvironmentGetFolderPath", "Medium",
                    "Uses Environment.GetFolderPath — folder paths are different on Linux",
                    line.Trim()));
            }

            // Drive letter assumptions
            if (DriveLetterRegex.IsMatch(line))
            {
                // Don't double-report if already caught by WindowsAbsolutePathRegex
                if (!WindowsAbsolutePathRegex.IsMatch(line))
                {
                    issues.Add(new LinuxIssue(
                        relativePath, lineNum, "DriveLetterAssumption", "Medium",
                        "Contains drive letter pattern or Path.GetPathRoot — may assume Windows drives",
                        line.Trim()));
                }
            }

            // Hardcoded container paths
            if (HardcodedContainerPathRegex.IsMatch(line))
            {
                issues.Add(new LinuxIssue(
                    relativePath, lineNum, "HardcodedContainerPath", "Low",
                    "Contains hardcoded container path — verify this matches your container configuration",
                    line.Trim()));
            }

            // Certificate path assumptions
            if (CertPathRegex.IsMatch(line))
            {
                issues.Add(new LinuxIssue(
                    relativePath, lineNum, "CertificatePathAssumption", "Medium",
                    "References certificate file — verify path and format work on Linux",
                    line.Trim()));
            }
        }
    }

    private static bool IsTextFileExtension(string ext)
    {
        return ext switch
        {
            ".cs" or ".csproj" or ".sln" or ".json" or ".xml" or ".config" or ".yaml" or ".yml"
            or ".md" or ".txt" or ".sh" or ".bash" or ".ps1" or ".cmd" or ".bat" or ".props"
            or ".targets" or ".razor" or ".cshtml" or ".html" or ".css" or ".js" or ".ts"
            or ".tsx" or ".jsx" or ".sql" or ".env" or ".gitignore" or ".dockerignore"
            or ".editorconfig" or ".ini" or ".cfg" or ".toml" or ".proto" or ".graphql"
            or ".http" or ".rest" or ".csv" => true,
            _ => false
        };
    }

    private static bool IsCodeOrConfigExtension(string ext)
    {
        return ext switch
        {
            ".cs" or ".csproj" or ".sln" or ".json" or ".xml" or ".config" or ".yaml" or ".yml"
            or ".props" or ".targets" or ".razor" or ".cshtml" or ".sh" or ".bash"
            or ".ps1" or ".cmd" or ".bat" or ".sql" or ".env" or ".ini" or ".cfg"
            or ".toml" or ".proto" or ".http" or ".rest" => true,
            _ => false
        };
    }
}
