using System.Text;
using System.Windows;
using Nexovant.Core;

namespace Nexovant;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nexovant.exe --scan-report <file> : scan only (never deletes) and write a text report.
        if (e.Args.Length == 2 && e.Args[0] == "--scan-report")
        {
            File.WriteAllText(e.Args[1], BuildScanReport(), Encoding.UTF8);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    static string BuildScanReport()
    {
        var sb = new StringBuilder();
        long total = 0;
        foreach (var category in CategoryCatalog.Create())
        {
            if (category.RequiresAdmin && !Elevation.IsAdmin)
            {
                sb.AppendLine($"{category.Name}: kräver administratör");
                continue;
            }
            var r = Scanner.Scan(category, CancellationToken.None);
            total += r.TotalBytes;
            sb.AppendLine($"{category.Name}: {Format.Bytes(r.TotalBytes)} ({r.ItemCount:N0} st), för nya: {r.SkippedRecent}, appar som körs: {string.Join(", ", r.SkippedApps)}");
            foreach (var root in r.Roots)
                sb.AppendLine("    " + root.Path);
        }
        sb.AppendLine($"Totalt: {Format.Bytes(total)}");
        return sb.ToString();
    }
}
