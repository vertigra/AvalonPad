using System.Windows;

namespace AvalonPad.Core.Services;

/// <summary>
/// Абстракция сервиса диалогов — чтобы ViewModel не зависел от View напрямую.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Диалог «Да / Нет» — быстрый выбор из двух вариантов.
    /// </summary>
    MessageBoxResult ShowYesNo(string message, string title);

    /// <summary>
    /// Диалог «Да / Нет / Отмена» — «Отмена» означает «продолжить как было».
    /// </summary>
    MessageBoxResult ShowYesNoCancel(string message, string title);
    void ShowInformation(string message, string title);
}

public sealed class DialogService : IDialogService
{
    public MessageBoxResult ShowYesNo(string message, string title) => 
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

    public MessageBoxResult ShowYesNoCancel(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    public void ShowInformation(string message, string title) => 
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
