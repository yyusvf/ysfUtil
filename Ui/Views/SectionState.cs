using System.Windows;
using System.Windows.Controls;

namespace YsfUtil.Ui.Views;

/// <summary>Remembers presentation only; collapsing never changes the feature's enabled state.</summary>
public static class SectionState
{
    private static AppSettings? settings;
    private static bool persist;
    internal static void Configure(AppSettings value, bool save = true) { settings = value; persist = save; }
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string), typeof(SectionState), new PropertyMetadata(null, KeyChanged));
    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string value) => d.SetValue(KeyProperty, value);
    private static void KeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Expander expander) return;
        expander.Expanded -= Changed;
        expander.Collapsed -= Changed;
        if (e.NewValue is not string key) return;
        expander.SetCurrentValue(Expander.IsExpandedProperty, settings?.CollapsedSections.Contains(key) != true);
        expander.Expanded += Changed;
        expander.Collapsed += Changed;
    }
    private static void Changed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender || settings == null || sender is not Expander expander || GetKey(expander) is not string key) return;
        if (expander.IsExpanded) settings.CollapsedSections.Remove(key);
        else settings.CollapsedSections.Add(key);
        if (persist) settings.Save();
    }
}
