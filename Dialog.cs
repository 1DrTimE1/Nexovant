using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexovant;

public enum DialogKind { Info, Question, Warning, Error }

/// <summary>Dark replacement for MessageBox that matches the rest of the app.</summary>
public sealed class Dialog : Window
{
    bool confirmed;

    Dialog(Window? owner, DialogKind kind, string heading, string message, string? details, string confirmText, string? cancelText, bool danger)
    {
        WindowTheme.Apply(this);
        Title = "Nexovant";
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 480;
        ShowInTaskbar = owner is null;

        var (glyph, color) = kind switch
        {
            DialogKind.Question => ("", "AccentBrush"),
            DialogKind.Warning => ("", "WarnBrush"),
            DialogKind.Error => ("", "DangerBrush"),
            _ => ("", "AccentBrush"),
        };

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = Res<FontFamily>("IconFont"),
            FontSize = 26,
            Foreground = Res<Brush>(color),
            Margin = new Thickness(0, 2, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res<Brush>("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Res<Brush>("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            LineHeight = 20,
        });
        if (!string.IsNullOrEmpty(details))
        {
            text.Children.Add(new Border
            {
                Style = Res<Style>("Card"),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 14, 0, 0),
                Child = new TextBlock
                {
                    Text = details,
                    Foreground = Res<Brush>("TextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22,
                },
            });
        }

        var body = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        body.Children.Add(icon);
        body.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, Style = Res<Style>("SecondaryButton"), IsCancel = true, Padding = new Thickness(18, 8, 18, 8) };
            buttons.Children.Add(cancel);
        }
        var ok = new Button
        {
            Content = confirmText,
            Style = Res<Style>(danger ? "DangerButton" : "PrimaryButton"),
            IsDefault = true,
            IsCancel = cancelText is null,
            Padding = new Thickness(18, 8, 18, 8),
        };
        ok.Click += (_, _) => { confirmed = true; Close(); };
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        root.Children.Add(body);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => ok.Focus();
    }

    static T Res<T>(string key) => (T)Application.Current.Resources[key];

    /// <summary>Asks a yes/no question. Returns true if the user pressed the confirm button.</summary>
    public static bool Confirm(Window? owner, string heading, string message, string? details, string confirmText, bool danger = false)
    {
        var dialog = new Dialog(owner, DialogKind.Question, heading, message, details, confirmText, "Avbryt", danger);
        dialog.ShowDialog();
        return dialog.confirmed;
    }

    public static void Show(Window? owner, DialogKind kind, string heading, string message, string? details = null) =>
        new Dialog(owner, kind, heading, message, details, "OK", null, false).ShowDialog();
}
