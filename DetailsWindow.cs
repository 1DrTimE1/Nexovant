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
        WindowTheme.Apply(this);
        Title = $"Välj filer – {result.Category.Name}";
        Width = 1040;
        Height = 700;
        MinWidth = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

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
            Text = "Bocka i de filer du vill ta bort. Avbockade filer lämnas kvar. Tips: markera flera rader och tryck Mellanslag för att växla, eller dubbelklicka för att visa filen i Utforskaren.",
            Foreground = Res<Brush>("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14),
        };

        // Toolbar: search + select all / none for the visible rows.
        search = new TextBox { Tag = "Sök efter fil eller mapp…", MinWidth = 280, Height = 38, VerticalAlignment = VerticalAlignment.Center };
        search.TextChanged += (_, _) => view.Refresh();
        var searchIcon = new TextBlock
        {
            Text = "",
            FontFamily = Res<FontFamily>("IconFont"),
            Foreground = Res<Brush>("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var selectAll = Button("", "Markera alla", (_, _) => SetVisible(true));
        var selectNone = Button("", "Avmarkera alla", (_, _) => SetVisible(false));
        var openFolder = Button("", "Visa i Utforskaren", (_, _) => OpenInExplorer());

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(selectAll);
        buttons.Children.Add(selectNone);
        buttons.Children.Add(openFolder);
        DockPanel.SetDock(buttons, Dock.Right);
        DockPanel.SetDock(searchIcon, Dock.Left);
        toolbar.Children.Add(buttons);
        toolbar.Children.Add(searchIcon);
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
            RowHeight = 34,
            EnableRowVirtualization = true,
        };
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        grid.Columns.Add(CheckBoxColumn());
        grid.Columns.Add(TextColumn("Storlek", nameof(FoundFile.SizeText), 100, sortBy: nameof(FoundFile.Size), alignRight: true));
        grid.Columns.Add(TextColumn("Namn", nameof(FoundFile.Name), 240));
        grid.Columns.Add(TextColumn("Ändrad", nameof(FoundFile.LastWrite), 140, format: "yyyy-MM-dd HH:mm"));
        grid.Columns.Add(TextColumn("Mapp", nameof(FoundFile.Folder), new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.PreviewKeyDown += Grid_PreviewKeyDown;
        grid.MouseDoubleClick += (_, e) =>
        {
            // Only a double-click on a row, not on a column header or the scrollbar.
            if (e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) is not null)
                OpenInExplorer();
        };

        summary = new TextBlock { Foreground = Res<Brush>("TextBrush"), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        var done = Button("", "Klar", (_, _) => Close(), primary: true);
        done.IsDefault = true;
        var footerContent = new DockPanel();
        DockPanel.SetDock(done, Dock.Right);
        footerContent.Children.Add(done);
        footerContent.Children.Add(summary);
        var footer = new Border { Style = Res<Style>("Card"), Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(18, 12, 18, 12), Child = footerContent };

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
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

    static Button Button(string glyph, string text, RoutedEventHandler onClick, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Res<FontFamily>("IconFont"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = content,
            Style = Res<Style>(primary ? "PrimaryButton" : "SecondaryButton"),
            Padding = new Thickness(14, 8, 14, 8),
        };
        System.Windows.Automation.AutomationProperties.SetName(b, text); // the content is an icon + text panel
        b.Click += onClick;
        return b;
    }

    static DataGridTextColumn TextColumn(string header, string path, DataGridLength width, string? sortBy = null, string? format = null, bool alignRight = false)
    {
        // Long names and paths are cut with "…" and shown in full as a tooltip.
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(path) { StringFormat = format }));
        if (alignRight) style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { StringFormat = format },
            SortMemberPath = sortBy ?? path,
            Width = width,
            ElementStyle = style,
        };
    }

    static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
            if (current is not Visual) return null; // e.g. a Run inside a TextBlock
        }
        return null;
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
            Width = 84,
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
        summary.Text = $"Valt att ta bort:  {count:N0} av {result.Files.Count:N0} filer  ·  {Format.Bytes(bytes)}";
    }
}
