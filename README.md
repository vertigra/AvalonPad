# AvalonPad

# Архитектура проекта AvalonPad

WPF приложение на **.NET 10 + AvalonDock 5 + MVVM + DI**. Решение разбито на три проекта.

## Стек технологий

- **.NET 10** (`net10.0-windows`), `LangVersion=latest`
- **AvalonDock 5.0.1** (`Dirkster.AvalonDock` + `Dirkster.AvalonDock.Serializer.Xml` + `Dirkster.AvalonDock.Themes.VS2013`) — классический `DockingManager` с `DocumentsSource` / `AnchorablesSource`
- **CommunityToolkit.Mvvm 8.4.2** (`[ObservableProperty]`, `[RelayCommand]`, `IMessenger`/`WeakReferenceMessenger`)
- **Microsoft.Extensions.DependencyInjection 10** + `Microsoft.Extensions.Hosting 10`
- **Serilog 4** + кастомный `ObservableSink` для UI-логов (`Serilog.Sinks.Async`, `Serilog.Sinks.File`)
- **Стиль кода**: file-scoped namespaces во всех 42 `.cs`-файлах

---

## Структура решения (`AvalonPad.slnx`)

```text
AvalonPad.slnx
├── src/AvalonPad.Core.AvalonDock/   — библиотека: обёртки над AvalonDock
├── src/AvalonPad.Core/              — библиотека: доменные сервисы, VM, модели
└── src/AvalonPad/                   — WPF-приложение (App, Views, MainViewModel)
```

Зависимости проектов:

```text
AvalonPad (WinExe, WPF)
    └── AvalonPad.Core (Library)
            └── AvalonPad.Core.AvalonDock (Library)
                    └── Dirkster.AvalonDock.* v5.0.1
```

> `AvalonPad` НЕ ссылается на `AvalonPad.Core.AvalonDock` напрямую — только через `AvalonPad.Core`. WPF-проект работает исключительно с доменными абстракциями из `Core`, а всё AvalonDock-специфичное инкапсулировано в `Core.AvalonDock`.

---

## Проект `AvalonPad.Core.AvalonDock`

Содержит всё, что напрямую зависит от AvalonDock: типы VM (`AvalonDocumentViewModel`, `AvalonToolViewModel`), обёртки сервисов (`DockEventBinder`, `DockWindowService`, `DockingManagerProvider`, `LayoutSerializerService`, `ThemeFactoryService`, `WorkspaceService`), стратегию размещения панелей (`LayoutUpdateStrategy`), `PanesStyleSelector`, и модели персистентности (`ThemeInfo`, `WorkspaceDocument`, `WorkspaceState`).

### Сервисы

| Сервис | Ответственность |
|---|---|
| `IDockingManagerProvider` | Singleton-холдер для `IDockingManager` + событие `ManagerChanged`. `MainWindow` устанавливает ссылку в `WindowLifecycleService.Attach()` при `Loaded`, очищает при `Closed`. |
| `IDockEventBinder` | Relay-сервис: переводит события AvalonDock (`ActiveContentChanged`, `DocumentClosing`, `AnchorableHidden`, `AnchorableClosed`, `LayoutChanged`) в обычные .NET events. Подписывается через `ManagerChanged`, внутри — двухфазная подписка на `LayoutRoot.Updated` через `manager.LayoutChanged`. |
| `IDockWindowService` | Управление `ObservableCollection<AvalonDocumentViewModel>`, активация (`ActiveDocument`), закрытие. Подписан на `dockEventBinder.ActiveContentChanged` и `DocumentClosing` — синхронизирует коллекцию с AvalonDock. |
| `ILayoutSerializerService` | `XmlLayoutSerializer` (v5 API). При десериализации через `LayoutSerializationCallback` помечает неразрешённые элементы `UnresolvedContentHandling.Remove`. |
| `IThemeFactoryService` | Маппинг `"VS2013Light" | "VS2013Dark" | "VS2013Blue"` → `Theme` AvalonDock. Применяет тему к `DockingManager.Theme`. Чистая фабрика, без состояния. |
| `IWorkspaceService` | JSON-сериализация `WorkspaceState` через `System.Text.Json`. `UnsafeRelaxedJsonEscaping` — чтобы кириллица не убегала в `\uXXXX`. |

