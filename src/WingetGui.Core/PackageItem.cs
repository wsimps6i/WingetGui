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

    /// <summary>winget pin type (Pinning, Blocking, Gating) for an installed package, from `winget pin list`; null when not pinned.</summary>
    public string? Pin { get; set; }

    public bool IsInstalled => InstalledVersion is not null;
    public bool IsUpdateNotApplicable => Update == UpdateState.NotApplicable;
    public bool IsPinned => Pin is not null;

    /// <summary>Update selected / Update all include it. Any pin keeps a package out, Gating included: a pin means "leave it to me".</summary>
    public bool CanUpdate => Update == UpdateState.Available && Pin is null;

    /// <summary>Available column text: shows the pin, since a pinned package's update is held back.</summary>
    public string? AvailableText => Pin is null ? AvailableVersion : $"{AvailableVersion} (pinned)".TrimStart();

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

public sealed record SourceInfo(string Name, string Argument, string Type, bool Explicit, string Trust)
{
    /// <summary>The ListView row's accessible name.</summary>
    public override string ToString() => $"{Name}, {Argument}";
}

public sealed record SearchBatch(string Source, IReadOnlyList<PackageItem> Items, string? Error);

public sealed record OperationProgress(string Stage, double? Percent);

public sealed record OperationResult(bool Success, string Message, bool RebootRequired = false);

public enum InstallScope { Default, User, Machine }

/// <summary>Which field a catalog search matches, in the Search mode buttons' order.</summary>
public enum SearchField { Name, Id, Both }

/// <summary>The filter panel's state on the Installed and Updates pages.</summary>
public sealed class PackageFilter
{
    /// <summary>Unticked sources. "" stands for packages not from a source (Add/Remove Programs only).</summary>
    public HashSet<string> HiddenSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool OnlyUpdates { get; set; }
    public bool OnlyPinned { get; set; }
    public bool OnlyNeedsReinstall { get; set; }

    /// <summary>Ticked filters combine with OR: Pinned plus Has an update shows both kinds. None ticked shows everything.</summary>
    public bool Matches(PackageItem p) =>
        !HiddenSources.Contains(p.Source)
        && (!(OnlyUpdates || OnlyPinned || OnlyNeedsReinstall)
            || (OnlyUpdates && p.Update == UpdateState.Available)
            || (OnlyPinned && p.IsPinned)
            || (OnlyNeedsReinstall && p.IsUpdateNotApplicable));
}
