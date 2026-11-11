using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class CommandLineTests
{
    private static ParsedCommandLine Parse(params string[] args) => CommandLine.Parse(args);

    [Fact]
    public void No_arguments_opens_the_window()
    {
        var cl = Parse();
        Assert.False(cl.IsSilent);
        Assert.False(cl.StartInTray);
    }

    [Theory]
    [InlineData("-Tray")]
    [InlineData("--tray")]
    [InlineData("/TRAY")]
    public void Tray_starts_hidden_but_is_not_silent(string arg)
    {
        var cl = Parse(arg);
        Assert.True(cl.StartInTray);
        Assert.False(cl.IsSilent);
    }

    [Theory]
    [InlineData(SilentCommand.ListUpdates, "-ListUpdates")]
    [InlineData(SilentCommand.UpdateAll, "--updateall")]
    [InlineData(SilentCommand.Help, "/?")]
    [InlineData(SilentCommand.Help, "-h")]
    public void Switches_without_values(SilentCommand expected, string arg)
    {
        var cl = Parse(arg);
        Assert.Equal((expected, (string?)null, (string?)null), (cl.Command, cl.Argument, cl.Error));
        Assert.True(cl.IsSilent);
    }

    [Theory]
    [InlineData(SilentCommand.Install, "-Install", "Mozilla.Firefox")]
    [InlineData(SilentCommand.Export, "--export", @"C:\Temp\my packages.json")]
    [InlineData(SilentCommand.Import, "/Import", "packages.json")]
    public void Switches_with_values(SilentCommand expected, string arg, string value)
    {
        var cl = Parse(arg, value);
        Assert.Equal((expected, value, (string?)null), (cl.Command, cl.Argument, cl.Error));
    }

    [Fact]
    public void Windows_App_SDK_activation_arguments_are_ignored() =>
        Assert.False(Parse("----AppNotificationActivated:action=updates").IsSilent);

    [Theory]
    [InlineData("-UpdatAll")]               // typo: must not silently open the window in a scheduled task
    [InlineData("-Install")]                // missing value
    [InlineData("-Install", "-ListUpdates")] // value that is really another switch
    [InlineData("-ListUpdates", "-UpdateAll")]
    [InlineData("Mozilla.Firefox")]         // stray value
    public void Bad_command_lines_are_errors_and_run_silently(params string[] args)
    {
        var cl = Parse(args);
        Assert.NotNull(cl.Error);
        Assert.True(cl.IsSilent); // prints usage and exits 2 rather than opening the window
    }

    [Fact]
    public async Task A_bad_command_line_exits_2_with_usage_without_touching_winget()
    {
        var output = new StringWriter();
        var code = await SilentRunner.RunAsync(Parse("-Nope"), output, new WingetService(), new AppSettings());
        Assert.Equal(2, code);
        Assert.Contains("Unknown switch '-Nope'", output.ToString());
        Assert.Contains("-UpdateAll", output.ToString());
    }
}