### ViewModel'и и модели

```text
AvalonDocumentViewModel (partial, ObservableObject)
├── ContentId (по умолчанию "doc-{Guid}"), Title, ToolTip
├── IsActive, IsModified
├── Title автоматически добавляет "*" при IsModified=true
├── virtual SaveToFile() => false
└── virtual ConfirmClose() => true

AvalonToolViewModel (abstract, partial, ObservableObject)
├── Title, ContentId
├── PreferredSide: AnchorSide (Left/Right/Top/Bottom)
├── IsOpen, IsHidden, IsActive
└── Используется LayoutUpdateStrategy для первичного размещения

ThemeInfo              — Name + DisplayName (для меню)
WorkspaceState         — List<WorkspaceDocument> + SavedAt
WorkspaceDocument      — ContentId, Title, FilePath, Content, IsModified, IsActive, DocumentTypeName
```

### `LayoutUpdateStrategy`

`ILayoutUpdateStrategy` — первичное размещение новых anchorable в `AnchorablesSource` по `PreferredSide`:
- `Right` / `Left` → создаёт `LayoutAnchorablePane` шириной 260px, добавляет в `RootPanel` (или оборачивает в горизонтальный `LayoutPanel`, если root вертикальный)
- `Bottom` / `Top` → создаёт `LayoutAnchorablePane` высотой 180px, аналогично с вертикальным `LayoutPanel`

> Только для **первичного** позиционирования. Для Show/Hide во время работы используется `ToolboxService` (см. ниже).

### `PanesStyleSelector`

`StyleSelector` для `LayoutItem`. Различает стили для `AvalonToolViewModel` (`LayoutAnchorableItem`) и `AvalonDocumentViewModel` (`LayoutDocumentItem`). В стилях — `TwoWay`-биндинг `IsActive` на `Model.IsActive`.

---

## Проект `AvalonPad.Core`

Доменные сервисы, базовые VM и модели — **без** прямых ссылок на AvalonDock (ссылается транзитивно через `AvalonPad.Core.AvalonDock`).

### Сервисы

| Сервис | Ответственность |
|---|---|
| `IAppPathService` | **Центральный источник путей приложения** (коммит `03a1369`, 19 Nov 2026). `AppDataDirectory` (`%AppData%/<AppName>/`, fallback на `%Temp%/<AppName>/` при ошибке создания), `Settings/Workspace/LayoutFileName` + `...FilePath`. Конструктор сразу создаёт директорию. `<AppName>` берётся динамически через `Assembly.GetEntryAssembly()?.GetName().Name ?? "AvalonPad"` (применено в коммите `957915a`) — библиотека `AvalonPad.Core` не знает имя приложения. От него зависят `SettingsService` и `PersistenceService` — больше нигде `Path.Combine(Environment..., ...)` не встречается. |
| `ISettingsService` | Настройки в `%AppData%/AvalonPad/appsettings.json` (путь берёт из `IAppPathService`). Создаётся вручную в `App()` **до** Serilog, владеет `GlobalLevelSwitch` / `OutputPanelSwitch`. **`IsFirstRun`** — флаг первого запуска (файла не было, дефолты записаны). Автосохранение через подписку на `Current.PropertyChanged` — делегируется в `PersistenceService.SettingsPropertyChanged`. |
| `ILogService` / `ILogService<T>` | Обёртка над Serilog. `LogService<T>` добавляет `SourceContext` через `ForContext<T>()`. |
| `IPersistenceService` | Координатор персистентности: `Restore()` (вызывает `RestoreHappened`), `Save()`, `ScheduleLayoutSave()` (debounce 500мс + `Dispatcher.BeginInvoke`). Реализация — `PersistenceService` в `AvalonPad` (не в Core). |
| `IDialogService` | `MessageBox.Show` для вопросов и информации. |
| `IFileService` | `OpenFileDialog` / `SaveFileDialog` + `ReadAllText` / `WriteAllText` (с созданием директории). |
| `IToolboxService` | Управление `ObservableCollection<AvalonToolViewModel>`. `Register/Get/Toggle/Show/Hide` + `UpdateLastPositions()`. **Содержит собственную логику `_lastPanes` для восстановления позиции панели при Show/Hide** (см. ниже). |
| `IThemeService` | Высокоуровневое управление темами: `AvailableThemes`, `CurrentTheme`, `ApplyTheme(name)`. Делегирует создание темы в `IThemeFactoryService`. Подписан на `IPersistenceService.RestoreHappened` — восстанавливает тему при старте. |

