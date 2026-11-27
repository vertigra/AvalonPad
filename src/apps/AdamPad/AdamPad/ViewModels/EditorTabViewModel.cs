using AvalonPad.Core.AvalonDock.Model;
using AvalonPad.Core.Services;
using AvalonPad.Core.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace AdamPad.ViewModels;

/// <summary>
/// ViewModel вкладки документа. В классическом DockingManager (с DocumentsSource)
/// это просто VM с INotifyPropertyChanged + свойства Title/Content/IsModified.
/// </summary>
public sealed partial class EditorTabViewModel : BaseDocumentViewModel, IPersistableDocument
{
    public ISettingsService SettingsService { get; }
    private readonly IFileService mFileService;
    private readonly IDialogService mDialogService;
    private readonly ILogService<EditorTabViewModel> mLogService;

    public EditorTabViewModel(ISettingsService settingsService, IFileService fileService, IDialogService dialogService, ILogService<EditorTabViewModel> logService) : base(dialogService)
    {
        SettingsService = settingsService;

        mFileService = fileService;
        mDialogService = dialogService;
        mLogService = logService;
    }

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    // Хук генератора срабатывает ДО изменения свойства Content.
    // Чтобы избежать ложного срабатывания IsModified при первой загрузке файла, 
    // мы временно сбрасываем этот флаг в методе LoadFile ниже.
    partial void OnContentChanging(string value) => IsModified = true;

    [ObservableProperty]
    public partial string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Загрузить содержимое файла в документ.
    /// </summary>
    public void LoadFile(string path)
    {
        FilePath = path;
        Title = Path.GetFileName(path);
        ToolTip = path;
        ContentId = path;

        try
        {
            Content = File.ReadAllText(path);

            // OnContentChanging взведет флаг IsModified в true, 
            // поэтому сбрасываем его обратно в false ПОСЛЕ присвоения Content.
            IsModified = false;
        }
        catch (Exception ex)
        {
            mLogService.LogError("Ошибка чтения файла", ex);
            mDialogService.ShowInformation($"Ошибка чтения файла: {ex.Message}", "Ошибка");
        }
    }

    /// <summary>
    /// Сохранить свои динамические поля в состояние workspace.
    /// </summary>
    public void SaveState(WorkspaceDocument state)
    {
        state.FilePath = string.IsNullOrEmpty(FilePath) ? null : FilePath;
        state.Content = Content;
    }

    /// <summary>
    /// Восстановить свои динамические поля из состояния workspace.
    /// Вызывается каталогом при восстановлении workspace.
    /// </summary>
    public void LoadState(WorkspaceDocument state)
    {
        ContentId = state.ContentId;
        Title = state.Title;
        FilePath = state.FilePath ?? string.Empty;
        Content = state.Content;
    }

    public override bool SaveToFile()
    {
        var path = FilePath;
        if (string.IsNullOrEmpty(path))
        {
            path = mFileService.ShowSaveDialog(Title);
            if (path is null) 
                return false;
        }

        try
        {
            mFileService.WriteAllText(path, Content);
            FilePath = path;
            Title = Path.GetFileName(path);
            IsModified = false;
            return true;
        }
        catch (Exception ex)
        {
            mLogService.LogError("Не удалось сохранить", ex);
            mDialogService.ShowInformation($"Не удалось сохранить: {ex.Message}", "Ошибка");
            return false;
        }
    }
}
