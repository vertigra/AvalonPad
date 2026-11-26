using AvalonPad.Core.AvalonDock.Services;
using AvalonPad.Core.AvalonDock.ViewModels;
using AvalonPad.Core.Services.AvalonDock;
using AvalonPad.Core.Models;
using AvalonPad.Core.Services;
using AvalonPad.Messages;
using AvalonPad.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using AvalonPad.Core.AvalonDock.Model;

namespace AvalonPad.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IWindowLifecycleAware
{
    #region Services

    private readonly IToolboxService mToolboxService;
    private readonly IThemeService mThemeService;
    private readonly IDockWindowService mDockWindowService;
    private readonly IPersistenceService mPersistenceService;
    private readonly ILogService<MainViewModel> mLogService;
    private readonly IAppModule mModule;
    private readonly IMessenger mMessenger;

    #endregion

    public MainViewModel(
        IToolboxService toolboxService,
        IThemeService themeService,
        IDockWindowService dockWindowService,
        IPersistenceService persistenceService,
        ILogService<MainViewModel> logService,
        IAppModule module,
        IMessenger messenger)
    {
        mToolboxService = toolboxService;
        mThemeService = themeService;
        mDockWindowService = dockWindowService;
        mPersistenceService = persistenceService;
        mLogService = logService;
        mModule = module;
        mMessenger = messenger;

        // Регистрируем панели модуля (до восстановления workspace)
        foreach (ToolRegistration tool in module.Tools)
            mToolboxService.Register(tool.ViewModelType);

        // Собираем меню: пункты модуля + стандартные пункты Shell
        FileMenuItems = BuildMenuItems("Файл");
        ToolsMenuItems = BuildMenuItems("Панели инструментов");

        FileMenuItems.Add(new MenuItemViewModel("Сохранить", SaveFileCommand, "Ctrl+S"));
        FileMenuItems.Add(new MenuItemViewModel("Закрыть документ", CloseDocumentCommand, "Ctrl+W"));
        FileMenuItems.Add(MenuItemViewModel.Separator());
        FileMenuItems.Add(new MenuItemViewModel("Выход", ExitCommand));

        mLogService.LogInformation("Приложение запущено.");

        // Подписки
        mDockWindowService.PropertyChanged += OnDocumentPropertyChanged;
        mThemeService.PropertyChanged += ThemePropertyChanged;
    }

    // --- Пробросы свойств для биндинга ---

    public string AppTitle => mModule.AppTitle;

    /// <summary>Пункты меню «Файл»: модульные + стандартные Shell (см. конструктор).</summary>
    public ObservableCollection<MenuItemViewModel> FileMenuItems { get; } = [];

    /// <summary>Пункты меню «Панели инструментов» — из модуля.</summary>
    public ObservableCollection<MenuItemViewModel> ToolsMenuItems { get; } = [];

    private ObservableCollection<MenuItemViewModel> BuildMenuItems(string parentHeader)
    {
        var items = new ObservableCollection<MenuItemViewModel>();
        foreach (MenuRegistration registration in mModule.MenuItems)
        {
            if (registration.ParentHeader != parentHeader)
                continue;

            items.Add(registration.Header is null
                ? MenuItemViewModel.Separator()
                : new MenuItemViewModel(registration.Header, registration.Command!, registration.InputGesture));
        }
        return items;
    }

    public ObservableCollection<AvalonDocumentViewModel> Documents => mDockWindowService.Documents;
    public AvalonDocumentViewModel? ActiveDocument => mDockWindowService.ActiveDocument;
    public ObservableCollection<AvalonToolViewModel> Anchorables => mToolboxService.Anchorables;
    public IReadOnlyList<ThemeInfo> AvailableThemes => mThemeService.AvailableThemes;
    public string? CurrentThemeName => mThemeService.CurrentTheme?.Name;
    

    // --- Жизненный цикл ---

    public void OnWindowLoaded(Window window) => mPersistenceService.Restore(window);

    public void OnWindowClosed(Window window) => mPersistenceService.Save(window);

    // --- Обработчики ---

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDockWindowService.ActiveDocument))
        {
            OnPropertyChanged(nameof(ActiveDocument));

            SaveFileCommand.NotifyCanExecuteChanged();
            CloseDocumentCommand.NotifyCanExecuteChanged();
        }
    }

    private void ThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(mThemeService.CurrentTheme))
        {
            OnPropertyChanged(nameof(CurrentThemeName));
            mLogService.LogInformation($"Тема изменена: {CurrentThemeName}");
        }

    }

    // --- Команды ---

    [RelayCommand(CanExecute = nameof(CanSaveOrClose))]
    private void SaveFile()
    {
        ActiveDocument?.SaveToFile();
    }

    [RelayCommand(CanExecute = nameof(CanSaveOrClose))]
    private void CloseDocument()
    {
        if(ActiveDocument != null)
            mDockWindowService.CloseDocument(ActiveDocument);
    }

    private bool CanSaveOrClose() => ActiveDocument is not null;


    [RelayCommand]
    private void OpenSettings()
    {
        mMessenger.Send(new OpenSettingsMessage());
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand]
    private void SetTheme(string? themeName)
    {
        if (string.IsNullOrEmpty(themeName)) 
            return;

        mThemeService.ApplyTheme(themeName);
    }
}