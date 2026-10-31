using System.Windows.Controls;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules.Tools;

public partial class ToolsFlyoutView : UserControl
{
    internal ToolsFlyoutView(IReadOnlyList<FeatureViewModel> features, ShutdownTimerViewModel shutdown,
        AudioOutputViewModel audio, AudioVolumeViewModel volume)
    {
        InitializeComponent();
        FeatureList.ItemsSource = features;
        ShutdownCard.DataContext = shutdown;
        AudioCard.DataContext = audio;
        VolumeCard.DataContext = volume;
        AudioCard.SettingsRequested += () => AudioSettingsRequested?.Invoke();
    }

    /// <summary>The audio card's settings button: open the audio section in Settings.</summary>
    internal event Action? AudioSettingsRequested;

    public void ShowShutdownTimer()
    {
        ShutdownCard.ExpandOptions();
        UpdateLayout();
        ShutdownCard.BringIntoView();
    }
}
