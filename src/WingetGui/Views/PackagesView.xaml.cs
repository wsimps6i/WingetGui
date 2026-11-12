using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WingetGui.Core;
using WinRT.Interop;

namespace WingetGui.Views;

public enum PackageMode { Installed, Updates, Search }

/// <summary>The Installed, Updates and Search pages: one list with a details pane, differing only in where the rows come from and which buttons show.</summary>
public sealed partial class PackagesView : UserControl
{
    private readonly PackageMode _mode;
    private readonly PackageFilter _filter = new();
    private bool _sourcesLoaded;
    private CancellationTokenSource? _search;
    private int _detailsRequest;
    private bool _confirming;

    public PackagesView(PackageMode mode)
    {
        InitializeComponent();
        _mode = mode;
        TitleText.Text = mode.ToString();
        QueryBox.PlaceholderText = mode == PackageMode.Search ? "Search winget and the Microsoft Store" : "Filter by name or id";
        InstallSelectedButton.Visibility = Show(mode == PackageMode.Search);
        UpdateSelectedButton.Visibility = Show(mode != PackageMode.Search);
        UpdateAllButton.Visibility = Show(mode == PackageMode.Updates);
        UninstallSelectedButton.Visibility = Show(mode == PackageMode.Installed);
        RefreshButton.Visibility = Show(mode != PackageMode.Search);
        MoreButton.Visibility = Show(mode == PackageMode.Installed);
        ScopeBox.SelectedIndex = (int)App.Settings.DefaultInstallScope;
        FiltersExpander.Visibility = Show(mode != PackageMode.Search);
        SearchModeExpander.Visibility = Show(mode == PackageMode.Search);
        Status.IsOpen = false;
        UpdateButtons();
        Loaded += (_, _) => _ = LoadSourcesAsync(); // again each time the page is shown, to pick up sources added or removed

        if (mode != PackageMode.Search)
        {
            App.Installed.Changed += ShowInstalled;
            ShowInstalled();
        }
    }

    public static double DimIf(bool dim) => dim ? 0.45 : 1;

    public static string? AvailableTip(bool notApplicable) => notApplicable
        ? "A newer version exists, but winget can't upgrade this install in place (for example, it was installed as MSI and the new version is MSIX only)."
        : null;

    private List<PackageItem> Selected => List.SelectedItems.Cast<PackageItem>().ToList();

