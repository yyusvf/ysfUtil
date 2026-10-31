using System.Windows.Controls;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules.Tools;

public partial class ToolsSettingsView : UserControl
{
    internal ToolsSettingsView(IReadOnlyList<FeatureViewModel> features, ShutdownTimerViewModel shutdown,
        AudioOutputViewModel audio, AudioVolumeViewModel volume)
    {
        InitializeComponent();
        SettingsFeatureList.ItemsSource = features;
        ShutdownSetting.DataContext = shutdown;
        VolumeSetting.DataContext = volume;
        AudioSettingsCard.DataContext = audio;
    }

    public void ShowAudioSettings()
    {
        AudioSettingsCard.ExpandOptions();
        UpdateLayout();
        AudioSettingsCard.BringIntoView();
    }
}
