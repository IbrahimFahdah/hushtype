using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Hushtype.App;

public partial class App
{
    private Task updateTask = Task.CompletedTask;
    private string? updateSource;
    private void ConfigureUpdates(ContextMenu menu)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "update-source.json");
        try
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var value = document.RootElement.GetProperty("Url").GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
                        throw new InvalidDataException("Update source must be an HTTPS URL without credentials.");
                    updateSource = value;
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Update source unavailable"); }
        var check = new MenuItem { Header = "Check for updates", IsEnabled = updateSource is not null };
        check.Click += (_, _) => { if (updateTask.IsCompleted) updateTask = CheckUpdatesAsync(manual: true); };
        menu.Items.Add(check);
    }

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (updateSource is null || quitting || (!manual && !settings.AutomaticUpdates)) return;
        try
        {
            var uri = new Uri(updateSource);
            var manager = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? new UpdateManager(new GithubSource(updateSource, null, false)) : new UpdateManager(updateSource);
            if (!manager.IsInstalled) { if (manual) Notify("App updates are available after installing Hushtype with Setup."); return; }
            var update = await manager.CheckForUpdatesAsync().WaitAsync(lifetime.Token);
            if (update is null) { if (manual) Notify("Hushtype is up to date."); return; }
            await manager.DownloadUpdatesAsync(update, cancelToken: lifetime.Token);
            Notify("A Hushtype update is ready. It will be applied the next time you start the app.");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Log.Warning(ex, "Update check failed"); if (manual) Notify("Could not check for updates. Dictation is still available offline."); }
    }
}