    private void ShowInstalled()
    {
        LoadingBar.Visibility = Show(App.Installed.IsLoading);
        if (App.Installed.Error is { } error)
            ShowStatus(InfoBarSeverity.Error, error);

        IEnumerable<PackageItem> items = App.Installed.Items;
        if (_mode == PackageMode.Updates)
            items = items.Where(p => p.Update != UpdateState.None).OrderBy(p => p.Update); // Available before NotApplicable
        items = items.Where(_filter.Matches);
        var filter = QueryBox.Text.Trim();
        if (filter.Length > 0)
            items = items.Where(p => p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                  || p.Id.Contains(filter, StringComparison.OrdinalIgnoreCase));
        var list = items.ToList();
        List.ItemsSource = list;
        CountText.Text = _mode == PackageMode.Updates
            ? $"{list.Count(p => p.CanUpdate)} available"
            : $"{list.Count} packages";
        UpdateButtons();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refilter();

    private void Refilter()
    {
        if (_mode == PackageMode.Search)
            _ = SearchAsync(QueryBox.Text.Trim());
        else
            ShowInstalled();
    }

    /// <summary>One tick box per winget source, keeping what was unticked. Installed and Updates also get one for apps winget didn't install.</summary>
    private async Task LoadSourcesAsync()
    {
        IReadOnlyList<SourceInfo> sources;
        try
        {
            sources = await App.Winget.GetSourcesAsync();
        }
        catch (Exception ex)
        {
            Log.Error($"Listing sources for the filter panel failed: {ex.Message}"); // the list itself reports winget not starting
            return;
        }
        if (!_sourcesLoaded && _mode == PackageMode.Search)
            foreach (var s in sources.Where(s => s.Explicit))
                _filter.HiddenSources.Add(s.Name); // as in the CLI, explicit sources (winget-font) are searched only when asked for
        _sourcesLoaded = true;

        var names = sources.Select(s => s.Name).ToList();
        if (_mode != PackageMode.Search)
            names.Add("");
        SourceChecks.Children.Clear();
        foreach (var name in names)
        {
            var box = new CheckBox { Content = name.Length > 0 ? name : "Not from a source", Tag = name, IsChecked = !_filter.HiddenSources.Contains(name) };
            box.Click += SourceCheck_Click;
            SourceChecks.Children.Add(box);
        }
    }

    private void SourceCheck_Click(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        if (box.IsChecked == true)
            _filter.HiddenSources.Remove((string)box.Tag);
        else
            _filter.HiddenSources.Add((string)box.Tag);
        Refilter();
    }

    private void SelectAllSources_Click(object sender, RoutedEventArgs e) => SetAllSources(true);

    private void ClearSources_Click(object sender, RoutedEventArgs e) => SetAllSources(false);

    private void SetAllSources(bool ticked)
    {
        foreach (var box in SourceChecks.Children.OfType<CheckBox>())
        {
            box.IsChecked = ticked;
            if (ticked)
                _filter.HiddenSources.Remove((string)box.Tag);
            else
                _filter.HiddenSources.Add((string)box.Tag);
        }
        Refilter();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        _filter.OnlyUpdates = HasUpdateCheck.IsChecked == true;
        _filter.OnlyPinned = PinnedCheck.IsChecked == true;
        _filter.OnlyNeedsReinstall = ReinstallCheck.IsChecked == true;
        ShowInstalled();
    }

    private void SearchMode_Changed(object sender, object e)
    {
        if (IsLoaded) // SelectedIndex in XAML fires this during InitializeComponent
            Refilter();
    }

    private void FilterToggle_Click(object sender, RoutedEventArgs e) => FilterPanel.Visibility = Show(FilterToggle.IsChecked == true);

    private async Task SearchAsync(string query)
    {
        _search?.Cancel();
        var cts = _search = new CancellationTokenSource();
        var results = new ObservableCollection<PackageItem>();
        if (query.Length < 2)
        {
            List.ItemsSource = results;
            CountText.Text = "";
            LoadingBar.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            await Task.Delay(400, cts.Token); // wait for typing to pause
            List.ItemsSource = results;
            LoadingBar.Visibility = Visibility.Visible;
            Status.IsOpen = false;
            var failed = new List<string>();
            if (_sourcesLoaded && SourceChecks.Children.OfType<CheckBox>().All(c => c.IsChecked != true))
                ShowStatus(InfoBarSeverity.Informational, "Tick at least one source to search.");
            // A copy: ticking boxes mid-search mustn't change the set the search threads read. Null until sources load = the default sources.
            var hidden = _sourcesLoaded ? new HashSet<string>(_filter.HiddenSources, StringComparer.OrdinalIgnoreCase) : null;
            await foreach (var batch in App.Winget.SearchAsync(query, (SearchField)Math.Max(0, FieldButtons.SelectedIndex), ExactCheck.IsChecked == true, hidden, cts.Token))
            {
                foreach (var item in batch.Items)
                    results.Add(item);
                if (batch.Error is not null)
                    failed.Add($"{batch.Source}: {batch.Error}");
                CountText.Text = $"{results.Count} results";
            }
            if (failed.Count > 0)
                ShowStatus(InfoBarSeverity.Warning, "Some sources couldn't be searched. " + string.Join("; ", failed));
            LoadingBar.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { } // superseded by newer typing
        catch (Exception ex)
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            ShowStatus(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
        var selected = Selected;
        var request = ++_detailsRequest;
        if (selected.Count != 1)
        {
            Details.Visibility = Visibility.Collapsed;
            DetailsPlaceholder.Visibility = Visibility.Visible;
            DetailsPlaceholder.Text = selected.Count == 0 ? "Select a package to see its details." : $"{selected.Count} packages selected.";
            return;
        }

        var item = selected[0];
        DetailsPlaceholder.Visibility = Visibility.Collapsed;
        Details.Visibility = Visibility.Visible;
        DetailName.Text = item.Name;
        DetailId.Text = item.Id;
        InstallPanel.Visibility = Show(!item.IsInstalled);
        InstalledPanel.Visibility = Show(item.IsInstalled);
        UpdateOneButton.IsEnabled = item.CanUpdate;
        UpdateOneButton.Content = item.CanUpdate ? $"Update to {item.AvailableVersion}" : item.IsPinned ? "Pinned" : "Up to date";
        // Pins match packages from a source; Add/Remove Programs-only entries can't be pinned.
        PinButton.Visibility = Show(item.Source.Length > 0 || item.IsPinned);
        PinButton.Content = item.IsPinned ? "Unpin" : "Pin";
        ScopeBox.SelectedIndex = (int)App.Settings.DefaultInstallScope;
        DetailPublisher.Text = DetailDescription.Text = DetailLicense.Text = DetailNotes.Text = "";
        DetailDescription.Text = "Loading…";
        DetailHomepage.Visibility = DetailNotesHeader.Visibility = Visibility.Collapsed;
        VersionBox.ItemsSource = null;

        PackageDetails details;
        try
        {
            details = await App.Winget.GetDetailsAsync(item);
        }
        catch (Exception ex)
        {
            if (request == _detailsRequest)
                DetailDescription.Text = $"Couldn't load details: {ex.Message}";
            return;
        }
        if (request != _detailsRequest)
            return; // the selection moved on while this loaded

        DetailPublisher.Text = details.Publisher is null ? "" : $"by {details.Publisher}";
        DetailDescription.Text = details.Description ?? "No description.";
        DetailLicense.Text = details.License is null ? "" : $"Licence: {details.License}";
        if (Uri.TryCreate(details.Homepage, UriKind.Absolute, out var home) && home.Scheme is "https" or "http")
        {
            DetailHomepage.NavigateUri = home;
            DetailHomepage.Visibility = Visibility.Visible;
        }
        DetailNotes.Text = details.ReleaseNotes ?? "";
        DetailNotesHeader.Visibility = Show(details.ReleaseNotes is not null);
        VersionBox.ItemsSource = details.Versions;
        VersionBox.SelectedIndex = details.Versions.Count > 0 ? 0 : -1; // newest first
    }

    private void UpdateButtons()
    {
        var selected = Selected;
        InstallSelectedButton.IsEnabled = selected.Any(p => !p.IsInstalled);
        UpdateSelectedButton.IsEnabled = selected.Any(p => p.CanUpdate);
        UninstallSelectedButton.IsEnabled = selected.Count > 0;
        UpdateAllButton.IsEnabled = App.Installed.UpdateCount > 0;
    }

    private void InstallSelected_Click(object sender, RoutedEventArgs e) =>
        Enqueue(Selected.Where(p => !p.IsInstalled), p => App.Install(p, null, App.Settings.DefaultInstallScope));

    private void UpdateSelected_Click(object sender, RoutedEventArgs e) =>
        Enqueue(Selected.Where(p => p.CanUpdate), App.Upgrade);

    private void UpdateAll_Click(object sender, RoutedEventArgs e) =>
        Enqueue(App.Installed.Items.Where(p => p.CanUpdate), App.Upgrade);

    private async void UninstallSelected_Click(object sender, RoutedEventArgs e) => await UninstallAsync(Selected);

    private async void UninstallOne_Click(object sender, RoutedEventArgs e) => await UninstallAsync(Selected.Take(1).ToList());

    private void UpdateOne_Click(object sender, RoutedEventArgs e) => Enqueue(Selected.Take(1), App.Upgrade);

    private void InstallOne_Click(object sender, RoutedEventArgs e)
    {
        var scope = (InstallScope)Math.Max(0, ScopeBox.SelectedIndex);
        var version = VersionBox.SelectedItem as string;
        Enqueue(Selected.Take(1), p => App.Install(p, version, scope));
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = App.Installed.RefreshAsync();

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is not { } item)
            return;
        PinButton.IsEnabled = false;
        var result = item.IsPinned ? await WingetCli.UnpinAsync(item) : await WingetCli.PinAsync(item);
        PinButton.IsEnabled = true;
        ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
        if (result.Success)
            await App.Installed.RefreshAsync();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"winget-packages-{DateTime.Now:yyyy-MM-dd}" };
        picker.FileTypeChoices.Add("winget package list", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Window));
        if (await picker.PickSaveFileAsync() is not { } file)
            return;
        LoadingBar.Visibility = Visibility.Visible;
        var result = await WingetCli.ExportAsync(file.Path, includeVersions: false);
        LoadingBar.Visibility = Show(App.Installed.IsLoading);
        ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error, result.Message);
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Window));
        if (await picker.PickSingleFileAsync() is not { } file)
            return;
        await ImportAsync(file.Path);
    }

    /// <summary>Reads a winget export file, looks each package up, and after one confirmation queues the ones not installed yet.</summary>
    internal async Task ImportAsync(string path)
    {
        if (_confirming)
            return;
        IReadOnlyList<(ImportEntry Entry, PackageItem? Package)> resolved;
        LoadingBar.Visibility = Visibility.Visible;
        try
        {
            var entries = ImportFile.Parse(await File.ReadAllTextAsync(path));
            ShowStatus(InfoBarSeverity.Informational, $"Looking up {entries.Count} packages…");
            resolved = await App.Winget.ResolveAsync(entries);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowStatus(InfoBarSeverity.Error, ex.Message);
            return;
        }
        finally
        {
            LoadingBar.Visibility = Show(App.Installed.IsLoading);
        }

        var toInstall = resolved.Where(r => r.Package is { IsInstalled: false }).ToList();
        var installed = resolved.Count(r => r.Package is { IsInstalled: true });
        var missing = resolved.Where(r => r.Package is null).Select(r => r.Entry.Id).ToList();
        var summary = $"{toInstall.Count} to install, {installed} already installed"
                      + (missing.Count > 0 ? $", {missing.Count} not found: {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? "…" : "")}" : ".");
        var name = Path.GetFileName(path);
        if (toInstall.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Informational, $"Nothing to install from {name}: {summary}");
            return;
        }

        _confirming = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = toInstall.Count == 1 ? $"Install {toInstall[0].Package!.Name} from {name}?" : $"Install {toInstall.Count} packages from {name}?",
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = summary + Environment.NewLine + Environment.NewLine
                               + string.Join(Environment.NewLine, toInstall.Select(r => $"{r.Package!.Name} {r.Entry.Version ?? r.Package.AvailableVersion}")),
                    },
                },
                PrimaryButtonText = "Install",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                foreach (var (entry, package) in toInstall)
                    App.Install(package!, entry.Version, App.Settings.DefaultInstallScope);
                ShowStatus(InfoBarSeverity.Informational, $"{toInstall.Count} installs queued. Progress is on the Activity page.");
            }
            else
            {
                Status.IsOpen = false; // drop the "Looking up…" message
            }
        }
        finally
        {
            _confirming = false;
        }
    }

    private async Task UninstallAsync(List<PackageItem> packages)
    {
        // WinUI allows one ContentDialog at a time; a second ShowAsync throws, and from an async void handler that ends the app.
        if (packages.Count == 0 || _confirming)
            return;
        _confirming = true;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = packages.Count == 1 ? $"Uninstall {packages[0].Name}?" : $"Uninstall {packages.Count} packages?",
            Content = string.Join(Environment.NewLine, packages.Take(15).Select(p => p.Name))
                      + (packages.Count > 15 ? $"{Environment.NewLine}…and {packages.Count - 15} more" : ""),
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                Enqueue(packages, App.Uninstall);
        }
        finally
        {
            _confirming = false;
        }
    }

    private void Enqueue(IEnumerable<PackageItem> packages, Action<PackageItem> enqueue)
    {
        var count = 0;
        foreach (var p in packages.ToList())
        {
            enqueue(p);
            count++;
        }
        if (count > 0)
            ShowStatus(InfoBarSeverity.Informational, count == 1 ? "Queued. Progress is on the Activity page." : $"{count} operations queued. Progress is on the Activity page.");
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
