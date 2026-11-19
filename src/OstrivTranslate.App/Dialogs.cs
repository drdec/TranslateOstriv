using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using OstrivTranslate.Core;

namespace OstrivTranslate.App;

public static class Dialogs
{
    public static bool? Ask(Window owner, string message, bool allowCancel = false)
    {
        var w = Shell(owner, L.T("Title"), 580, 220);
        var root = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        bool? answer = null;
        AddButton(buttons, L.T("Yes"), () => { answer = true; w.Close(); });
        AddButton(buttons, L.T("No"), () => { answer = false; w.Close(); });
        if (allowCancel) AddButton(buttons, L.T("Cancel"), w.Close);
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        w.Content = root; w.ShowDialog(); return answer;
    }
    public static void Message(Window owner, string title, string text)
    {
        var w = Shell(owner, title, 650, 330);
        var root = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); AddButton(buttons, L.T("Ok"), w.Close);
        root.Children.Add(new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) });
        w.Content = root; w.ShowDialog();
    }
    public static TargetLanguage? Language(Window owner)
    {
        var w = Shell(owner, L.T("AddLanguage"), 480, 290);
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = L.T("LanguageCode"), Margin = new Thickness(0, 0, 0, 6) });
        var code = new TextBox(); root.Children.Add(code);
        root.Children.Add(new TextBlock { Text = L.T("LanguageName"), Margin = new Thickness(0, 12, 0, 6) });
        var name = new TextBox(); root.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; root.Children.Add(buttons);
        TargetLanguage? result = null;
        AddButton(buttons, L.T("Ok"), () =>
        {
            if (!ProjectStore.ValidLanguageId(code.Text.Trim()) || string.IsNullOrWhiteSpace(name.Text)) { Message(w, L.T("Error"), L.T("InvalidLanguage")); return; }
            result = new TargetLanguage { Id = code.Text.Trim().ToLowerInvariant(), Name = name.Text.Trim() }; w.Close();
        });
        AddButton(buttons, L.T("Cancel"), w.Close); w.Content = root; w.ShowDialog(); return result;
    }
    public static bool Review(Window owner, TranslationProject target, List<ImportChange> changes)
    {
        if (changes.Count == 0) { Message(owner, L.T("Review"), L.T("NoImportChanges")); return true; }
        var w = Shell(owner, L.T("Review"), 1240, 650);
        var root = new DockPanel { Margin = new Thickness(20) };
        var info = new TextBlock { Text = L.T("ReviewHint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) }; DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var grid = new DataGrid { ItemsSource = changes, SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "✓", Binding = new Binding("Apply"), Width = 42 });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("TargetLanguage"), Binding = new Binding("Language"), IsReadOnly = true, Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("Status"), Binding = new Binding("Reason") { Converter = new ReasonConverter() }, IsReadOnly = true, Width = 170 });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("Original"), Binding = new Binding("Original"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("Existing"), Binding = new Binding("Existing"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = L.T("Incoming"), Binding = new Binding("Translation"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var factory = new FrameworkElementFactory(typeof(ComboBox));
        factory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Choices"));
        factory.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding("Target") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        grid.Columns.Add(new DataGridTemplateColumn { Header = L.T("Destination"), CellTemplate = new DataTemplate { VisualTree = factory }, Width = 210 });
        root.Children.Add(grid);
        var accepted = false;
        AddButton(buttons, L.T("Apply"), () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            try { Importer.Apply(target, changes); accepted = true; w.Close(); }
            catch (TranslationException ex) { Message(w, L.T("Error"), L.T(ex.Code) + "\n" + ex.Detail); }
        });
        AddButton(buttons, L.T("Cancel"), w.Close); w.Content = root; w.ShowDialog(); return accepted;
    }
    private static Window Shell(Window owner, string title, double width, double height) => new() { Owner = owner, Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 440), MinHeight = Math.Min(height, 220), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
    private static void AddButton(Panel panel, string label, Action action) { var b = new Button { Content = label, MinWidth = 85 }; b.Click += (_, _) => action(); panel.Children.Add(b); }
    private sealed class ReasonConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => L.T(value?.ToString() ?? "");
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
