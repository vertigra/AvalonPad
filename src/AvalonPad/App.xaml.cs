using AvalonPad.Core.Services.AvalonDock;
using AvalonPad.Views;
using AvalonPad.Extensions;
using AvalonPad.Factories;
using AvalonPad.Services;
using AvalonPad.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using AvalonPad.Core.Services;
using AvalonPad.Core.AvalonDock.Services;
using System.Windows.Navigation;
using AvalonPad.Core.Models;

namespace AvalonPad;

public partial class App : Application
{
    private readonly IHost mHost;
    private readonly ObservableCollection<string> mUiLogLines = [];
    private readonly Exception? mLoadException = null;
    private readonly Dictionary<string, string> mErrorConfigProperties = [];

    public App()
    {
        AppPathService appPathService = new();
        
        SettingsService settingsService = new(appPathService);

        try
        {
            mErrorConfigProperties = settingsService.Load();
        }
        catch(Exception ex)
        {
            mLoadException = ex;
        }

        var settings = settingsService.Current;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(a => a.File(
                settings.LogFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.RetainedFileCountLimit,
                levelSwitch: settingsService.GlobalLevelSwitch,
                outputTemplate: settings.FileLogTemplate))
            .WriteTo.ObservableSink(mUiLogLines, levelSwitch: settingsService.OutputPanelSwitch)
            .CreateLogger();

        if (settingsService.IsFirstRun)
        {
            Log.Information("Первый запуск: файл настроек не найден, создан со значениями по умолчанию");
        }

        if (mLoadException != null)
        {
            Log.Error(mLoadException, "Ошибка при загрузке настроек, используются настройки по умолчанию");
        }

        if (mErrorConfigProperties.Count > 0)
        {
            foreach (KeyValuePair<string, string> property in mErrorConfigProperties)
            {
                Log.Warning("Настройка {propertyName} не прочитана: {propertyError}", property.Key, property.Value);
            }
        }

        mHost = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IAppPathService>(appPathService);
                services.AddSingleton<ISettingsService>(settingsService);

                // Serilog.ILogger — глобальный логер
                services.AddSingleton(Log.Logger);
                services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

                // Базовый логер (без контекста) — для тех, кому не нужен SourceContext
                services.AddSingleton<ILogService>(sp => new LogService(sp.GetRequiredService<ILogger>(), mUiLogLines));
   
                // Типизированный логер — добавляет SourceContext автоматически
                services.AddSingleton(typeof(ILogService<>), typeof(LogService<>));

                services.AddSingleton<IPersistenceService, PersistenceService>();
                
                // --- Сервисы приложения ---

                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IFileService, FileService>();
                services.AddSingleton<IWorkspaceService, WorkspaceService>();

                // Слабосвязанные сообщения
                services.AddSingleton<IMessenger, WeakReferenceMessenger>();
                
                // Holder для DockingManager
                services.AddSingleton<IDockingManagerProvider, DockingManagerProvider>();

                // Подписка на события DockingManager 
                services.AddSingleton<IDockEventBinder, DockEventBinder>();

                // Жизненный цикл окна (Loaded/Closed) 
                services.AddSingleton<IWindowLifecycleService, WindowLifecycleService>();

                // Сериализация layout (положение панелей, ширины, floating-окна)
                services.AddSingleton<ILayoutSerializerService, LayoutSerializerService>();

                // фабрика тем, с типами AvalonDock
                services.AddSingleton<IThemeFactoryService, ThemeFactoryService>();
                // Управление темами оформления
                services.AddSingleton<IThemeService, ThemeService>();

                // Сервис управления панелями (Toolbox)
                services.AddSingleton<IToolboxService, ToolboxService>();

                // Фабрика для создания документов через DI (вместо new EditorTabViewModel())
                services.AddSingleton<IEditorTabViewModelFactory, EditorTabViewModelFactory>();

                // Сервис по управлению документами
                services.AddSingleton<IDockWindowService, DockWindowService>();

                // --- Документы: каждый раз новый экземпляр (transient) ---
                services.AddTransient<EditorTabViewModel>();

                services.AddSingleton<SettingsViewModel>();
                
                // --- Панели (один экземпляр на всё приложение) ---
                services.AddSingleton<PropertiesToolboxViewModel>();
                services.AddSingleton<OutputToolboxViewModel>();

                // --- Главная ViewModel ---
                services.AddSingleton<MainViewModel>();

                // --- Главное окно ---
                services.AddSingleton<MainWindow>();
            })
            .UseSerilog()
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await mHost.StartAsync();

        _ = mHost.Services.GetRequiredService<SettingsViewModel>();

        MainWindow mainWindow = mHost.Services.GetRequiredService<MainWindow>();
        AppSettings settings = mHost.Services.GetRequiredService<ISettingsService>().Current;

        mainWindow = RestoreMainWindowState(mainWindow, settings);
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await mHost.StopAsync();

        var settings = mHost.Services.GetRequiredService<ISettingsService>().Current;
        var mainWindow = mHost.Services.GetRequiredService<MainWindow>();

        SaveMainWindowState(mainWindow, settings);
        
        mHost.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>
    /// Применяем сохранённую геометрию окна ДО Show — иначе будет визуальный
    /// скачок: окно мелькнёт на дефолтных размерах из XAML (1100x700), потом
    /// перестроится на сохранённые. Применение до Show убирает этот эффект.
    /// </summary>
    private void SaveMainWindowState(MainWindow mainWindow, AppSettings currentSettings)
    {
        // Сохраняем геометрию главного окна в настройки.
        // Ответственность App как composition root: на старте применили,
        // на выходе — сохраняем. WindowLifecycleService к этому не относится,
        // он занимается только подпиской на Loaded/Closed и регистрацией
        // DockingManager.
        //
        // Пишем Width/Height только при WindowState == Normal, иначе в настройки
        // уйдут максимизированные размеры экрана. При максимизированном закрытии
        // оставляем предыдущие Normal-размеры.
        try
        {
            if (mainWindow.WindowState == WindowState.Normal)
            {
                currentSettings.WindowWidth = mainWindow.Width;
                currentSettings.WindowHeight = mainWindow.Height;
            }
            currentSettings.IsMaximized = mainWindow.WindowState == WindowState.Maximized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Не удалось сохранить геометрию окна");
        }

    }

    private static MainWindow RestoreMainWindowState(MainWindow mainWindow, AppSettings currentSettings)
    {
        mainWindow.Width = currentSettings.WindowWidth;
        mainWindow.Height = currentSettings.WindowHeight;
        mainWindow.WindowState = currentSettings.IsMaximized ? WindowState.Maximized : WindowState.Normal;

        return mainWindow;
    }
}
