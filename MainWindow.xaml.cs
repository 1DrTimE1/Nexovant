using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Nexovant.Core;

namespace Nexovant;

public partial class MainWindow : Window
{
    readonly ObservableCollection<CategoryItem> items = [];
    readonly bool isAdmin = Elevation.IsAdmin;
    bool busy;

    public MainWindow()
    {
        InitializeComponent();

        foreach (var category in CategoryCatalog.Create())
        {
            var item = new CategoryItem(category, isAdmin);
            item.SelectionChanged += UpdateCleanButton;
            items.Add(item);
        }
        CategoryList.ItemsSource = items;

        if (isAdmin)
        {
            AdminButton.Visibility = Visibility.Collapsed;
            Title += " (administratör)";
        }

        UpdateDiskInfo();
        StatusText.Text = "Klicka på Skanna för att se hur mycket utrymme som kan frigöras. Inget tas bort utan att du bekräftar.";
        UpdateCleanButton();

        VersionText.Text = $"v{Updater.CurrentVersion}";
        Loaded += async (_, _) => await CheckForUpdateAsync();
    }

    UpdateInfo? pendingUpdate;

    async Task CheckForUpdateAsync()
    {
        _ = Task.Run(Updater.CleanupOldVersion);
        try
        {
            pendingUpdate = await Updater.CheckAsync();
        }
        catch (Exception)
        {
            return; // offline or GitHub unreachable: just try again next start
        }
        if (pendingUpdate is null) return;

        UpdateText.Text = Updater.CanSelfUpdate
            ? $"En ny version av Nexovant finns: v{pendingUpdate.Version} (du har v{Updater.CurrentVersion})."
            : $"En ny version finns: v{pendingUpdate.Version}. Den här utvecklarversionen uppdateras inte automatiskt.";
        UpdateButton.Visibility = Updater.CanSelfUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateBanner.Visibility = Visibility.Visible;
    }

    async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (pendingUpdate is null) return;
        UpdateButton.IsEnabled = false;
        UpdateBar.Visibility = Visibility.Visible;
        UpdateText.Text = $"Laddar ner v{pendingUpdate.Version}…";
        try
        {
            await Updater.InstallAsync(pendingUpdate, new Progress<double>(p => UpdateBar.Value = p));
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"Uppdateringen misslyckades: {ex.Message}";
            UpdateBar.Visibility = Visibility.Collapsed;
            UpdateButton.IsEnabled = true;
        }
    }

    void UpdateDiskInfo()
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;
        var drive = new DriveInfo(systemDrive);
        DiskText.Text = $"Disk {systemDrive.TrimEnd('\\')}   {Format.Bytes(drive.AvailableFreeSpace)} ledigt av {Format.Bytes(drive.TotalSize)}";
        DiskBar.Value = 100.0 * (drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize;
    }

    long SelectedBytes => items.Sum(i => i.SelectedBytes);

    void UpdateCleanButton()
    {
        var bytes = SelectedBytes;
        CleanButton.Content = bytes > 0 ? $"Rensa valda ({Format.Bytes(bytes)})" : "Rensa valda";
        CleanButton.IsEnabled = !busy && bytes > 0;
    }

    void SetBusy(bool value)
    {
        busy = value;
        WorkBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !value;
        AdminButton.IsEnabled = !value;
        UpdateCleanButton();
    }

    async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    async Task ScanAsync()
    {
        SetBusy(true);
        foreach (var item in items.Where(i => i.IsAvailable))
        {
            item.SetScanning();
            StatusText.Text = $"Skannar: {item.Name}…";
            try
            {
                item.SetResult(await Task.Run(() => Scanner.Scan(item.Category, CancellationToken.None)));
            }
            catch (Exception ex)
            {
                item.SetError(ex.Message);
            }
        }
        var total = items.Where(i => i.Result is not null).Sum(i => i.Result!.TotalBytes);
        StatusText.Text = $"Skanningen är klar. {Format.Bytes(total)} kan frigöras. Bocka i kategorier, eller klicka på \"Välj filer…\" för att välja enskilda filer.";
        SetBusy(false);
    }

    async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = items.Where(i => i.SelectedBytes > 0).ToList();
        if (selected.Count == 0) return;

        var message = "Följande kommer att tas bort:\n\n"
            + string.Join("\n", selected.Select(i => i.Category.Kind == CategoryKind.RecycleBin
                ? $"•  {i.Name}  –  {Format.Bytes(i.SelectedBytes)}"
                : $"•  {i.Name}  –  {i.SelectedCount:N0} filer, {Format.Bytes(i.SelectedBytes)}"))
            + $"\n\nTotalt: {Format.Bytes(SelectedBytes)}"
            + "\n\nFiler som används av program hoppas automatiskt över.";
        if (selected.Any(i => i.Category.Kind == CategoryKind.RecycleBin))
            message += "\n\nObs! Papperskorgen töms permanent.";
        message += "\n\nVill du fortsätta?";

        if (MessageBox.Show(this, message, "Nexovant", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        long freed = 0;
        int failed = 0;
        foreach (var item in selected)
        {
            StatusText.Text = $"Rensar: {item.Name}…";
            try
            {
                var result = await Task.Run(() => Cleaner.Clean(item.Result!, CancellationToken.None));
                freed += result.FreedBytes;
                failed += result.Failed;
            }
            catch (Exception ex)
            {
                item.SetError(ex.Message);
            }
        }
        UpdateDiskInfo();
        SetBusy(false);

        await ScanAsync();
        StatusText.Text = $"Klart! {Format.Bytes(freed)} frigjordes."
            + (failed > 0 ? $" {failed:N0} filer användes av andra program och lämnades kvar." : "");
    }

    void ShowDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CategoryItem { Result: { } result } item })
        {
            new DetailsWindow(result) { Owner = this }.ShowDialog();
            item.RefreshSelection();
        }
    }

    void AdminButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // The user declined the UAC prompt; keep running normally.
        }
    }
}
