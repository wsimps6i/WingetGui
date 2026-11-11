using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class SelfUpdateTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly Version Current = new(0, 1, 0);

    private static string Release(string tag = "v0.2.0", string? asset = null, string? url = null, string? digest = Digest) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/wsimps6i/WingetGui/releases/tag/{{tag}}",
          "assets": [
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/wsimps6i/WingetGui/releases/download/{{tag}}/SHA256SUMS.txt" },
            { "name": "{{asset ?? "WingetGui-0.2.0-win-x64.zip"}}",
              "browser_download_url": "{{url ?? $"https://github.com/wsimps6i/WingetGui/releases/download/{tag}/WingetGui-0.2.0-win-x64.zip"}}"
              {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    [Fact]
    public void A_newer_release_with_a_digest_is_offered()
    {
        var update = SelfUpdate.ParseLatest(Release(), Current)!;
        Assert.Equal(new Version(0, 2, 0), update.Version);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", update.Sha256);
        Assert.Equal("https://github.com/wsimps6i/WingetGui/releases/download/v0.2.0/WingetGui-0.2.0-win-x64.zip", update.ZipUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.1")]       // 0.1 == 0.1.0
    [InlineData("v0.0.9")]
    public void The_same_or_an_older_release_is_not_an_update(string tag) =>
        Assert.Null(SelfUpdate.ParseLatest(Release(tag: tag), Current));

    [Fact]
    public void No_digest_means_no_update() =>
        Assert.Throws<InvalidDataException>(() => SelfUpdate.ParseLatest(Release(digest: null), Current));

    [Theory]
    [InlineData("md5:0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:not-hex-not-hex-not-hex-not-hex-not-hex-not-hex-not-hex-not-hex1")]
    [InlineData("sha256:0123")]
    public void A_malformed_digest_is_refused(string digest) =>
        Assert.Throws<InvalidDataException>(() => SelfUpdate.ParseLatest(Release(digest: digest), Current));

    [Theory]
    [InlineData("https://evil.example/WingetGui-0.2.0-win-x64.zip")]
    [InlineData("https://github.com/someone-else/WingetGui/releases/download/v0.2.0/WingetGui-0.2.0-win-x64.zip")]
    [InlineData("http://github.com/wsimps6i/WingetGui/releases/download/v0.2.0/WingetGui-0.2.0-win-x64.zip")]
    public void A_download_outside_this_repositorys_releases_is_refused(string url) =>
        Assert.Throws<InvalidDataException>(() => SelfUpdate.ParseLatest(Release(url: url), Current));

    [Fact]
    public void A_release_without_the_expected_zip_is_refused() =>
        Assert.Throws<InvalidDataException>(() => SelfUpdate.ParseLatest(Release(asset: "WingetGui-0.2.0-win-arm64.zip"), Current));

    [Fact]
    public void ApplyUpdate_needs_a_folder()
    {
        Assert.Equal(SilentCommand.ApplyUpdate, CommandLine.Parse(["-ApplyUpdate", @"C:\Apps\Winget GUI"]).Command);
        Assert.NotNull(CommandLine.Parse(["-ApplyUpdate"]).Error);
        Assert.Equal(SilentCommand.Unregister, CommandLine.Parse(["-Unregister"]).Command);
    }
}

public class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wingetgui-swap-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name, params (string Path, string Text)[] files)
    {
        var dir = Path.Combine(_root, name);
        foreach (var (path, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, path))!);
            File.WriteAllText(Path.Combine(dir, path), text);
        }
        return dir;
    }

    [Fact]
    public void Swap_replaces_the_files_and_keeps_the_old_copy_as_previous()
    {
        var install = Folder("Winget GUI", ("WingetGui.exe", "old"), ("old-only.dll", "x"));
        var source = Folder("staged", ("WingetGui.exe", "new"), (@"Assets\AppIcon.ico", "icon"));

        UpdateInstaller.Swap(source, install + "\\", TimeSpan.FromSeconds(1));

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "WingetGui.exe")));
        Assert.True(File.Exists(Path.Combine(install, @"Assets\AppIcon.ico")));
        Assert.False(File.Exists(Path.Combine(install, "old-only.dll"))); // no stale files from the old version
        Assert.Equal("old", File.ReadAllText(Path.Combine(install + ".previous", "WingetGui.exe")));
    }

    [Fact]
    public void Swap_refuses_a_folder_that_is_not_a_Winget_GUI_install()
    {
        var notOurs = Folder("Documents", ("letter.docx", "important"));
        var source = Folder("staged", ("WingetGui.exe", "new"));

        Assert.Throws<InvalidOperationException>(() => UpdateInstaller.Swap(source, notOurs, TimeSpan.FromSeconds(1)));
        Assert.Equal("important", File.ReadAllText(Path.Combine(notOurs, "letter.docx")));
    }

    [Fact]
    public void Swap_puts_the_old_copy_back_when_copying_fails()
    {
        var install = Folder("Winget GUI", ("WingetGui.exe", "old"));
        var source = Folder("staged", ("WingetGui.exe", "new"), ("locked.dll", "x"));

        using (File.Open(Path.Combine(source, "locked.dll"), FileMode.Open, FileAccess.Read, FileShare.None)) // copying this fails
            Assert.ThrowsAny<IOException>(() => UpdateInstaller.Swap(source, install, TimeSpan.FromSeconds(1)));

        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "WingetGui.exe")));
        Assert.False(Directory.Exists(install + ".previous"));
    }

    [Fact]
    public void Swap_waits_for_the_old_app_to_release_its_files()
    {
        var install = Folder("Winget GUI", ("WingetGui.exe", "old"));
        var source = Folder("staged", ("WingetGui.exe", "new"));
        var exe = File.Open(Path.Combine(install, "WingetGui.exe"), FileMode.Open, FileAccess.Read, FileShare.Read); // the "running" app
        _ = Task.Delay(1200).ContinueWith(_ => exe.Dispose());                                                     // ...exits after 1.2 s

        var started = DateTime.UtcNow;
        UpdateInstaller.Swap(source, install, TimeSpan.FromSeconds(10));

        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(1), "Swap should have waited for the file to be released");
        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "WingetGui.exe")));
    }
}
