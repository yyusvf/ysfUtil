using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace YsfUtil.Ui.Views;

/// <summary>Clips a section's contents while animating the space they occupy.</summary>
public sealed class RevealPanel : Decorator
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(RevealPanel),
        new PropertyMetadata(true, Changed));
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register("Progress", typeof(double), typeof(RevealPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public RevealPanel() { ClipToBounds = true; }

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (RevealPanel)d;
        double from = (double)panel.GetValue(ProgressProperty), to = (bool)e.NewValue ? 1 : 0;
        panel.BeginAnimation(ProgressProperty, null);
        panel.SetValue(ProgressProperty, to);
        panel.IsEnabled = (bool)e.NewValue;
        panel.IsHitTestVisible = (bool)e.NewValue;
        if (!panel.IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        panel.BeginAnimation(ProgressProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop,
        });
    }
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child == null) return new();
        double progress = (double)GetValue(ProgressProperty);
        if (progress <= 0) return new();
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * progress);
    }
    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(0, 0, size.Width, Child.DesiredSize.Height));
        return size;
    }
}
