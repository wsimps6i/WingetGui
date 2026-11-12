using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class SkipAndOptionsTests
{
    private static PackageItem Update(string available, string? skipped = null, string? pin = null) => new()
    {
        Id = "Contoso.App", Name = "App", InstalledVersion = "1.0", AvailableVersion = available,
        Update = UpdateState.Available, SkippedVersion = skipped, Pin = pin,
    };

    [Fact]
    public void Skipped_version_is_held_back()
    {
        var p = Update("2.0", skipped: "2.0");
        Assert.True(p.IsSkipped);
        Assert.False(p.CanUpdate);
        Assert.Equal("2.0 (skipped)", p.AvailableText);
    }

    [Fact]
    public void Newer_version_than_the_skipped_one_shows_again()
    {
        var p = Update("2.1", skipped: "2.0");
        Assert.False(p.IsSkipped);
        Assert.True(p.CanUpdate);
        Assert.Equal("2.1", p.AvailableText);
    }

    [Fact]
    public void Pin_wins_over_skip_in_the_text() => Assert.Equal("2.0 (pinned)", Update("2.0", skipped: "2.0", pin: "Pinning").AvailableText);

    [Fact]
    public void Options_are_empty_only_with_nothing_set()
    {
        Assert.True(new PackageOptions().IsEmpty);
        Assert.True(new PackageOptions(Arguments: "  ").IsEmpty);
        Assert.False(new PackageOptions(Architecture: InstallArchitecture.X86).IsEmpty);
        Assert.False(new PackageOptions(Scope: InstallScope.Machine).IsEmpty);
    }

    [Fact]
    public void Install_options_fall_back_to_the_default_scope()
    {
        var settings = new AppSettings { DefaultInstallScope = InstallScope.User };
        settings.PackageOptions["Contoso.Arm"] = new PackageOptions(Architecture: InstallArchitecture.Arm64);
        settings.PackageOptions["Contoso.Machine"] = new PackageOptions(Scope: InstallScope.Machine);

        Assert.Equal(new PackageOptions(InstallScope.User), settings.InstallOptionsFor("Contoso.Unknown"));
        Assert.Equal(new PackageOptions(InstallScope.User, InstallArchitecture.Arm64), settings.InstallOptionsFor("Contoso.Arm"));
        Assert.Equal(InstallScope.Machine, settings.InstallOptionsFor("Contoso.Machine").Scope);
        Assert.Equal(InstallScope.Default, settings.OptionsFor("Contoso.Arm").Scope); // updates don't get a scope
    }

    [Theory]
    [InlineData("Wix", "Unknown", "X64", "System", "ElevationRequired", "MSI (WiX) · x64 · all users · needs admin")]
    [InlineData("Exe", "Unknown", "Neutral", "Unknown", "Unknown", "EXE")]
    [InlineData("Zip", "Msi", "Arm64", "User", "ElevationProhibited", "ZIP containing MSI · arm64 · current user")]
    [InlineData("MSStore", "Unknown", "X64", "Unknown", "Unknown", "Microsoft Store · x64")]
    public void Describes_installer(string type, string nested, string arch, string scope, string elevation, string expected) =>
        Assert.Equal(expected, WingetService.DescribeInstaller(type, nested, arch, scope, elevation));

    [Fact]
    public void Reads_installer_url_and_hash_from_winget_show()
    {
        const string output = "Found 7-Zip [7zip.7zip]\r\nVersion: 26.06\r\nInstaller:\r\n  Installer Type: wix\r\n  Installer Url: https://github.com/ip7z/7zip/releases/download/26.06/7z2606-x64.msi\r\n  Installer SHA256: 6dc822569e777d9a\r\n  Release Date: 2026-11-07\r\n";
        Assert.Equal(new InstallerDownload("https://github.com/ip7z/7zip/releases/download/26.06/7z2606-x64.msi", "6dc822569e777d9a"), WingetCli.ParseInstaller(output));
    }

    [Theory]
    [InlineData("Installer:\n  Installer Type: msstore\n")] // Store apps have no URL
    [InlineData("  Installer Url: file:///C:/evil.exe\n")] // only web addresses are shown as links
    [InlineData("No package found matching input criteria.")]
    public void No_installer_url_gives_nothing(string output) => Assert.Null(WingetCli.ParseInstaller(output));
}
