using System.Collections.ObjectModel;
using System.Windows.Input;

namespace AvalonPad.Shell.ViewModels;

/// <summary>
/// Элемент меню для привязки (Shell). Header == null — разделитель.
/// Дети — вложенные пункты (задел на будущее; сейчас меню плоское).
/// </summary>
public sealed class MenuItemViewModel
{
    public static MenuItemViewModel Separator() => new();

    private MenuItemViewModel()
    {
        Children = [];
    }

    public MenuItemViewModel(string header, ICommand command, string? inputGesture = null)
        : this()
    {
        Header = header;
        Command = command;
        InputGesture = inputGesture;
    }

    public string? Header { get; }
    public ICommand? Command { get; }
    public string? InputGesture { get; }
    public bool IsSeparator => Header is null;
    public ObservableCollection<MenuItemViewModel> Children { get; }
}
