using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class LogAndTextTests
{
    [Fact]
    public void Prune_keeps_the_retention_window_and_ignores_other_files()
    {
        var folder = Directory.CreateTempSubdirectory("wingetgui-logs-").FullName;
        try
        {
            var today = new DateTime(2026, 11, 11);
            foreach (var name in new[] { "2026-11-11.log", "2026-11-09.log", "2026-11-08.log", "2026-10-01.log", "notes.log", "2026-01-01.txt" })
                File.WriteAllText(Path.Combine(folder, name), "x");

            Log.Prune(folder, retentionDays: 4, today); // keeps 11-08 .. 11-11

            var left = Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).Order().ToArray();
            Assert.Equal(["2026-01-01.txt", "2026-11-08.log", "2026-11-09.log", "2026-11-11.log", "notes.log"], left);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("PostInstall", "Post install")]
    [InlineData("Downloading", "Downloading")]
    [InlineData("InstallError", "Install error")]
    public void Stage_turns_enum_names_into_words(string state, string expected) =>
        Assert.Equal(expected, WingetService.Stage(state));

    [Fact]
    public void Progress_shows_percent_and_size_or_stays_indeterminate()
    {
        Assert.Equal(new OperationProgress("Downloading 50% (1.0 of 2.0 MB)", 0.5), WingetService.Progress("Downloading", 0.5, 1048576, 2097152));
        Assert.Equal(new OperationProgress("Installing 25%", 0.25), WingetService.Progress("Installing", 0.25));
        Assert.Equal(new OperationProgress("Installing…", null), WingetService.Progress("Installing", 0));
    }

    [Fact]
    public void Failure_includes_installer_code_and_hresult()
    {
        var text = WingetService.Failure("InstallError", 1603, new Exception("Fatal error during installation.") { HResult = unchecked((int)0x8A150011) });
        Assert.Equal("Install error (installer exit code 1603): Fatal error during installation. (0x8A150011)", text);
    }
}
