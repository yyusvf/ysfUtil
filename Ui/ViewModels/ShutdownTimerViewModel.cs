using System.Globalization;

namespace YsfUtil.Ui.ViewModels;

internal sealed class ShutdownTimerViewModel : ViewModelBase
{
    private readonly ShutdownTimer timer;
    private readonly AppSettings settings;
    private string minutes;
    private string? error;

    public ShutdownTimerViewModel(ShutdownTimer timer, AppSettings settings)
    {
        this.timer = timer;
        this.settings = settings;
        minutes = (settings.ShutdownMinutes is >= 1 and <= 1440 ? settings.ShutdownMinutes : 30)
            .ToString(CultureInfo.InvariantCulture);
        StartCommand = new RelayCommand(Start);
        CancelCommand = new RelayCommand(Cancel);
        FifteenMinutesCommand = new RelayCommand(() => Minutes = "15");
        ThirtyMinutesCommand = new RelayCommand(() => Minutes = "30");
        SixtyMinutesCommand = new RelayCommand(() => Minutes = "60");
        timer.Changed += Refresh;
        timer.Failed += message => { Error = message; };
    }

    public string Minutes
    {
        get => minutes;
        set
        {
            if (Set(ref minutes, value)) Error = null;
        }
    }

    public bool IsRunning => timer.IsRunning;
    public bool IsEnabled
    {
        get => settings.ShutdownTimerEnabled;
        set
        {
            if (settings.ShutdownTimerEnabled == value) return;
            settings.ShutdownTimerEnabled = value;
            if (!value) Cancel();
            settings.Save();
            Raise();
            Raise(nameof(CanStart));
        }
    }
    public bool CanStart => IsEnabled && !IsRunning;
    public string Status => IsRunning
        ? $"{(int)timer.Remaining.TotalHours:00}:{timer.Remaining:mm\\:ss} remaining · {timer.Deadline!.Value.ToLocalTime():HH:mm}"
        : "No shutdown scheduled";
    public string? Error
    {
        get => error;
        private set { if (Set(ref error, value)) Raise(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public RelayCommand StartCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand FifteenMinutesCommand { get; }
    public RelayCommand ThirtyMinutesCommand { get; }
    public RelayCommand SixtyMinutesCommand { get; }

    private void Start()
    {
        if (!CanStart) return;
        if (!int.TryParse(Minutes, NumberStyles.None, CultureInfo.InvariantCulture, out int duration)
            || duration is < 1 or > 1440)
        {
            Error = "Enter a whole number from 1 to 1440 minutes.";
            return;
        }

        Error = null;
        timer.Start(duration);
        settings.ShutdownMinutes = duration;
        settings.Save();
    }

    public void Cancel()
    {
        timer.Cancel();
        Error = null;
    }

    public void Refresh()
    {
        Raise(nameof(IsRunning));
        Raise(nameof(CanStart));
        Raise(nameof(Status));
    }
}
