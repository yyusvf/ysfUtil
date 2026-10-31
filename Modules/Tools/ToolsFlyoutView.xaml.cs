using System.Windows.Controls;
using YsfUtil.Ui.ViewModels;

namespace YsfUtil.Modules.Tools;

/// <summary>The hotkey actions without a card of their own: taskbar and desktop icons.</summary>
public partial class ToolsFlyoutView : UserControl
{
    internal ToolsFlyoutView(IEnumerable<FeatureViewModel> features)
    {
        InitializeComponent();
        FeatureList.ItemsSource = features;
    }
}
