using System.Windows.Controls;
namespace YsfUtil.Ui.Views;
public partial class AudioOutputSettingsCard : UserControl
{
    public AudioOutputSettingsCard() => InitializeComponent();
    public void ExpandOptions() => Options.SetCurrentValue(Expander.IsExpandedProperty, true);
}
