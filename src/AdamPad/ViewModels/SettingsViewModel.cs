using AvalonPad.Core.AvalonDock.Services;
using AvalonPad.Core.Services;
using AvalonPad.Core.ViewModels;
using AdamPad.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

namespace AdamPad.ViewModels;

public sealed class SettingsViewModel : BaseDocumentViewModel, IRecipient<OpenSettingsMessage>
{
    public ISettingsService SettingsService { get; }

    private readonly IDockWindowService mDockWindowService;

    private readonly ILogService<SettingsViewModel> mLogService;

    public SettingsViewModel(ISettingsService settingsService, IDockWindowService dockWindowService, IMessenger messenger, ILogService<SettingsViewModel> logService, IDialogService dialogService) : base(dialogService)
    {
        SettingsService = settingsService;
        mDockWindowService = dockWindowService;
        mLogService = logService;

        Title = "Настройки";
        ContentId = nameof(SettingsViewModel);
        ToolTip = "Настройки приложения";

        messenger.RegisterAll(this);

        SettingsService.Current.PropertyChanged += SettingsChanged;
    }

    /// <summary>Список доступных шрифтов системы (для ComboBox'а настроек).</summary>
    public IReadOnlyList<string> AvailableFonts { get; } = [.. Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s)];

    /// <summary>Доступные размеры шрифта (pt) для ComboBox'а настроек.</summary>
    public IReadOnlyList<int> AvailableFontSizes { get; } = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72];

    public void Receive(OpenSettingsMessage message)
    {
        //активируем если вкладка открыта
        if (mDockWindowService.Documents.Contains(this))
        {
            mDockWindowService.ActivateDocument(this);
            return;
        }
        
        mDockWindowService.OpenDocument(this);
        mDockWindowService.ActivateDocument(this);
        
    }

    public override bool SaveToFile()
    {
        try
        {
            SettingsService.Save(); 
            IsModified = false;
        }
        catch(Exception ex)
        {
            mLogService.LogError("Ошибка при сохранении настроек", ex);
        }
        
        return true;
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsModified = true;
    }
}