### Модели

```text
AppSettings : ObservableObject (partial, [ObservableProperty])
├── ThemeName, WindowWidth, WindowHeight, IsMaximized
├── EditorFontFamily, EditorFontSize, ShowLineNumbers
├── LogFilePath, RetainedFileCountLimit, FileLogTemplate
└── GlobalLogLevel, OutputLogLevel      ← runtime, управляют LoggingLevelSwitch
```

Все дефолты хранятся прямо в `AppSettings` (инициализаторы свойств). **Статического `appsettings.json` в `BaseDirectory` нет** — файл в `%AppData%/AvalonPad/` создаётся автоматически при **первом запуске**: `SettingsService.Load()` обнаруживает отсутствие файла, выставляет `IsFirstRun = true` и сам вызывает `Save()`, записывая дефолты на диск.

### Базовые ViewModel

```text
BaseDocumentViewModel : AvalonDocumentViewModel
└── Внедряет IDialogService, реализует ConfirmClose() с диалогом "Сохранить изменения?"

BaseToolboxViewModel : AvalonToolViewModel
└── Конструктор (title, contentId) — для наследников в WPF-проекте
```

---

## Проект `AvalonPad` (WPF-приложение)

### Сервисы

| Сервис | Ответственность |
|---|---|
| `PersistenceService : IPersistenceService` | Полная реализация координатора. Пути к `workspace.json` / `layout.xml` **берёт из `IAppPathService`** (раньше были статическими хардкод-полями). Подписан на `IDockEventBinder.LayoutChanged` → `ScheduleLayoutSave()`. Подписан на `ISettingsService.Current.PropertyChanged` → `SettingsService.Save()` (автосохранение настроек). |
| `WindowLifecycleService` | Подписка на `Window.Loaded/Closed`, регистрирует `DockingManager` в `IDockingManagerProvider`. Если `window.DataContext` реализует `IWindowLifecycleAware` — уведомляет VM о lifecycle событиях. **Не `[Obsolete]`** — это осознанная абстракция, без неё View не может зарегистрировать свой `DockingManager` в Core-сервисе без code-behind логики. Built-in аналога в WPF / .NET Generic Host нет (`IHostedService` / `IHostApplicationLifetime` работают на уровне приложения, не окон). |
| `EditorTabViewModelFactory` | `CreateNew(title, content)`, `CreateFromFile(path)`, `LoadDocumentState(WorkspaceDocument)`. Скрывает `serviceProvider.GetRequiredService<EditorTabViewModel>()` (Transient). |

### ViewModel'и

