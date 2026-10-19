using System.Windows;
using System.Windows.Controls;

namespace YsfUtil.Ui.Views;

public partial class AudioOutputCard : UserControl
{
    public AudioOutputCard() => InitializeComponent();
    public event Action? SettingsRequested;
    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
