using System.Windows.Input;

namespace YsfUtil.Ui.ViewModels;

/// <summary>Ein Befehl, der einfach eine Methode aufruft.</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