```text
EditorTabViewModel : BaseDocumentViewModel
├── Content (string), FilePath
├── OnContentChanging => IsModified=true (хук генератора)
├── LoadFile(path), SaveToFile() (через IFileService)
└── Восстанавливается через IEditorTabViewModelFactory.LoadDocumentState

SettingsViewModel : BaseDocumentViewModel, IRecipient<OpenSettingsMessage>
├── sealed (без partial — source-generator атрибутов в классе нет)
├── Синглтон, ContentId="Settings"
├── SettingsService (public)
├── Подписан на SettingsService.Current.PropertyChanged => IsModified=true
├── Receive(OpenSettingsMessage) — открывает/активирует вкладку настроек
└── SaveToFile() — вызывает SettingsService.Save(), сбрасывает IsModified,
    ошибки пишет через ILogService<SettingsViewModel>

MainViewModel : ObservableObject, IWindowLifecycleAware
├── Пробросы для биндинга: Documents, ActiveDocument, Anchorables,
│   AvailableThemes, CurrentThemeName
├── OnWindowLoaded() => mPersistenceService.Restore()
├── OnWindowClosed() => mPersistenceService.Save()
├── Регистрирует стартовые панели: Register<PropertiesToolboxViewModel>(),
│   Register<OutputToolboxViewModel>()
└── Команды: OpenNewDocument, OpenFile, SaveFile, CloseDocument,
    OpenSettings, ToggleProperties, ToggleOutput, Exit, SetTheme

PropertiesToolboxViewModel : BaseToolboxViewModel  (PreferredSide=Right)
├── SelectedObjectName, ObjectCount

OutputToolboxViewModel : BaseToolboxViewModel     (PreferredSide=Bottom)
└── LogLines => ILogService<OutputToolboxViewModel>.LogLines
```

### Связь через Messenger

`MainViewModel.OpenSettingsCommand` отправляет `OpenSettingsMessage` через `IMessenger` (`WeakReferenceMessenger`). `SettingsViewModel` зарегистрирован как `IRecipient<OpenSettingsMessage>` — открывает или активирует себя. Это развязывает `MainViewModel` от прямого знания о `SettingsViewModel`.

### Views

`MainWindow.xaml` — `Menu` (Файл / Панели / Настройки) + `DockingManager` с биндингами:

```xml
<avalonDock:DockingManager
    DocumentsSource="{Binding Documents}"
    AnchorablesSource="{Binding Anchorables}"
    LayoutItemContainerStyleSelector="{StaticResource PanesStyleSelector}">

    <avalonDock:DockingManager.LayoutUpdateStrategy>
        <docking:LayoutUpdateStrategy />
    </avalonDock:DockingManager.LayoutUpdateStrategy>

    <avalonDock:LayoutRoot>
        <!-- Стартовая структура: вертикальный root
             ├ горизонтальный: LayoutDocumentPane + правая pane 260px
             └ нижняя pane 180px (для OutputToolbox) -->
    </avalonDock:LayoutRoot>
</avalonDock:DockingManager>
```

`MainWindow.xaml.cs` минимален: `DataContext = viewModel` + `lifecycle.Attach(this, dockManager)`. Вся lifecycle-логика — в `WindowLifecycleService`, не в code-behind.

`App.xaml` содержит глобальные `DataTemplate` — AvalonDock автоматически подхватит View по типу VM:
`EditorTabViewModel → EditorTabView`, `PropertiesToolboxViewModel → PropertiesToolView`, `OutputToolboxViewModel → OutputToolView`, `SettingsViewModel → SettingsView`.

---

## DI-регистрация (`App.xaml.cs`)

`AppPathService` и `SettingsService` создаются вручную в конструкторе `App()` **до** `Log.Logger` — Serilog использует `GlobalLevelSwitch`/`OutputPanelSwitch` из `SettingsService`. После этого оба регистрируются в DI. `IConfiguration` **не используется** — `SettingsService` сам парсит JSON через `JsonDocument`, отдельный конфиг-пайплайн `Microsoft.Extensions.Configuration` избыточен (убран в коммите `03a1369`, мёртвый код вычищен в `15d4d28`).

