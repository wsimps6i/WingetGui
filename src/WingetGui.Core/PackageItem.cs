using Microsoft.Management.Deployment;

namespace WingetGui.Core;

public enum UpdateState
{
    None,
    /// <summary>winget can upgrade it in place.</summary>
    Available,
    /// <summary>A newer version exists but no installer of the same technology applies (e.g. MSI installed, MSIX only now). See PLANNING.md, Phase 0.</summary>
    NotApplicable,
}

/// <summary>One row in the Installed, Updates or Search list.</summary>
public sealed class PackageItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? InstalledVersion { get; init; }
    /// <summary>Newer version for an installed package (only when Update != None), or the latest version for a search result.</summary>
    public string? AvailableVersion { get; init; }
    public string Source { get; init; } = "";
    public UpdateState Update { get; init; }

    public bool IsInstalled => InstalledVersion is not null;
    public bool IsUpdateNotApplicable => Update == UpdateState.NotApplicable;

    /// <summary>ListView uses this as each row's accessible name, so screen readers read the package rather than the type name.</summary>
    public override string ToString() => InstalledVersion is null ? $"{Name}, {Id}" : $"{Name}, {Id}, version {InstalledVersion}";

    /// <summary>The COM object operations run against. Null only in tests.</summary>
    internal CatalogPackage? Package { get; init; }
}

public sealed record PackageDetails(
    string? Publisher,
    string? Description,
    string? Homepage,
    string? License,
    string? ReleaseNotes,
    IReadOnlyList<string> Versions);

public sealed record SearchBatch(string Source, IReadOnlyList<PackageItem> Items, string? Error);

public sealed record OperationProgress(string Stage, double? Percent);

public sealed record OperationResult(bool Success, string Message, bool RebootRequired = false);

public enum InstallScope { Default, User, Machine }
