using System.Text.RegularExpressions;
using RepoCompare.Models;

namespace RepoCompare.Services;

/// <summary>
/// Heuristic classifier that assigns ChangeCategory + RiskLevel to each ComparisonResult.
/// Rules evaluated in priority order — first matching category wins.
/// Risk can be elevated independently by special conditions.
/// </summary>
public class ChangeClassifier
{
    private static readonly string[] ContainerPathPatterns =
    [
        "Dockerfile", "dockerfile", ".dockerignore", "docker-compose", "docker_compose",
        "entrypoint.", "k8s/", "kubernetes/", "helm/", "charts/", "openshift/", "tekton/",
        ".helm", "skaffold.yaml", "tilt", "podman"
    ];

    private static readonly string[] TestPathPatterns =
    [
        "Test/", "Tests/", "test/", "tests/", ".Tests/", ".Test/",
        "Spec/", "Specs/", ".Tests.csproj", ".Test.csproj", "xunit", "nunit", "MSTest"
    ];

    private static readonly string[] ConfigPathPatterns =
    [
        "appsettings", "launchSettings", "web.config", "app.config", ".env", "nlog.config", "serilog", "log4net"
    ];

    private static readonly HashSet<string> BuildInfraExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".csproj", ".sln", ".props", ".targets" };

    private static readonly string[] BuildInfraFiles =
    [
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "nuget.config", "NuGet.Config", "global.json", ".editorconfig"
    ];

    private static readonly string[] BusinessLogicPathPatterns =
    [
        "Services/", "Controllers/", "Models/", "Repositories/", "Repository/", "Handlers/",
        "Commands/", "Queries/", "Validators/", "Middleware/", "Filters/", "Extensions/",
        "Interfaces/", "Hubs/", "SignalR/", "GraphQL/"
    ];

    private static readonly Regex WindowsPathRegex = new(
        @"[A-Za-z]:\\[^\s""']*|\\\\[A-Za-z0-9_\-\.]+\\", RegexOptions.Compiled);

    private static readonly Regex RegistryRegex = new(
        @"Microsoft\.Win32\.Registry|RegistryKey|Registry\.(LocalMachine|CurrentUser|GetValue)", RegexOptions.Compiled);

    private static readonly Regex EnvironmentFolderRegex = new(
        @"Environment\.GetFolderPath\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Classifies a ComparisonResult by assigning Category and RiskLevel.
    /// </summary>
    public void Classify(ComparisonResult result, string? sourceContent, string? targetContent, bool hasCaseCollision)
    {
        var path = result.RelativePath;
        var fileName = Path.GetFileName(path);
        var content = sourceContent ?? targetContent;

        // Step 1: Category
        result.Category = DetermineCategory(path, fileName, content);

        // Step 2: Base risk
        result.Risk = DetermineBaseRisk(result);

        // Step 3: Elevate risk for special conditions
        if (hasCaseCollision)
        {
            result.Risk = RiskLevel.HighRisk;
            result.RiskReasons.Add("File path has a case-only collision — will break on Linux");
        }

        if (result.IsBinary && result.Status == FileStatus.Modified)
        {
            result.Risk = RiskLevel.HighRisk;
            result.RiskReasons.Add("Binary file with content differences — cannot auto-merge");
        }

        if (content != null && !result.IsBinary)
        {
            if (WindowsPathRegex.IsMatch(content))
            {
                if (result.Category != ChangeCategory.LinuxMigration)
                    result.Category = ChangeCategory.LinuxMigration;
                result.Risk = RiskLevel.ReviewRequired;
                result.RiskReasons.Add("Contains Windows-style paths (backslashes or drive letters)");
            }
            if (RegistryRegex.IsMatch(content))
            {
                result.Category = ChangeCategory.LinuxMigration;
                result.Risk = RiskLevel.HighRisk;
                result.RiskReasons.Add("Contains Windows Registry access — not available on Linux");
            }
            if (EnvironmentFolderRegex.IsMatch(content))
                result.RiskReasons.Add("Uses Environment.GetFolderPath — paths differ on Linux");
        }

        if (result.RiskReasons.Count == 0)
        {
            result.RiskReasons.Add(result.Risk switch
            {
                RiskLevel.SafeToCopy => "No conflict indicators detected",
                RiskLevel.ReviewRequired => "Standard review recommended before applying",
                RiskLevel.HighRisk => "Multiple risk factors detected",
                _ => "Unclassified"
            });
        }
    }

    private static ChangeCategory DetermineCategory(string path, string fileName, string? content)
    {
        if (ContainerPathPatterns.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return ChangeCategory.ContainerSpecific;
        if (TestPathPatterns.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return ChangeCategory.Test;
        if (ConfigPathPatterns.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return ChangeCategory.Config;
        var ext = Path.GetExtension(path);
        if (BuildInfraExtensions.Contains(ext))
            return ChangeCategory.BuildInfra;
        if (BuildInfraFiles.Any(f => fileName.Equals(f, StringComparison.OrdinalIgnoreCase)))
            return ChangeCategory.BuildInfra;
        if (content != null && (WindowsPathRegex.IsMatch(content) || RegistryRegex.IsMatch(content)))
            return ChangeCategory.LinuxMigration;
        if (BusinessLogicPathPatterns.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return ChangeCategory.BusinessLogic;
        if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return ChangeCategory.BusinessLogic;
        return ChangeCategory.Unknown;
    }

    private static RiskLevel DetermineBaseRisk(ComparisonResult result)
    {
        if (result.Status == FileStatus.Identical)
            return RiskLevel.SafeToCopy;

        if (result.Category == ChangeCategory.ContainerSpecific &&
            result.Status is FileStatus.OnlyInSource or FileStatus.OnlyInTarget)
            return RiskLevel.SafeToCopy;

        return RiskLevel.ReviewRequired;
    }
}