```text
App()
├── new AppPathService()                          — AppDataDirectory (с fallback на %Temp%)
├── new SettingsService(appPathService)           — берёт путь из IAppPathService
├── settingsService.Load()                        — при отсутствии файла: IsFirstRun=true, Save() пишет дефолты
│                                                  — при ошибке парсинга поля: Dictionary<имя, ошибка>
│                                                  — при прочих исключениях: ловим в mLoadException
├── Log.Logger = new LoggerConfiguration()
│       .WriteTo.Async(... File(settings.LogFilePath,
│                              levelSwitch: settingsService.GlobalLevelSwitch, ...))
│       .WriteTo.ObservableSink(mUiLogLines,
│                              levelSwitch: settingsService.OutputPanelSwitch)
│       .CreateLogger()
├── if (settingsService.IsFirstRun)  Log.Information("Первый запуск: ...")
├── if (mLoadException != null)  Log.Error(ex, "Ошибка при загрузке настроек, ...")
├── foreach (mErrorConfigProperties)  Log.Warning("Настройка {propertyName} не прочитана: {propertyError}", ...)
└── Host.CreateDefaultBuilder().ConfigureServices(services =>
    {
        services.AddSingleton<IAppPathService>(appPathService);
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton(Log.Logger);
        services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

        services.AddSingleton<ILogService>(sp => new LogService(Log.Logger, mUiLogLines));
        services.AddSingleton(typeof(ILogService<>), typeof(LogService<>));

        services.AddSingleton<IPersistenceService, PersistenceService>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IMessenger, WeakReferenceMessenger>();

        services.AddSingleton<IDockingManagerProvider, DockingManagerProvider>();
        services.AddSingleton<IDockEventBinder, DockEventBinder>();
        services.AddSingleton<IWindowLifecycleService, WindowLifecycleService>();
        services.AddSingleton<ILayoutSerializerService, LayoutSerializerService>();
        services.AddSingleton<IThemeFactoryService, ThemeFactoryService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IToolboxService, ToolboxService>();
        services.AddSingleton<IEditorTabViewModelFactory, EditorTabViewModelFactory>();
        services.AddSingleton<IDockWindowService, DockWindowService>();

        services.AddTransient<EditorTabViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<PropertiesToolboxViewModel>();
        services.AddSingleton<OutputToolboxViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    });
```

---

## Ключевые паттерны

### 1. Отслеживание изменений layout

`IDockEventBinder` использует `LayoutRoot.Updated`, **не** `manager.LayoutChanged` (последний срабатывает только при полной замене `Layout`). Двухфазная подписка:

1. Подписываемся на `manager.LayoutChanged` (срабатывает при установке/замене `Layout`)
2. Когда `manager.Layout` присваивается — отписываемся от старого `Layout.Updated`, подписываемся на новый

`LayoutRoot.Updated` срабатывает при **любых** структурных изменениях: перемещение, показ/скрытие, dock/undock, смена ширины.

### 2. PanesStyleSelector

`LayoutItemContainerStyleSelector` различает стили для документов и тулбоксов. В стилях — `TwoWay`-биндинг `IsActive`:

```xml
<Setter Property="IsActive" Value="{Binding Model.IsActive, Mode=TwoWay}" />
```

### 3. LayoutUpdateStrategy

Размещает новые anchorable в `AnchorablesSource` по `PreferredSide`. **Только первичное** размещение; для Show/Hide во время работы — `ToolboxService`.

### 4. SettingsService

- `AppSettings` — POCO с `[ObservableProperty]`, дефолты в инициализаторах свойств
- `SettingsService` создаётся в `App()` **до** Serilog (получает `IAppPathService` в конструктор), владеет `LoggingLevelSwitch`-ами
- Путь к файлу берётся из `IAppPathService` и хранится в приватном `mSettingsFilePath`
- `Load()` — парсит JSON через `JsonDocument`, ошибки по полям возвращает как `Dictionary<string,string>` (а не валит всю загрузку). В начале вызывается `mErrorConfigProperties.Clear()` — повторная загрузка не накапливает устаревшие ошибки
- **Первый запуск** (`!File.Exists(mSettingsFilePath)`): `IsFirstRun = true`, применяются дефолтные уровни логов, **вызывается `Save()`** — файл создаётся с дефолтами на диске. **Исключение не бросается** — caller'у не нужно ловить `FileNotFoundException` (коммит `03a1369`)
- `Save()` — **атомарная запись**: пишем в `appsettings.json.tmp`, затем `File.Move(temp, target, overwrite: true)`. Защита от повреждения файла при сбое посреди записи
- Автосохранение: `PersistenceService` подписан на `SettingsService.Current.PropertyChanged` и зовёт `SettingsService.Save()`
- `_isLoading` удалён — вместо него автосохранение не отключается на загрузке (т.к. `Load()` пишет напрямую через `propInfo.SetValue`, а не через `[ObservableProperty]`-сеттеры — `PropertyChanged` не срабатывает)

