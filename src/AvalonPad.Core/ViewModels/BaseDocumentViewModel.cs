using AvalonPad.Core.AvalonDock.ViewModels;
using AvalonPad.Core.Services;
using System.Windows;

namespace AvalonPad.Core.ViewModels;

public class BaseDocumentViewModel : AvalonDocumentViewModel
{
    private readonly IDialogService mDialogService;

    public BaseDocumentViewModel(IDialogService dialogService) 
    {
        mDialogService = dialogService;
    }

    public override bool ConfirmClose()
    {
        if (!IsModified)
            return true;

        var result = mDialogService.ShowQuestion($"Сохранить изменения в «{Title}»?", "Несохранённые изменения");

        return result switch
        {
            // Сохранить и закрыть. Если пользователь отменил диалог сохранения
            // (SaveFileDialog вернул null), SaveToFile() вернёт false —
            // и закрытие документа тоже отменится.
            MessageBoxResult.Yes => SaveToFile(),

            // Закрыть без сохранения — изменения теряются сознательно.
            MessageBoxResult.No => true,

            // Отмена (или закрытие диалога крестиком) — документ остаётся открытым.
            _ => false,
        };
    }
}
