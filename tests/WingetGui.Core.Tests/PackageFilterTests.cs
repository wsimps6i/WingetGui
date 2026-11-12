using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class PackageFilterTests
{
    private static readonly PackageItem Plain = new() { Id = "Contoso.Plain", Name = "Plain", InstalledVersion = "1.0", Source = "winget" };
    private static readonly PackageItem Updatable = new() { Id = "Contoso.New", Name = "New", InstalledVersion = "1.0", AvailableVersion = "2.0", Source = "winget", Update = UpdateState.Available };
    private static readonly PackageItem Reinstall = new() { Id = "Contoso.Msi", Name = "Msi", InstalledVersion = "1.0", AvailableVersion = "2.0", Source = "winget", Update = UpdateState.NotApplicable };
    private static readonly PackageItem Pinned = new() { Id = "Contoso.Pin", Name = "Pin", InstalledVersion = "1.0", Source = "msstore", Pin = "Pinning" };
    private static readonly PackageItem Local = new() { Id = "ARP.Local", Name = "Local", InstalledVersion = "1.0" };
    private static readonly PackageItem[] All = [Plain, Updatable, Reinstall, Pinned, Local];

    private static PackageItem[] Apply(PackageFilter f) => All.Where(f.Matches).ToArray();

    [Fact]
    public void Shows_everything_by_default() => Assert.Equal(All, Apply(new PackageFilter()));

    [Fact]
    public void Hides_unticked_sources_including_not_from_a_source()
    {
        var f = new PackageFilter();
        f.HiddenSources.Add("WINGET"); // case-insensitive, like winget's source names
        f.HiddenSources.Add("");
        Assert.Equal([Pinned], Apply(f));
    }

    [Fact]
    public void Ticked_filters_combine_with_or()
    {
        Assert.Equal([Updatable], Apply(new PackageFilter { OnlyUpdates = true }));
        Assert.Equal([Reinstall, Pinned], Apply(new PackageFilter { OnlyPinned = true, OnlyNeedsReinstall = true }));
    }

    [Fact]
    public void Sources_and_filters_both_apply()
    {
        var f = new PackageFilter { OnlyUpdates = true, OnlyPinned = true };
        f.HiddenSources.Add("msstore");
        Assert.Equal([Updatable], Apply(f));
    }
}
