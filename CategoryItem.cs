using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Nexovant.Core;

namespace Nexovant;

public sealed class CategoryItem : INotifyPropertyChanged
{
    bool? isSelected;
    string sizeText;
    string detailText;
    CategoryScanResult? result;

    public CategoryItem(CleanupCategory category, bool isAdmin)
    {
        Category = category;
        IsAvailable = !category.RequiresAdmin || isAdmin;
        isSelected = category.DefaultSelected && IsAvailable;
        sizeText = IsAvailable ? "–" : "Kräver admin";
        detailText = IsAvailable ? "Ej skannad" : "Starta som administratör";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SelectionChanged;

    public CleanupCategory Category { get; }
    public string Name => Category.Name;
    public string Description => Category.Description;
    public string? Warning => Category.Warning;
    public Visibility WarningVisibility => string.IsNullOrEmpty(Warning) ? Visibility.Collapsed : Visibility.Visible;
    public bool IsAvailable { get; }
    public double CardOpacity => IsAvailable ? 1.0 : 0.55;

    /// <summary>true = all files selected, false = none, null = some (the user picked individual files).</summary>
    public bool? IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            if (value is bool all && Result is { } r)
                foreach (var file in r.Files) file.IsSelected = all;
            Changed();
            UpdateDetailText();
            SelectionChanged?.Invoke();
        }
    }

    public string SizeText { get => sizeText; private set { sizeText = value; Changed(); } }
    public string DetailText { get => detailText; private set { detailText = value; Changed(); } }

    public CategoryScanResult? Result
    {
        get => result;
        private set { result = value; Changed(); Changed(nameof(DetailsVisibility)); }
    }

    public Visibility DetailsVisibility =>
        Result is { Files.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    bool IsRecycleBin => Category.Kind == CategoryKind.RecycleBin;

    public int SelectedCount => Result is null ? 0 : Result.Files.Count(f => f.IsSelected);

    public long SelectedBytes => Result switch
    {
        null => 0,
        _ when IsRecycleBin => isSelected == true ? Result.TotalBytes : 0,
        _ => Result.Files.Where(f => f.IsSelected).Sum(f => f.Size),
    };

    public void SetScanning()
    {
        Result = null;
        SizeText = "…";
        DetailText = "Skannar…";
    }

    public void SetResult(CategoryScanResult r)
    {
        // A fresh scan starts from the category checkbox: checked (or partly checked) selects every file.
        var selectAll = isSelected != false;
        foreach (var file in r.Files) file.IsSelected = selectAll;
        Result = r;
        isSelected = selectAll;
        Changed(nameof(IsSelected));
        SizeText = Format.Bytes(r.TotalBytes);
        UpdateDetailText();
        SelectionChanged?.Invoke();
    }

    /// <summary>Call after the user has picked individual files.</summary>
    public void RefreshSelection()
    {
        if (Result is { Files.Count: > 0 } r)
        {
            var count = SelectedCount;
            isSelected = count == 0 ? false : count == r.Files.Count ? true : null;
            Changed(nameof(IsSelected));
        }
        UpdateDetailText();
        SelectionChanged?.Invoke();
    }

    public void SetError(string message)
    {
        Result = null;
        SizeText = "Fel";
        DetailText = message;
    }

    void UpdateDetailText()
    {
        if (Result is not { } r) return;

        string text;
        if (IsRecycleBin)
            text = $"{r.ItemCount:N0} objekt";
        else if (r.Files.Count == 0)
            text = "Inget att rensa";
        else
        {
            var count = SelectedCount;
            text = $"{count:N0} av {r.Files.Count:N0} filer valda";
            if (count > 0 && count < r.Files.Count)
                text += $"\n{Format.Bytes(SelectedBytes)} valt";
        }
        if (r.SkippedApps.Count > 0)
            text += $"\nÖppna, hoppades över: {string.Join(", ", r.SkippedApps)}";
        DetailText = text;
    }

    void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
