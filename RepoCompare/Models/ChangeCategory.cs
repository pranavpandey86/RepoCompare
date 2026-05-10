namespace RepoCompare.Models;

/// <summary>
/// Classification of a file change based on its path and content patterns.
/// Used to separate container-specific changes from business logic changes
/// during branch synchronization.
/// </summary>
public enum ChangeCategory
{
    /// <summary>Dockerfile, docker-compose, k8s, helm, .dockerignore, entrypoint.sh</summary>
    ContainerSpecific,

    /// <summary>Windows path fixes, registry removal, case fixes for Linux compatibility</summary>
    LinuxMigration,

    /// <summary>Services/, Controllers/, Models/, Repositories/, Handlers/ changes</summary>
    BusinessLogic,

    /// <summary>appsettings.*, launchSettings, web.config, .env files</summary>
    Config,

    /// <summary>*Test*, *Spec*, xunit, nunit test files</summary>
    Test,

    /// <summary>.csproj, .sln, Directory.Build.props, nuget.config, global.json</summary>
    BuildInfra,

    /// <summary>Anything that doesn't match the above patterns</summary>
    Unknown
}

/// <summary>
/// Risk level for applying a file change during branch synchronization.
/// Determines whether a file can be safely copied or needs manual review.
/// </summary>
public enum RiskLevel
{
    /// <summary>Container-only file, no conflict potential — safe to copy directly.</summary>
    SafeToCopy,

    /// <summary>Changed in a way that warrants human review before applying.</summary>
    ReviewRequired,

    /// <summary>Case collision, binary change, or both-sides conflict — manual merge needed.</summary>
    HighRisk
}
