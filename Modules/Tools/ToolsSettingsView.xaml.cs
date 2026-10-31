using System.Windows.Controls;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules.Tools;

/// <summary>Switches for the single tools.</summary>
public partial class ToolsSettingsView : UserControl
{
    internal ToolsSettingsView(IReadOnlyList<FeatureViewModel> features, ShutdownTimerViewModel shutdown,
        AudioVolumeViewModel volume)
    {
        InitializeComponent();
        SettingsFeatureList.ItemsSource = features;
        ShutdownSetting.DataContext = shutdown;
        VolumeSetting.DataContext = volume;
    }
}
