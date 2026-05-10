namespace RepoCompare.Models;

/// <summary>
/// Represents a Linux-incompatibility issue detected in a source file.
/// These issues will cause problems when migrating from Windows to Linux containers.
/// </summary>
public record LinuxIssue(
    /// <summary>Relative file path where the issue was detected.</summary>
    string FilePath,

    /// <summary>1-indexed line number. 0 indicates a path-level issue (not line-specific).</summary>
    int LineNumber,

    /// <summary>
    /// Issue type identifier, e.g.:
    /// "WindowsPath", "RegistryUsage", "DriveLetterAssumption",
    /// "CRLFInScript", "MissingShebang", "CsprojReferenceCasing",
    /// "HardcodedContainerPath", "CertificatePathAssumption",
    /// "EnvironmentGetFolderPath", "DockerFile"
    /// </summary>
    string IssueType,

    /// <summary>"High", "Medium", "Low", or "Info".</summary>
    string Severity,

    /// <summary>Human-readable description of the issue and its impact.</summary>
    string Description,

    /// <summary>The actual source line content. Empty for path-level issues.</summary>
    string LineContent
);