### 5. PersistenceService

Пути к `workspace.json` и `layout.xml` **берёт из `IAppPathService`** в конструкторе (раньше были `static readonly` полями с хардкодом `Path.Combine(Environment..., "AvalonPad")`).

```csharp
public void ScheduleLayoutSave()
{
    Interlocked.Increment(ref mLayoutChangeCount);
    Task.Run(async () =>
    {
        await Task.Delay(cSaveLayoutDelayMs);  // 500мс
        if (Interlocked.Decrement(ref mLayoutChangeCount) == 0)
        {
            // Сериализация обращается к DockingManager.Layout (DependencyProperty) —
            // должна выполняться в UI-потоке
            Application.Current?.Dispatcher.BeginInvoke(() => SaveLayout());
        }
    });
}
```

`Save()` сохраняет **layout + workspace**. `Restore()` вызывает `RestoreHappened` → подписчики (`ThemeService`) восстанавливают своё состояние, затем восстанавливает workspace (через тип-дискриминатор `DocumentTypeName`), затем layout (через `Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ...)` — чтобы `LayoutItem`-ы уже были созданы).

### 6. Восстановление состояния (Restore Event Pattern)

```text
App.OnStartup → mHost.StartAsync() → MainWindow.Show() → Window.Loaded
    ↓
WindowLifecycleService.OnLoaded
    ├── IDockingManagerProvider.Manager = dockManager   → DockEventBinder подписывается
    └── if (window.DataContext is IWindowLifecycleAware aware)
            aware.OnWindowLoaded()
    ↓
MainViewModel.OnWindowLoaded()
    ↓
PersistenceService.Restore()
    ├── RestoreHappened?.Invoke()  → ThemeService.ApplyTheme(settings.ThemeName)
    ├── RestoreWorkspace()         → IWorkspaceService.Load → IEditorTabViewModelFactory.LoadDocumentState
    └── RestoreLayout()            → Dispatcher.BeginInvoke(Loaded) → ILayoutSerializerService.Deserialize
```

### 7. ToolboxService и `mLastPanes` — спорная логика

`ToolboxService` поддерживает собственный словарь `Dictionary<string, LayoutAnchorablePane> mLastPanes`, обновляемый при каждом `IDockEventBinder.LayoutChanged` через `UpdateLastPositions()`. При `Show<T>()`:

```csharp
private void ShowInternal(AvalonToolViewModel toolbox, LayoutAnchorable anchorable)
{
    // Сбрасываем PreviousContainer — не доверяем ему
    if (anchorable is ILayoutPreviousContainer prev)
        prev.PreviousContainer = null;

    // Если есть запомненная pane и она жива (в дереве) — добавляем туда
    if (mLastPanes.TryGetValue(toolbox.ContentId, out var pane) && pane.Root != null)
    {
        if (anchorable.Parent is LayoutAnchorablePane oldParent)
            oldParent.Children.Remove(anchorable);

        pane.Children.Add(anchorable);
    }

    anchorable.Show();
    anchorable.IsActive = true;
}
```

В коде явно написано: **«Сбрасываем PreviousContainer — не доверяем ему»**. То есть `mLastPanes` — это собственная реализация того, что AvalonDock делает через `ILayoutPreviousContainer.PreviousContainer`. Логика работает, но дублирует встроенную; при рефакторинге кандидат на упрощение — либо довериться `PreviousContainer`, либо вынести в `LayoutUpdateStrategy`.

