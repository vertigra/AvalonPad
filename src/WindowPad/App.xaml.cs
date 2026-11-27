using AvalonPad.Core.AvalonDock.Services;
using AvalonPad.Core.Models;
using AvalonPad.Core.Services;
using AvalonPad.Core.Services.AvalonDock;
using WindowPad.Factories;
using WindowPad.Services;
using AvalonPad.Shell.Extensions;
using AvalonPad.Shell.Services;
using AvalonPad.Shell.ViewModels;
using AvalonPad.Shell.Views;
using WindowPad.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace WindowPad;

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

                // Каталог документов: какие типы восстанавливать из workspace.json
                // (PersistenceService конкретных типов не знает)
                services.AddSingleton<IDocumentCatalog, DocumentCatalog>();

                // Контракт модуля: Shell знает только интерфейс IAppModule
                services.AddSingleton<IAppModule, WindowPadModule>();

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

        // Eager-resolve синглтон SettingsViewModel — конструктор регистрирует
        // обработчик OpenSettingsMessage в WeakReferenceMessenger. Без этого
        // команда "Настройки → Параметры" будет молча терять сообщение,
        // если в workspace.json не было сохранённой вкладки настроек.
        _ = mHost.Services.GetRequiredService<SettingsViewModel>();

        MainWindow window = mHost.Services.GetRequiredService<MainWindow>();
        window.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await mHost.StopAsync();
        mHost.Dispose();
        Log.CloseAndFlush();

        base.OnExit(e);
    }
}
