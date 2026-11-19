using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OstrivTranslate.Core;

namespace OstrivTranslate.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private TranslationProject? project;
    private string? projectPath;
    private List<EntryRow> rows = [];
    private ICollectionView? view;
    private readonly FontCoverage fonts = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool initialized, dirty, busy;
    private EntryRow? selected;
    private BuildReceipt? lastBuild;
    private string activeLanguage = "";
    public EntryRow? Selected { get => selected; private set { selected = value; Changed(nameof(Selected)); Changed(nameof(CanEdit)); RefreshDetails(); } }
    public bool CanEdit => project is not null && activeLanguage.Length > 0 && Selected is not null;
    public bool HasProject => project is not null;
    public bool CanBuild => project is not null && activeLanguage.Length > 0 && project.SourceHash == Extractor.SupportedHash;
    private BuildMode Mode => ModeBox.SelectedIndex == 1 ? BuildMode.Strict : BuildMode.Extended;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MainWindow()
    {
        InitializeComponent(); DataContext = this;
        UiLanguageBox.SelectedValue = L.Language;
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); RefreshFilter(); RefreshDetails(); };
        initialized = true; RefreshLabels();
        PreviewKeyDown += (_, e) =>
        {
            if (busy || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
            if (e.Key == System.Windows.Input.Key.S) { SaveProject((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.F) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        };
    }
    private void SetProject(TranslationProject next, string? path = null)
    {
        project = next; projectPath = path; dirty = false; lastBuild = null; LaunchButton.IsEnabled = false;
        Changed(nameof(HasProject)); Changed(nameof(CanBuild));
        rows = next.Entries.Select(e => new EntryRow(e, () => activeLanguage, () => Mode, MarkDirty)).ToList();
        EntriesGrid.ItemsSource = rows; view = CollectionViewSource.GetDefaultView(rows); view.Filter = Filter;
        ReloadLanguages(); fonts.Load(next.SourcePath); RefreshLabels(); RefreshFilter();
        EntriesGrid.SelectedItem = view.Cast<EntryRow>().FirstOrDefault();
    }
    private void ReloadLanguages()
    {
        var previous = activeLanguage;
        TargetLanguageBox.ItemsSource = null; TargetLanguageBox.ItemsSource = project?.Languages;
        TargetLanguageBox.SelectedItem = project?.Languages.FirstOrDefault(l => l.Id == previous) ?? project?.Languages.FirstOrDefault();
        Changed(nameof(CanEdit));
    }
    private void MarkDirty()
    {
        dirty = true; RefreshTitle();
        refreshTimer.Stop(); refreshTimer.Start();
    }
    private void RefreshTitle() => Title = L.T("Title") + (projectPath is null ? "" : " · " + Path.GetFileName(projectPath)) + (dirty ? " *" : "");
    private bool Filter(object value)
    {
        if (value is not EntryRow row) return false;
        var search = SearchBox.Text.Trim();
        if (search.Length > 0 && !row.Entry.Original.Contains(search, StringComparison.OrdinalIgnoreCase) && !row.Translation.Contains(search, StringComparison.OrdinalIgnoreCase) && !row.Entry.Context.Contains(search, StringComparison.OrdinalIgnoreCase) && !row.Entry.Id.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
        return FilterBox.SelectedIndex switch
        {
            1 => row.Translation.Length == 0,
            2 => row.HasErrors,
            3 => row.Translation.Length > row.Entry.Original.Length,
            4 => !row.Entry.Confirmed && !row.Entry.Approved,
            5 => row.Entry.Confirmed || row.Entry.Approved,
            _ => true
        };
    }
    private void RefreshFilter()
    {
        if (!initialized) return;
        // Do not remove a row while the user is typing in it. Apply translation-dependent
        // filters when selection/search/filter changes instead, so the editor keeps focus.
        if (!TranslationEditor.IsKeyboardFocusWithin) view?.Refresh();
        if (project is null) { SummaryLabel.Text = ""; return; }
        SummaryLabel.Text = string.Format(L.T("Summary"), rows.Count(r => r.Entry.Confirmed || r.Entry.Approved), rows.Count(r => !r.Entry.Confirmed && !r.Entry.Approved), rows.Count(r => (r.Entry.Confirmed || r.Entry.Approved) && r.Translation.Length > 0), rows.Count(r => r.Entry.Confirmed || r.Entry.Approved), rows.Count(r => r.HasErrors), view?.Cast<object>().Count() ?? 0);
    }
    private void RefreshDetails()
    {
        if (!initialized) return;
        var row = Selected;
        DetailsHeading.Visibility = EntryEditors.Visibility = EntryMetrics.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        var candidates = EntriesGrid.SelectedItems.Cast<EntryRow>().Where(r => !r.Entry.Confirmed).ToArray();
        ApproveBox.IsEnabled = candidates.Length > 0;
        ApproveBox.Visibility = candidates.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApproveBox.IsChecked = candidates.Length == 0 ? false : candidates.All(r => r.Entry.Approved) ? true : candidates.Any(r => r.Entry.Approved) ? null : false;
        ApproveBox.SetResourceReference(ContentControl.ContentProperty, EntriesGrid.SelectedItems.Count > 1 ? "ApproveSelected" : "Approve");
        if (row is null) { ContextLabel.Text = ""; BytesLabel.Text = ""; IssuesLabel.Text = ""; return; }
        ContextLabel.Text = string.Format(L.T("EntryInfo"), row.Entry.Id, row.Entry.Offset, row.Entry.ReferenceCount, row.Entry.Context);
        BytesLabel.Text = string.Format(L.T("ByteInfo"), row.Entry.Original.Length, row.Entry.OriginalBytes, row.Translation.Length, row.Translation.Length * 2);
        var messages = row.Issues.Select(i => L.T(i.Code) + (i.Detail.Length > 0 ? ": " + i.Detail : "")).ToList();
        if (row.Translation.Length > 0)
        {
            if (!fonts.Available) messages.Add(L.T("FontUnavailable"));
            var missing = fonts.Missing(row.Translation);
            if (missing.Count > 0) messages.Add(L.T("FontMissing") + ": " + string.Join("; ", missing));
        }
        IssuesLabel.Text = string.Join("\n", messages);
    }
    private void RefreshLabels()
    {
        foreach (var row in rows) row.Refresh();
        SourceLabel.Text = project is null ? L.T("NoProject") : string.Format(L.T("SourceInfo"), project.SourcePath, project.SourceHash);
        ProfileWarning.Visibility = project is not null && project.SourceHash != Extractor.SupportedHash ? Visibility.Visible : Visibility.Collapsed;
        StatusLabel.Text = L.T("Ready"); RefreshTitle(); RefreshFilter(); RefreshDetails();
    }
    private void UiLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || UiLanguageBox.SelectedValue is not string language) return;
        L.Set(language); App.Preferences.UiLanguage = language; RefreshLabels(); SavePreferences();
    }
    private void TargetLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        activeLanguage = (TargetLanguageBox.SelectedItem as TargetLanguage)?.Id ?? "";
        foreach (var row in rows) row.Refresh();
        Changed(nameof(CanEdit)); Changed(nameof(CanBuild)); RefreshFilter(); RefreshDetails();
    }
    private void Entry_Changed(object sender, SelectionChangedEventArgs e) => Selected = EntriesGrid.SelectedItems.Count == 1 ? EntriesGrid.SelectedItems[0] as EntryRow : null;
    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        // Snapshot the selection: approving rows can remove them from the current filter.
        var candidates = EntriesGrid.SelectedItems.Cast<EntryRow>().Where(r => !r.Entry.Confirmed).ToArray();
        if (candidates.Length == 0) return;
        // A mixed selection becomes fully approved in one click; another click revokes it.
        var approve = !candidates.All(r => r.Entry.Approved);
        foreach (var row in candidates)
        {
            row.Entry.Approved = approve;
            row.Refresh();
        }
        MarkDirty(); RefreshFilter(); RefreshDetails();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (initialized) { view?.Refresh(); RefreshFilter(); } }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) { view?.Refresh(); RefreshFilter(); } }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) { foreach (var row in rows) row.Refresh(); RefreshFilter(); RefreshDetails(); } }
    private void SavePreferences()
    {
        try { App.SavePreferences(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLabel.Text = L.T("PreferencesFailed"); }
    }
    private string? PickSource()
    {
        var dialog = new OpenFileDialog { Filter = L.T("ExeFiles") + "|*.exe", Title = L.T("OpenGame"), FileName = "ostriv.exe" };
        var folder = Path.GetDirectoryName(App.Preferences.GamePath);
        if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
    private async Task Run(Func<Task> operation)
    {
        if (busy) return;
        busy = true; Workspace.IsEnabled = false; BusyOverlay.Visibility = Visibility.Visible;
        try { await operation(); }
        catch (Exception ex) { ShowError(ex); }
        finally { busy = false; Workspace.IsEnabled = true; BusyOverlay.Visibility = Visibility.Collapsed; }
    }
    private void ShowError(Exception ex) => Dialogs.Message(this, L.T("Error"), ex is TranslationException te ? L.T(te.Code) + (te.Detail.Length > 0 ? "\n" + te.Detail : "") : L.T("Error") + "\n" + ex.Message);
    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var answer = Dialogs.Ask(this, L.T("Unsaved"), true);
        return answer == false || answer == true && SaveProject(false);
    }
    private async void OpenGame_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var source = PickSource(); if (source is null) return;
        await Run(async () =>
        {
            var extracted = await Task.Run(() => Extractor.Extract(source));
            SetProject(extracted.Project); App.Preferences.GamePath = source; SavePreferences();
            var language = Dialogs.Language(this);
            if (language is not null) { project!.Languages.Add(language); ReloadLanguages(); MarkDirty(); }
        });
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = L.T("ProjectFiles") + "|*.json", Title = L.T("OpenProject") };
        if (dialog.ShowDialog(this) != true) return;
        await Run(async () =>
        {
            var saved = await Task.Run(() => ProjectStore.Load(dialog.FileName));
            Extraction? fresh = null;
            if (File.Exists(saved.SourcePath)) fresh = await Task.Run(() => Extractor.Extract(saved.SourcePath));
            if (fresh is not null && fresh.Project.SourceHash == saved.SourceHash)
                SetProject(ProjectStore.BindToSource(saved, fresh), dialog.FileName);
            else
            {
                Dialogs.Message(this, L.T("Rescan"), L.T("SourceChangedPrompt"));
                var source = PickSource(); if (source is null) return;
                fresh = await Task.Run(() => Extractor.Extract(source));
                await Migrate(saved, fresh, dialog.FileName);
            }
        });
    }
    private bool SaveProject(bool saveAs)
    {
        if (project is null) return false;
        var path = projectPath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog { Filter = L.T("ProjectFiles") + "|*.ostriv.json", DefaultExt = ".ostriv.json", FileName = path is null ? "Ostriv.ostriv.json" : Path.GetFileName(path), Title = L.T("Save") };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try { ProjectStore.Save(path, project); projectPath = path; dirty = false; RefreshTitle(); StatusLabel.Text = L.T("Saved"); return true; }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    private void Save_Click(object sender, RoutedEventArgs e) => SaveProject(false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveProject(true);
    private void AddLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (project is null) return;
        var language = Dialogs.Language(this); if (language is null) return;
        if (project.Languages.Any(l => l.Id.Equals(language.Id, StringComparison.OrdinalIgnoreCase))) { ShowError(new TranslationException("InvalidLanguage")); return; }
        project.Languages.Add(language); ReloadLanguages(); TargetLanguageBox.SelectedItem = language; MarkDirty();
    }
    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (project is null) return;
        var source = PickSource(); if (source is null) return;
        await Run(async () => { var fresh = await Task.Run(() => Extractor.Extract(source)); await Migrate(project, fresh, projectPath); });
    }
    private Task Migrate(TranslationProject old, Extraction fresh, string? path)
    {
        if (old.SourceHash == fresh.Project.SourceHash)
        {
            SetProject(ProjectStore.BindToSource(old, fresh), path); MarkDirty(); return Task.CompletedTask;
        }
        fresh.Project.Languages = old.Languages.Select(l => new TargetLanguage { Id = l.Id, Name = l.Name }).ToList();
        var changes = Importer.Migration(old, fresh.Project);
        if (Dialogs.Review(this, fresh.Project, changes)) { SetProject(fresh.Project, null); MarkDirty(); }
        return Task.CompletedTask;
    }
    private void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (project is null) return;
        var dialog = new OpenFileDialog { Filter = L.T("CsvFiles") + "|*.csv" }; if (dialog.ShowDialog(this) != true) return;
        try
        {
            var changes = CsvExchange.Preview(File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true)), project);
            if (Dialogs.Review(this, project, changes)) { ReloadLanguages(); foreach (var row in rows) row.Refresh(); MarkDirty(); RefreshFilter(); RefreshDetails(); }
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (project is null) return;
        var dialog = new SaveFileDialog { Filter = L.T("CsvFiles") + "|*.csv", DefaultExt = ".csv", FileName = "Ostriv.csv" }; if (dialog.ShowDialog(this) != true) return;
        try { AtomicFiles.Write(dialog.FileName, CsvExchange.Export(project), project.SourcePath); StatusLabel.Text = L.T("Exported"); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        if (project is null || activeLanguage.Length == 0) return;
        var firstError = rows.FirstOrDefault(r => r.HasErrors);
        if (firstError is not null)
        {
            FilterBox.SelectedIndex = 2; SearchBox.Clear(); EntriesGrid.SelectedItem = firstError; EntriesGrid.ScrollIntoView(firstError);
            ShowError(new TranslationException(firstError.Issues[0].Code, firstError.Entry.Id)); return;
        }
        var text = string.Join("", rows.Where(r => r.Translation.Length > 0).Select(r => r.Translation));
        if ((!fonts.Available || fonts.Missing(text).Count > 0) && Dialogs.Ask(this, L.T("FontWarningConfirm")) != true) return;
        var dialog = new SaveFileDialog { Title = L.T("SaveExe"), Filter = L.T("ExeFiles") + "|*.exe", DefaultExt = ".exe", InitialDirectory = Path.GetDirectoryName(project.SourcePath), FileName = "" };
        if (dialog.ShowDialog(this) != true) return;
        var language = activeLanguage; var mode = Mode;
        await Run(async () =>
        {
            lastBuild = await Task.Run(() => Builder.BuildFile(project, language, mode, dialog.FileName)); LaunchButton.IsEnabled = true;
            var message = string.Format(L.T("BuildSuccess"), lastBuild.Count, lastBuild.OutputPath);
            if (lastBuild.ReportError is not null) message += "\n\n" + L.T("ReportFailed") + "\n" + lastBuild.ReportError;
            Dialogs.Message(this, L.T("Build"), message);
        });
    }
    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (lastBuild is null || project is null) return;
        try
        {
            if (Extractor.Hash(File.ReadAllBytes(lastBuild.OutputPath)) != lastBuild.OutputHash) throw new TranslationException("LaunchChanged");
            Process.Start(new ProcessStartInfo(lastBuild.OutputPath) { WorkingDirectory = Path.GetDirectoryName(project.SourcePath), UseShellExecute = false });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (busy || !ConfirmDiscard()) e.Cancel = true; }

    // Repeatable native WPF smoke test. No game files are written or game process launched.
    public async Task SmokeTest(string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var extracted = await Task.Run(() => Extractor.Extract(App.Preferences.GamePath));
            extracted.Project.Languages = [new() { Id = "be", Name = "Беларуская" }, new() { Id = "pl", Name = "Polski" }];
            SetProject(extracted.Project);
            var well = rows.First(r => r.Entry.Original == "Колодязь" && r.Entry.Confirmed);
            well.Translation = "Каменны калодзеж з чыстай вадой";
            TargetLanguageBox.SelectedIndex = 1; well.Translation = "Kamienna studnia z czystą wodą";
            TargetLanguageBox.SelectedIndex = 0;
            foreach (var ui in new[] { "en", "be", "ru" })
            {
                UiLanguageBox.SelectedValue = ui; UpdateLayout();
                if (well.Translation != "Каменны калодзеж з чыстай вадой" || (string)Application.Current.Resources["Build"] != L.T("Build")) throw new InvalidOperationException("Language switch lost data or resources");
            }
            await SelectionSmokeTest(outputDirectory, well);
            SearchBox.Text = "Колодязь"; EntriesGrid.SelectedItem = well; RefreshDetails(); UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(this);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(outputDirectory, "editor.png"))) png.Save(stream);
            var snapshot = Path.Combine(outputDirectory, "smoke.ostriv.json"); ProjectStore.Save(snapshot, project!);
            var restored = ProjectStore.Load(snapshot);
            if (restored.Languages.Count != 2 || restored.Entries.First(e => e.Id == well.Entry.Id).Translations.Count != 2) throw new InvalidOperationException("Project roundtrip failed");
            if (!restored.Entries.Select(e => e.Approved).SequenceEqual(project!.Entries.Select(e => e.Approved))) throw new InvalidOperationException("Bulk approvals lost in project roundtrip");
            File.WriteAllText(Path.Combine(outputDirectory, "smoke-result.txt"), $"PASS: WPF loaded; 3 UI languages; 2 translation languages; bindings; project roundtrip; extended selection; mixed approval; bulk approve/revoke; filtered Select All; bulk approval persistence.\nFonts available: {fonts.Available}.\nMissing Belarusian glyphs: {string.Join("; ", fonts.Missing(well.Translation))}\nGame runtime: NOT TESTED.\n");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(outputDirectory, "smoke-result.txt"), "FAIL\n" + ex); Environment.ExitCode = 1; }
        finally { dirty = false; Close(); }
    }

    private async Task SelectionSmokeTest(string outputDirectory, EntryRow confirmed)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var candidates = rows.Where(r => !r.Entry.Confirmed && r.Entry.Original.StartsWith("Андр", StringComparison.Ordinal)).Take(3).ToArray();
        Check(candidates.Length == 3, "Missing selection test candidates");
        Check(EntriesGrid.SelectionMode == DataGridSelectionMode.Extended, "Range selection disabled");
        FilterBox.SelectedIndex = 0;
        candidates[0].Approved = true;
        EntriesGrid.SelectedItems.Clear();
        EntriesGrid.SelectedItems.Add(candidates[0]);
        EntriesGrid.SelectedItems.Add(candidates[1]);
        EntriesGrid.SelectedItems.Add(confirmed);
        var originalTranslation = confirmed.Translation;
        Check(Selected is null && !CanEdit && EntryEditors.Visibility == Visibility.Collapsed && EntryMetrics.Visibility == Visibility.Collapsed, "Multiple rows exposed a single-entry editor");
        Check(ApproveBox.IsEnabled && ApproveBox.Visibility == Visibility.Visible && ApproveBox.IsChecked is null, "Mixed approval not shown");
        foreach (var ui in new[] { "en", "ru", "be" })
        {
            UiLanguageBox.SelectedValue = ui;
            Check(EntriesGrid.SelectedItems.Count == 3 && (string)ApproveBox.Content == L.T("ApproveSelected"), "UI language switch lost selection or bulk label");
        }
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(this);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(outputDirectory, "selection.png"))) png.Save(stream);

        // Invoke the checkbox's routed action, including its real selection/filter refresh.
        ApproveBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(candidates.Take(2).All(r => r.Entry.Approved) && !candidates[2].Entry.Approved, "Approval affected unselected rows or skipped selected rows");
        Check(!confirmed.Entry.Approved && confirmed.Translation == originalTranslation, "Bulk approval changed a confirmed entry");
        EntriesGrid.SelectedItems.Clear();
        EntriesGrid.SelectedItems.Add(candidates[0]); EntriesGrid.SelectedItems.Add(candidates[1]);
        Check(ApproveBox.IsChecked == true, "Approved selection not checked");
        ApproveBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(candidates.All(r => !r.Entry.Approved), "Bulk revoke failed");

        FilterBox.SelectedIndex = 4; SearchBox.Text = "Андр";
        var displayed = EntriesGrid.Items.Cast<EntryRow>().ToArray();
        Check(displayed.Length >= 3 && displayed.Length < rows.Count, "Selection filter not applied");
        var excluded = rows.Except(displayed).Select(r => (r.Entry, r.Entry.Approved)).ToArray();
        EntriesGrid.Focus();
        System.Windows.Input.ApplicationCommands.SelectAll.Execute(null, EntriesGrid);
        Check(EntriesGrid.SelectedItems.Count == displayed.Length, "Select All missed filtered rows");
        ApproveBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(displayed.All(r => r.Entry.Approved) && EntriesGrid.Items.Count == 0, "Bulk approval skipped rows as they left the candidates filter");
        Check(excluded.All(pair => pair.Entry.Approved == pair.Approved), "Select All changed hidden rows");
        Check(dirty && ApproveBox.Visibility == Visibility.Collapsed, "Empty selection or dirty state incorrect");
        FilterBox.SelectedIndex = 5; SearchBox.Clear(); EntriesGrid.SelectedItem = confirmed;
        Check(Selected == confirmed && EntryEditors.Visibility == Visibility.Visible && CanEdit, "Single-entry editing did not recover");
    }
}
