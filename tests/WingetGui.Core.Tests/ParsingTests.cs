using WingetGui.Core;

namespace WingetGui.Core.Tests;

public class ParsingTests
{
    // Same column widths as real `winget pin list` output on winget 1.29 (Name 42, Id 26, Version 17, Source 7).
    private static string Row(string name, string id, string version, string source, string pin) =>
        name.PadRight(42) + id.PadRight(26) + version.PadRight(17) + source.PadRight(7) + pin;

    [Fact]
    public void ParsePins_reads_id_and_pin_type_by_column()
    {
        var output = string.Join("\r\n",
            "   - \r   \\ \r", // progress spinner redrawn with \r before the table
            Row("Name", "Id", "Version", "Source", "Pin type"),
            new string('-', 100),
            Row("Contoso Agent", "Contoso.Agent", "10.6.1.1", "winget", "Pinning"),
            // A name with spaces must not shift the columns, which come from the header.
            Row("Fabrikam Office Suite - en-gb", "Fabrikam.Office", "16.0.20527.20194", "winget", "Blocking"),
            "");

        var pins = WingetCli.ParsePins(output);

        Assert.Equal([new PinInfo("Contoso.Agent", "Pinning"), new PinInfo("Fabrikam.Office", "Blocking")], pins);
    }

    [Theory]
    [InlineData("There are no pins configured.")]
    [InlineData("")]
    public void ParsePins_returns_nothing_without_a_table(string output) =>
        Assert.Empty(WingetCli.ParsePins(output));

    [Fact]
    public void PinInfo_matches_ids_winget_shortened_with_an_ellipsis()
    {
        Assert.True(new PinInfo("Fabrikam.Office", "Pinning").Matches("fabrikam.office"));
        Assert.False(new PinInfo("Fabrikam.Office", "Pinning").Matches("Fabrikam.OfficeTools"));
        Assert.True(new PinInfo("Contoso.VeryLongPackageNa…", "Pinning").Matches("Contoso.VeryLongPackageName"));
    }

    [Fact]
    public void ImportFile_reads_ids_versions_and_source_names()
    {
        const string json = """
            {
              "$schema" : "https://aka.ms/winget-packages.schema.2.0.json",
              "Sources" : [
                {
                  "Packages" : [
                    { "PackageIdentifier" : "Git.Git" },
                    { "PackageIdentifier" : "7zip.7zip", "Version" : "26.06" },
                    { "PackageIdentifier" : "  " }
                  ],
                  "SourceDetails" : { "Argument" : "https://cdn.winget.microsoft.com/cache", "Name" : "winget" }
                },
                { "Packages" : [ { "PackageIdentifier" : "9NBLGGH4NNS1" } ] }
              ]
            }
            """;

        var entries = ImportFile.Parse(json);

        Assert.Equal([
            new ImportEntry("Git.Git", null, "winget"),
            new ImportEntry("7zip.7zip", "26.06", "winget"),
            new ImportEntry("9NBLGGH4NNS1", null, ""),
        ], entries);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "Packages": [] }""")]
    [InlineData("""{ "Sources": [ { "Packages": [ { "Id": "x" } ] } ] }""")]
    public void ImportFile_rejects_files_that_are_not_winget_exports(string json) =>
        Assert.Throws<FormatException>(() => ImportFile.Parse(json));

    // Only rejections: a valid source would really start an elevated winget.
    [Theory]
    [InlineData("bad name", "https://winget.contoso.com/api", "Microsoft.Rest")]
    [InlineData("contoso\" & calc", "https://winget.contoso.com/api", "Microsoft.Rest")]
    [InlineData("contoso", "http://winget.contoso.com/api", "Microsoft.Rest")]
    [InlineData("contoso", "https://x\" --force", "Microsoft.Rest")]
    [InlineData("contoso", "https://winget.contoso.com/api", "Something.Else")]
    public async Task AddSource_rejects_input_before_anything_runs_elevated(string name, string url, string type)
    {
        var result = await WingetCli.AddSourceAsync(name, url, type, isExplicit: false);
        Assert.False(result.Success);
    }
}