### 8. WindowLifecycleService — почему не built-in

`[Obsolete]`-маркер с сервиса снят в коммите `15d4d28` — он был wishful thinking, никакого built-in аналога в WPF / .NET Generic Host нет:

- `IHostedService.StartAsync/StopAsync` — для lifecycle **приложения** (host start/stop), не для отдельных окон
- `IHostApplicationLifetime` — то же самое, на уровне приложения
- `Window.Loaded` / `Window.Closed` — это и есть «встроенный» WPF-механизм, но он требует либо code-behind (что противоречит MVVM), либо attached behavior (что тащит NuGet `Microsoft.Xaml.Behaviors.Wpf` и больше кода, чем текущий сервис)

Сервис решает реальную проблему: View имеет ресурс (`DockingManager`), Core имеет consumer'а (`IDockingManagerProvider`); без middleware-сервиса, который перекидывает мост, не обойтись без code-behind. Текущая реализация — осознанная абстракция, не костыль.

---

## Файлы персистентности

Расположение: `%AppData%/AvalonPad/` (при недоступности — `%Temp%/AvalonPad/`, см. `AppPathService`). Все пути централизованы в `IAppPathService`.

| Файл | Содержимое | Когда пишется |
|---|---|---|
| `appsettings.json` | JSON-сериализация `AppSettings` (секция `"App"`, с комментариями из `[Description]`) | Атомарно при любом изменении настройки (через `PersistenceService.SettingsPropertyChanged → SettingsService.Save()`). **При первом запуске** файл создаётся самим `SettingsService.Load()` — после обнаружения отсутствия файла вызывается `Save()`, пишущий дефолты из `AppSettings`. |
| `layout.xml` | Позиции панелей и floating-окон (`XmlLayoutSerializer`) | Debounced autosave: 500мс после `LayoutChanged`, через `Dispatcher.BeginInvoke`. |
| `workspace.json` | `WorkspaceState`: список `WorkspaceDocument` (включая `DocumentTypeName`, `Content`, `FilePath`, `IsModified`) + `SavedAt` | При `OnWindowClosed()` → `PersistenceService.Save()`. |

### Что сохраняется в `workspace.json`

Сохраняются **все** документы из `IDockWindowService.Documents` — не только `EditorTabViewModel`. Для каждого пишется `DocumentTypeName = doc.GetType().FullName`. При восстановлении:

- Если `DocumentTypeName == typeof(EditorTabViewModel).FullName` — `mEditorFactory.LoadDocumentState(d)`
- Если `DocumentTypeName == typeof(SettingsViewModel).FullName` — берётся синглтон из `IServiceProvider`, восстанавливаются только `IsActive` и `IsModified` (остальное статично)

---

## Сводка зависимостей сервисов

```text
IAppPathService                ← не зависит ни от кого (просто пути + Directory.CreateDirectory)
        ↑
ISettingsService               ← зависит от IAppPathService
IDockingManagerProvider        ← не зависит ни от кого
        ↑
IDockEventBinder               ← зависит от IDockingManagerProvider
        ↑
IToolboxService                ← IDockingManagerProvider, IDockEventBinder
IDockWindowService             ← IDockEventBinder
        ↑
IPersistenceService            ← IDockEventBinder, IDockWindowService,
                                  ILayoutSerializerService, IWorkspaceService,
                                  IEditorTabViewModelFactory, ISettingsService,
                                  IAppPathService
        ↑
IThemeService                  ← IDockingManagerProvider, ISettingsService,
                                  IThemeFactoryService, IPersistenceService
        ↑
MainViewModel                  ← координатор, зависит от всех
```

**Циклов зависимостей нет** — поток однонаправленный.

---

## Финальные заметки

