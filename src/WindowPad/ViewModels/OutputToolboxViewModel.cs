using AvalonDock.Layout;
using AvalonPad.Core.Services;
using AvalonPad.Core.ViewModels;
using System.Collections.ObjectModel;

namespace WindowPad.ViewModels;

/// <summary>
/// Панель «Вывод» — закрепляется снизу, логирует сообщения приложения.
/// </summary>
public sealed partial class OutputToolboxViewModel : BaseToolboxViewModel
{

    #region Services

    private readonly ILogService<OutputToolboxViewModel> mLogService;

    #endregion

    public OutputToolboxViewModel(ILogService<OutputToolboxViewModel> logService) : base("Вывод", nameof(OutputToolboxViewModel))
    {
        mLogService = logService;
        PreferredSide = AnchorSide.Bottom;
    }

    public ObservableCollection<string> LogLines => mLogService.LogLines;
}
