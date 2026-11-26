using AvalonPad.Core.AvalonDock.ViewModels;
using AvalonPad.Core.AvalonDock.Model;
using AvalonPad.Core.AvalonDock.Services;
using AvalonPad.Core.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AvalonPad.Services;

public sealed class PersistenceService : IPersistenceService
{
    public event EventHandler? RestoreHappened;

    private readonly IDockingManagerProvider mDockingManager;
    private readonly ILayoutSerializerService mLayoutSerializer;
    private readonly IWorkspaceService mWorkspaceService;
    private readonly IDocumentCatalog mCatalog;
    private readonly IDockWindowService mDockWindowService;
    private readonly ISettingsService mSettingsService;
    private readonly ILogService<PersistenceService> mLoggerService;
    private readonly string WorkspaceFilePath = string.Empty;
    private readonly string LayoutFilePath = string.Empty;
    private int mLayoutChangeCount;
    private const int cSaveLayoutDelayMs = 500;

    public PersistenceService(
        IDockingManagerProvider managerProvider,
        ILayoutSerializerService layoutSerializer,
        IWorkspaceService workspaceService,
        IDocumentCatalog documentCatalog,
        IDockEventBinder dockEventBinder,
        ISettingsService settingsService,
        IDockWindowService dockWindowService,
        IAppPathService appPathService,
        ILogService<PersistenceService> loggerService)
    {
        mDockingManager = managerProvider;
        mLayoutSerializer = layoutSerializer;
        mWorkspaceService = workspaceService;
        mCatalog = documentCatalog;
        mSettingsService = settingsService;
        mDockWindowService = dockWindowService;
        mLoggerService = loggerService;

        WorkspaceFilePath = appPathService.WorkspaceFilePath; 
        LayoutFilePath = appPathService.LayoutFilePath;

        mSettingsService.Current.PropertyChanged += SettingsPropertyChanged;
        dockEventBinder.LayoutChanged += OnLayoutChanged;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        ScheduleLayoutSave();
    }

    public void Save(Window window)
    {
        SaveWindowState(window);

        try 
        { 
            SaveLayout(); 
        }
        catch (Exception ex) 
        { 
            mLoggerService.LogError("SaveLayout failed", ex); 
        }

        try 
        { 
            SaveWorkspace(); 
        }
        catch (Exception ex) 
        { 
            mLoggerService.LogError("SaveWorkspace failed", ex); 
        }
    }


    public void Restore(Window window)
    {
        RestoreWindowState(window);
        RestoreWorkspace();
        RestoreLayout();

        NotifyRestoreCompleted();
    }

    private void SaveWindowState(Window window)
    {
        var current = mSettingsService.Current;
        if (window.WindowState == WindowState.Normal)
        {
            current.WindowWidth = window.Width;
            current.WindowHeight = window.Height;
        }
        current.IsMaximized = window.WindowState == WindowState.Maximized;
    }

    private void RestoreWindowState(Window window)
    {
        var current = mSettingsService.Current;
        window.Width = current.WindowWidth;
        window.Height = current.WindowHeight;
        window.WindowState = current.IsMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void RestoreWorkspace()
    {
        WorkspaceState state = mWorkspaceService.Load(WorkspaceFilePath);

        if (state.Documents.Count == 0)
        {
            mLoggerService.LogInformation($"Нет документов для восстановления");
            return;
        }
            

        foreach (WorkspaceDocument d in state.Documents)
        {
            AvalonDocumentViewModel? doc = mCatalog.Create(d.DocumentTypeName);

            if (doc is null)
            {
                mLoggerService.LogWarning($"Документ «{d.Title}» ({d.DocumentTypeName}) не найден в каталоге — пропущен.");
                continue;
            }

            // Своё динамическое состояние документ восстанавливает сам (IPersistableDocument),
            // общие поля — здесь.
            if (doc is IPersistableDocument persistable)
                persistable.LoadState(d);

            doc.IsActive = d.IsActive;
            doc.IsModified = d.IsModified;

            mDockWindowService.OpenDocument(doc);
        }

        mLoggerService.LogInformation($"Восстановлено документов: {state.Documents.Count}");
    }

    private void RestoreLayout()
    {
        var manager = mDockingManager.Manager;

        if (manager is null)
        {
            mLoggerService.LogWarning("DockingManager не загружен — восстановление layout невозможно.");
            return;
        }

        if (!File.Exists(LayoutFilePath))
        {
            mLoggerService.LogWarning($"Файл layout не найден: {LayoutFilePath}");
            return;
        }

        // Восстановление откладываем — AvalonDock ещё не создал LayoutItem-ы
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            try
            {
                if (mLayoutSerializer.Deserialize(manager, LayoutFilePath))
                {
                    mLoggerService.LogInformation($"Layout восстановлен: {LayoutFilePath}");
                    
                    if (manager.ActiveContent is AvalonDocumentViewModel activeDoc)
                    {
                        mDockWindowService.ActivateDocument(activeDoc);

                        mLoggerService.LogInformation($"Активный документ восстановлен: {activeDoc.Title}");

                        return;
                    }

                    mLoggerService.LogInformation($"Активных документов для восстановления не обнаружено");

                    return;
                }
                
                mLoggerService.LogWarning("Не удалось восстановить layout.");
            }
            catch (Exception ex)
            {
                mLoggerService.LogError("Ошибка восстановления layout", ex);
            }
        });

        
    }

    /// <summary>
    /// Debounced сохранение layout — вызывать при каждом LayoutChanged.
    /// </summary>
    public void ScheduleLayoutSave()
    {
        Interlocked.Increment(ref mLayoutChangeCount);
        Task.Run(async () =>
        {
            await Task.Delay(cSaveLayoutDelayMs);
            if (Interlocked.Decrement(ref mLayoutChangeCount) == 0)
            {
                try
                {
                    mLoggerService.LogDebug("Автосохранение layout");

                    // ВАЖНО: сериализация обращается к DockingManager.Layout (DependencyProperty),
                    // а значит должна выполняться в UI-потоке. Marshal через Dispatcher.
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        SaveLayout();
                    });
                }
                catch (Exception ex)
                {
                    mLoggerService.LogError("Автосохранение layout failed", ex);
                }
            }
        });
    }

    private void SaveLayout()
    {
        var manager = mDockingManager.Manager;
        
        if (manager is null) 
            return;

        mLayoutSerializer.Serialize(manager, LayoutFilePath);
    }

    private void SaveWorkspace()
    {
        var state = new WorkspaceState();

        foreach (var doc in mDockWindowService.Documents)
        {
            var workspaceDocument = new WorkspaceDocument
            {
                ContentId = doc.ContentId,
                Title = doc.Title,
                IsActive = doc.IsActive,
                IsModified = doc.IsModified,
                DocumentTypeName = doc.GetType().FullName ?? "Undefined" // Сохраняем тип, чтобы знать, что создавать при загрузке

            };

            // Свои динамические поля документ сохраняет сам (IPersistableDocument)
            if (doc is IPersistableDocument persistable)
                persistable.SaveState(workspaceDocument);

            state.Documents.Add(workspaceDocument);
        }

        mWorkspaceService.Save(WorkspaceFilePath, state);
    }

    /// <summary>
    /// Сообщет сервисам что необходимо восстановить настройки
    /// </summary>
    private void NotifyRestoreCompleted() => RestoreHappened?.Invoke(this, new EventArgs());

    /// <summary>
    /// Получает сигнал от сервиса настроек что настройки изменены.Исключает пересохранение при загрузке
    /// </summary>
    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) => mSettingsService.Save();
}