1. **AppPathService** — центральный источник путей (коммит `03a1369`, 19 Nov 2026). Создаётся вручную в `App()` **до** `SettingsService`. `AppDataDirectory` (`%AppData%/<AppName>/`, fallback на `%Temp%/<AppName>/`). `<AppName>` берётся через `Assembly.GetEntryAssembly()?.GetName().Name ?? "AvalonPad"` — библиотека Core не знает имя приложения (применено в `957915a` при ренейме).
2. **SettingsService** создаётся вручную в `App()` конструкторе **до** Serilog (получает `IAppPathService` в ctor) — чтобы Serilog использовал `LoggingLevelSwitch` из сервиса. Ошибки загрузки настроек не валят приложение: `mLoadException` и `mErrorConfigProperties` логируются, дальше работаем на дефолтах.
3. **appsettings.json отсутствует в проекте** (удалён в коммите `02111b8`, 17 Nov 2026). Дефолты хранятся в `AppSettings.cs`. Файл в `%AppData%/AvalonPad/` создаётся **при первом запуске** самим `SettingsService.Load()` — после обнаружения отсутствия файла вызывается `Save()` (атомарно: `tmp` + `File.Move`), пишущий дефолты. `IsFirstRun=true` сигнализирует `App()` для логирования.
4. **`IConfiguration` полностью убран** (коммит `03a1369`, 19 Nov 2026; мёртвый код вычищен в `15d4d28`, 24 Nov 2026). Раньше `App.xaml.cs` строил `ConfigurationBuilder.AddJsonFile(...)` и регистрировал `IConfiguration` в DI — но ни один сервис его не инжектил (`SettingsService` парсит JSON сам через `JsonDocument`, Serilog настраивался вручную через `Log.Logger`). Из `AvalonPad.Core.csproj` удалены пакеты `Microsoft.Extensions.Configuration.*` и `Serilog.Settings.Configuration`.
5. **SettingsViewModel.SaveToFile()** реально вызывает `SettingsService.Save()` (раньше был no-op, просто `IsModified = false`). Ошибки логируются через `ILogService<SettingsViewModel>`.
6. **SettingsService.Load()** при отсутствии файла **больше не бросает исключение** (коммит `03a1369`) — выставляет `IsFirstRun=true` и сам пишет дефолты. До этого бросал `FileNotFoundException` (а ещё раньше — `FileLoadException`).
7. **`WindowLifecycleService` больше не `[Obsolete]`** (коммит `15d4d28`, 24 Nov 2026). Built-in аналога в WPF / .NET Generic Host нет — `IHostedService`/`IHostApplicationLifetime` работают на уровне приложения, не окон. Сервис — осознанная абстракция, без неё View не может зарегистрировать свой `DockingManager` в Core-сервисе без code-behind.
8. **Разделение `IThemeFactoryService` / `IThemeService`**: фабрика (чисто AvalonDock) живёт в `Core.AvalonDock`, высокоуровневый сервис (хранит текущую тему, восстанавливает из настроек) — в `Core`.
9. **`ToolboxService.mLastPanes`** — собственная реализация того, что в AvalonDock делает `PreviousContainer`. В коде явно помечено «не доверяем ему». Логика рабочая, но дублирует встроенную — кандидат на упрощение.
10. **MainViewModel** максимально чистый — только команды, пробросы свойств и реализация `IWindowLifecycleAware`.
11. **`SettingsViewModel`** — `sealed` (без `partial` — source-generator атрибутов в классе нет). Синглтон с фиксированным `ContentId="Settings"`. Открывается через `IMessenger` (`OpenSettingsMessage`), не через прямой вызов из `MainViewModel`.
12. **`EditorTabViewModel.OnContentChanging`** взводит `IsModified=true`. Чтобы загрузка файла через `LoadFile()` не помечала документ как изменённый, `IsModified` сбрасывается в `false` после присвоения `Content`.
13. **File-scoped namespaces во всех 42 `.cs`-файлах** (коммиты `0d00c36`, `d92ec83`, `ed040ec`). Последовательный стиль по всему проекту.

---

*Документ актуализирован по состоянию репо на коммит `15d4d28` (24 Nov 2026).*

