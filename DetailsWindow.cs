using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Nexovant.Core;

namespace Nexovant;

/// <summary>Lets the user pick exactly which of a category's files should be removed.</summary>
public sealed class DetailsWindow : Window
{
    readonly CategoryScanResult result;
    readonly ICollectionView view;
    readonly DataGrid grid;
    readonly TextBox search;
    readonly TextBlock summary;

    public DetailsWindow(CategoryScanResult result)
    {
        this.result = result;
        Title = $"Välj filer – {result.Category.Name}";
        Width = 1000;
        Height = 680;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Res<Brush>("BgBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var files = result.Files.OrderByDescending(f => f.Size).ToList();
        view = CollectionViewSource.GetDefaultView(files);
        view.Filter = Matches;

        var heading = new TextBlock
        {
            Text = result.Category.Name,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res<Brush>("TextBrush"),
        };
        var hint = new TextBlock
        {
            Text = "Bocka i de filer du vill ta bort. Avbockade filer lämnas kvar. Tips: markera rader och tryck Mellanslag för att växla.",
            Foreground = Res<Brush>("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
        };

        // Toolbar: search + select all / none for the visible rows.
        search = new TextBox { Padding = new Thickness(8, 6, 8, 6), VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 260 };
        search.TextChanged += (_, _) => view.Refresh();
        var searchLabel = new TextBlock { Text = "Sök:", Foreground = Res<Brush>("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var selectAll = Button("Markera alla", (_, _) => SetVisible(true));
        var selectNone = Button("Avmarkera alla", (_, _) => SetVisible(false));
        var openFolder = Button("Öppna i Utforskaren", (_, _) => OpenInExplorer());

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(selectAll);
        buttons.Children.Add(selectNone);
        buttons.Children.Add(openFolder);
        DockPanel.SetDock(buttons, Dock.Right);
        DockPanel.SetDock(searchLabel, Dock.Left);
        toolbar.Children.Add(buttons);
        toolbar.Children.Add(searchLabel);
        toolbar.Children.Add(search);

        grid = new DataGrid
        {
            ItemsSource = view,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            SelectionMode = DataGridSelectionMode.Extended,
            RowHeight = 26,
            EnableRowVirtualization = true,
        };
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.Columns.Add(CheckBoxColumn());
        grid.Columns.Add(new DataGridTextColumn { Header = "Storlek", Binding = new Binding(nameof(FoundFile.SizeText)), SortMemberPath = nameof(FoundFile.Size), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Namn", Binding = new Binding(nameof(FoundFile.Name)), Width = 220 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Ändrad", Binding = new Binding(nameof(FoundFile.LastWrite)) { StringFormat = "yyyy-MM-dd HH:mm" }, Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Mapp", Binding = new Binding(nameof(FoundFile.Folder)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.PreviewKeyDown += Grid_PreviewKeyDown;
        grid.MouseDoubleClick += (_, _) => OpenInExplorer();

        summary = new TextBlock { Foreground = Res<Brush>("TextBrush"), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        var done = Button("Klar", (_, _) => Close(), primary: true);
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(done, Dock.Right);
        footer.Children.Add(done);
        footer.Children.Add(summary);

        var root = new DockPanel { Margin = new Thickness(20) };
        foreach (var top in new UIElement[] { heading, hint, toolbar })
        {
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
        }
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(grid);
        Content = root;

        foreach (var f in result.Files) f.PropertyChanged += File_PropertyChanged;
        Closed += (_, _) =>
        {
            foreach (var f in result.Files) f.PropertyChanged -= File_PropertyChanged;
        };
        UpdateSummary();
    }

    bool summaryPending;

    // "Markera alla" can flip thousands of files at once, so the summary is recalculated only once afterwards.
    void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (summaryPending) return;
        summaryPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            summaryPending = false;
            UpdateSummary();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    static T Res<T>(string key) => (T)Application.Current.Resources[key];

    static Button Button(string text, RoutedEventHandler onClick, bool primary = false)
    {
        var b = new Button
        {
            Content = text,
            Style = Res<Style>(primary ? "PrimaryButton" : "SecondaryButton"),
            Padding = new Thickness(14, 7, 14, 7),
        };
        b.Click += onClick;
        return b;
    }

    static DataGridTemplateColumn CheckBoxColumn()
    {
        var box = new FrameworkElementFactory(typeof(CheckBox));
        box.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(FoundFile.IsSelected))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        box.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        box.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataGridTemplateColumn
        {
            Header = "Ta bort",
            CellTemplate = new DataTemplate { VisualTree = box },
            SortMemberPath = nameof(FoundFile.IsSelected),
            Width = 70,
        };
    }

    bool Matches(object item) =>
        string.IsNullOrWhiteSpace(search?.Text) ||
        ((FoundFile)item).Path.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase);

    void SetVisible(bool selected)
    {
        foreach (FoundFile f in view) f.IsSelected = selected;
    }

    void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || grid.SelectedItems.Count == 0) return;
        var rows = grid.SelectedItems.Cast<FoundFile>().ToList();
        var newValue = !rows.All(f => f.IsSelected);
        foreach (var f in rows) f.IsSelected = newValue;
        e.Handled = true;
    }

    void OpenInExplorer()
    {
        if (grid.SelectedItem is FoundFile f && File.Exists(f.Path))
            Process.Start("explorer.exe", $"/select,\"{f.Path}\"");
    }

    void UpdateSummary()
    {
        var count = result.Files.Count(f => f.IsSelected);
        var bytes = result.Files.Where(f => f.IsSelected).Sum(f => f.Size);
        summary.Text = $"Valt att ta bort: {count:N0} av {result.Files.Count:N0} filer  ·  {Format.Bytes(bytes)}";
    }
}
