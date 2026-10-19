using System.Diagnostics;
using YsfUtil.Updates;

namespace YsfUtil.Ui.ViewModels;

internal sealed class UpdateViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings settings;
    private readonly Func<string?> installBlock;
    private readonly Func<bool> quiet;
    private readonly Action exit;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(1) };
    private UpdateInfo? available;
    private bool busy;
    private string status = "Check for a newer version on GitHub.";

    public UpdateViewModel(AppSettings settings, Func<string?> installBlock, Func<bool> quiet, Action exit)
    {
        this.settings = settings;
        this.installBlock = installBlock;
        this.quiet = quiet;
        this.exit = exit;
        CheckCommand = new RelayCommand(async () => await CheckAsync(manual: true));
        InstallCommand = new RelayCommand(async () => await InstallAsync());
        ReleaseCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo(available?.PageUrl ?? UpdateChecker.Repository + "/releases") { UseShellExecute = true }); }
            catch { Status = "Could not open the release page."; }
        });
        timer.Tick += async (_, _) => await CheckAsync(manual: false);
    }

    public IReadOnlyList<string> Modes { get; } = ["Offer updates", "Install automatically", "Never check automatically"];
    public int ModeIndex
    {
        get => (int)settings.Updates;
        set
        {
            if (value < 0 || value >= Modes.Count || value == ModeIndex) return;
            settings.Updates = (UpdateMode)value;
            settings.Save();
            Raise();
        }
    }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => busy; private set { Set(ref busy, value); Raise(nameof(CanCheck)); Raise(nameof(CanInstall)); } }
    public bool CanCheck => !IsBusy;
    public bool HasUpdate => available != null;
    public bool CanInstall => !IsBusy && available?.DownloadUrl != null;
    public string InstallLabel => available == null ? "Update and restart" : $"Update to {available.Version.ToString(3)}";
    public RelayCommand CheckCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand ReleaseCommand { get; }

    public async Task StartAsync(bool failed)
    {
        if (failed) Status = "Update failed. ysfUtil was restarted. Please try again or run the setup manually.";
        timer.Start();
        await CheckAsync(manual: false);
    }

    private async Task CheckAsync(bool manual)
    {
        if (IsBusy || lifetime.IsCancellationRequested || (!manual && !UpdateChecker.ShouldCheck(settings.Updates, settings.LastUpdateCheckUtc, DateTime.UtcNow))) return;
        IsBusy = true;
        if (manual) Status = "Checking GitHub…";
        bool autoInstall = false;
        try
        {
            UpdateInfo? result = await UpdateChecker.FindNewerAsync(lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            if (!manual) { settings.LastUpdateCheckUtc = DateTime.UtcNow; settings.Save(); }
            available = result;
            Raise(nameof(HasUpdate)); Raise(nameof(InstallLabel));
            if (available != null)
            {
                Status = available.DownloadUrl != null ? $"Version {available.Version.ToString(3)} is ready. Settings are kept."
                    : $"Version {available.Version.ToString(3)} is available. Open its release page for a download.";
                autoInstall = !manual && settings.Updates == UpdateMode.Automatic && available.DownloadUrl != null;
            }
            else if (manual) Status = "You're up to date.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { if (manual) Status = "Could not check GitHub. Check your connection and try again."; }
        finally { IsBusy = false; }
        if (autoInstall) await InstallAsync();
    }

    private async Task InstallAsync()
    {
        if (!CanInstall || available == null || lifetime.IsCancellationRequested) return;
        string? blocked = installBlock();
        if (blocked != null) { Status = blocked; return; }
        IsBusy = true;
        try
        {
            var progress = new Progress<int>(percent => Status = $"Downloading update… {percent}%");
            string source = await UpdateChecker.DownloadAsync(available, progress, lifetime.Token);
            blocked = installBlock();
            if (blocked != null) { Status = blocked; return; }
            Status = "Restarting ysfUtil…";
            await PortableUpdater.PrepareAsync(source, available.Sha256!, quiet(), lifetime.Token, available.IsSetup);
            exit();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Status = "Update failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    public void Dispose() { timer.Stop(); lifetime.Cancel(); }
}
