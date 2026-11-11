namespace WingetGui.Core;

public enum SilentCommand { Help, ListUpdates, UpdateAll, Install, Export, Import }

/// <summary>A parsed command line: a silent command (no window), or a normal start, optionally hidden in the tray.</summary>
public sealed record ParsedCommandLine(SilentCommand? Command, string? Argument, bool StartInTray, string? Error)
{
    /// <summary>Runs without a window: a command, or a bad command line (which prints usage and exits 2).</summary>
    public bool IsSilent => Command is not null || Error is not null;
}

public static class CommandLine
{
    public const string Usage = """
        Usage: WingetGui.exe [switch]

          (no switch)       Open the window.
          -Tray             Start hidden in the notification area (used by Start at sign-in).
          -ListUpdates      List available updates.
          -UpdateAll        Install every available update, skipping pinned packages.
          -Install <id>     Install a package by its exact winget id.
          -Export <file>    Save installed packages as a winget export file.
          -Import <file>    Install the packages in a winget export file that aren't installed yet.
          -Help             Show this text.

        Switches also work as --name or /name. Exit code: 0 OK, 1 something failed, 2 bad command line.
        Output goes to the console and to the daily log. From a console, use
        start /wait WingetGui.exe -ListUpdates  (or Start-Process -Wait in PowerShell) to wait for it.
        """;

    private static readonly Dictionary<string, SilentCommand> Switches = new(StringComparer.OrdinalIgnoreCase)
    {
        ["?"] = SilentCommand.Help,
        ["h"] = SilentCommand.Help,
        ["help"] = SilentCommand.Help,
        ["listupdates"] = SilentCommand.ListUpdates,
        ["updateall"] = SilentCommand.UpdateAll,
        ["install"] = SilentCommand.Install,
        ["export"] = SilentCommand.Export,
        ["import"] = SilentCommand.Import,
    };

    public static ParsedCommandLine Parse(IReadOnlyList<string> args)
    {
        SilentCommand? command = null;
        string? argument = null;
        var tray = false;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("----", StringComparison.Ordinal))
                continue; // Windows App SDK activation arguments, e.g. when a notification starts the app

            if (a.Length < 2 || a[0] is not ('-' or '/'))
                return Fail($"Unexpected argument '{a}'.");

            var name = a.TrimStart('-', '/');
            if (name.Equals("tray", StringComparison.OrdinalIgnoreCase))
            {
                tray = true;
                continue;
            }
            if (!Switches.TryGetValue(name, out var found))
                return Fail($"Unknown switch '{a}'.");
            if (command is not null)
                return Fail("Use one switch at a time.");
            command = found;
            if (found is SilentCommand.Install or SilentCommand.Export or SilentCommand.Import)
            {
                if (i + 1 >= args.Count || args[i + 1].Length == 0 || args[i + 1][0] is '-' or '/')
                    return Fail($"{a} needs a value: {(found == SilentCommand.Install ? "a package id" : "a file path")}.");
                argument = args[++i];
            }
        }
        return new ParsedCommandLine(command, argument, tray, null);
    }

    private static ParsedCommandLine Fail(string error) => new(null, null, false, error);
}
