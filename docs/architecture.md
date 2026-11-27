# Архитектура: AvalonPad как база, AdamPad и WindowPad как приложения

> Составлено: 26 ноября 2026.
> Отправная точка: `master` с пятью проектами в решении (клоны AdamPad/WindowPad добавлены из src/AvalonPad).
> Стиль документа согласован с `docs/code-review.md`.

---

## 1. Задача

Вести два производных приложения — **AdamPad** (приложение к роботу) и **WindowPad**
(приложение к БД и окнам) — на общей базе AvalonPad без веток и без дублирования
общего кода.

Сформулированное правило: *изменения, нужные обоим приложениям, делаются в базе;
изменения под конкретный проект — в его модуле.* Структура ниже делает это правило
механическим: границей между «базой» и «проектом» становится не ветка, а граница
сборки.

## 2. Целевая структура

```
src/
  base/
    AvalonPad.Core/                ← сервисы и контракты базы (как есть)
    AvalonPad.Core.AvalonDock/     ← каркас докинга (как есть)
    AvalonPad.Shell/               ← НОВОЕ: WPF-каркас — окно, меню, персистентность
  apps/
    AdamPad/
      AdamPad.Core/                ← сервисы робота: протокол, модели, хранилище
      AdamPad/                     ← WinExe: App.xaml, модуль, Views, VM
    WindowPad/
      WindowPad.Core/              ← сервисы БД: DbContext, ORM-репозитории
      WindowPad/                   ← WinExe: App.xaml, модуль, Views, VM
```

Физическая раскладка `base/`/`apps/` — косметика, выполняется последним шагом
(правка путей в `.slnx`, namespaces не трогаются).

## 3. Диаграмма зависимостей (строгая версия — принята)

```
AdamPad (WinExe) ──→ AdamPad.Core ──┐
                                    ├──→ AvalonPad.Core ──→ AvalonPad.Core.AvalonDock ──→ NuGet
WindowPad (WinExe) ─→ WindowPad.Core ┘        ↑
AdamPad (WinExe) ──→ AvalonPad.Shell ─────────┘
WindowPad (WinExe) ─↗
```

Текущее состояние (до миграции): вертикаль `AvalonPad → AvalonPad.Core →
AvalonPad.Core.AvalonDock` — та же, Shell появится в процессе.

### Жёсткие правила

1. **`AvalonPad.Core.AvalonDock` — вершина пирамиды.** Только NuGet
   (`Dirkster.AvalonDock`, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Logging.Abstractions`),
   внутренних ссылок нет. *(уже так)*
2. **`AvalonPad.Core` → `AvalonPad.Core.AvalonDock`.** *(уже так)*
3. **`AvalonPad.Shell` → `AvalonPad.Core`.** Сборочных ссылок на приложения нет, и типы приложений Shell в компиляции не видит. Контент приложений Shell получает в рантайме, двумя каналами:
   - **DI:** `IAppModule` (Tools/Documents/MenuItems) инжектится в Shell-объекты конструктором; регистрация — в `App.xaml.cs` приложения.
   - **DataTemplates:** сопоставление ViewModel → View объявляется в `App.xaml` приложения (шаблон уже так работает — см. комментарий в `MainWindow.xaml`).
   Направление знаний: **ссылки компиляции — вниз, данные — вверх.** Shell не обязан знать приложения, он обязан уметь потреблять контракт.
4. **`AdamPad.Core` / `WindowPad.Core` → только `AvalonPad.Core`.**
   Ссылки на Shell у домена нет и не появится: всё, что нужно домену
   (`SettingsService`, `AppPathService`, базовые контракты), приходит транзитивно
   через `AvalonPad.Core`. Типы AvalonDock тоже текут транзитивно — их
   использование в app-Core запрещено конвенцией (см. п. 5).
5. **Сервисы не знают UI.** Сервису из app-Core нельзя импортировать
   `AvalonDock.*`, `AvalonPad.Shell.*` и дёргать `MainViewModel`/`PersistenceService`
   напрямую. Событие «открой документ» передаётся через `WeakReferenceMessenger`
   (образец в проекте: `OpenSettingsMessage`); VM в WinExe подписывается и
   вызывает сервис оболочки.
6. **WinExe — только клей.** `App.xaml.cs` с DI-регистрацией, реализация
   `IAppModule`, Views, VM. Вся логика — в соседнем `*.Core`. VM при этом живут в
   WinExe (опираются на `AvalonDocumentViewModel`/`AvalonToolViewModel`); если
   логика VM распухает — её доменную часть выносить в `*.Core`, VM остаётся
   тонкой обёрткой.
7. **Приложения не видят друг друга.** AdamPad никогда не ссылается на WindowPad
   и наоборот. Пакеты (WebView2, ORM и т.п.) ставятся только в csproj того
   проекта, где используется.
8. **Никаких ссылок «вверх» из базы в приложения. Нарушение направления — самое
   дорогое, что может случиться со схемой.**

## 4. Контракт приложения

Оболочка не знает содержимое приложений — она потребляет контракт:

```csharp
// AvalonPad.Core (как реализовано на шаге 2б)
public interface IAppModule
{
    string AppTitle { get; }
    IEnumerable<ToolRegistration> Tools     { get; }  // панели
    IEnumerable<MenuRegistration> MenuItems { get; }  // меню и команды
}
```

- `MainViewModel` и `MainWindow` живут в Shell и **параметризованы**: группы
  «Файл»/«Панели инструментов» строятся из `MenuItems` (обёртка
  `MenuItemViewModel`, разделитель — по `Header == null`), панели — из `Tools`
  (`ToolboxService.Register(Type)`), заголовок окна — из `AppTitle`.
- Документы — не через `IAppModule.Documents`, а через каталог
  `IDocumentCatalog` (шаг 2а): знание о типах живёт в приложении, Shell видит
  только `AvalonDocumentViewModel`. Обобщённая `IDocumentViewModelFactory`
  не потребовалась.
- Команда модуля открывает документ напрямую через Shell-сервис
  `IDockWindowService.OpenDocument(...)`: сохранение в workspace и
  восстановление работают для любого типа одинаково (тип записан как
  `typeof(X).FullName` в `WorkspaceDocument`).
- Модуль регистрируется в DI **до** восстановления workspace — иначе каталог
  документов пуст в момент restore.

## 5. Куда класть новый код — проверка перед коммитом

| Вопрос | Ответ | Куда |
|---|---|---|
| Нужно хотя бы одному приложению? | да | модуль того приложения (WinExe или его Core — по слою) |
| Нужно обоим сейчас или скоро? | да | Shell (или перенос из модуля позже) |
| Меняет поведение окон/докинга? | да | `AvalonPad.Core` / `AvalonPad.Core.AvalonDock` |
| Хочется положить сервис с `using System.Windows`? | сигнал | не тот проект: UI-зависимости — в WinExe/Shell |

Промежуточный тест, когда сомнительно: **содержит ли класс ссылку на чужую
предметную область?** Нет — можно в базу; есть — только в свой модуль. Ошибка
места дешева: перенос файла без правки зависимостей.

## 6. Типовые сценарии

### 6.1. Новый тип документа в одном приложении (пример: окно браузера у AdamPad)

1. `AdamPad/ViewModels/BrowserDocumentViewModel.cs` — наследник базового контракта.
   Не сохраняется в файл → наследовать от `AvalonDocumentViewModel` (не
   `BaseDocumentViewModel`), `ConfirmClose()` не переопределять.
2. `AdamPad/Views/BrowserView.xaml` — UserControl; пакет `Microsoft.Web.WebView2`
   — только в `AdamPad.csproj`.
3. Регистрация в `AdamPadModule`: `DocumentRegistration("BrowserDoc", view, factory)`
   + пункт меню.

База и WindowPad не меняются. SDK-style csproj подхватит файлы автоматически.

### 6.2. ORM в одном приложении (пример: WindowPad)

Пакет ORM — только в `WindowPad.csproj`; DbContext и репозитории — в
`WindowPad.Core`; регистрация — в `WindowPad/App.xaml.cs`. Собственные данные
приложения (строки подключения и т.п.) — в собственной области
`%AppData%/WindowPad/` через `IAppPathService`, без засорения общего
`appsettings.json`.

### 6.3. Документ вырос из проектного в базовое

1. Файлы VM и View переезжают из `apps/<Имя>/` в Shell (namespaces не трогать —
   код продолжит компилироваться).
2. Строка `DocumentRegistration(...)` добавляется в модуль второго приложения.
3. NuGet-пакет переезжает в csproj Shell.

## 7. План миграции

Каждый шаг — отдельный рабочий коммит (сборка не рвётся).

1. **Shell создаётся, дубликаты убираются.** Библиотека `AvalonPad.Shell`
   (`UseWPF`); перенести из App-слоя всё, что не знает про содержимое
   приложения: `WindowLifecycleService`, `Messages`, `Extensions`,
   `Converters`, `SettingsViewModel/View`, `Output/Properties`.
   `PersistenceService` — НЕ на этом шаге: он ссылается на `EditorTabViewModel`
   (содержимое приложения) и переезжает на шаге 2 вместе с выделением
   каталога документов. Namespaces сохранить как есть (`AvalonPad.Services`
   и т.п.) — тогда код компилируется без правок; в XAML добавляются префиксы
   `shellvm`/`shellviews` с `assembly=AvalonPad.Shell` для переезжающих типов
   (DataTemplates в App.xaml, конвертеры в MainWindow.xaml).
2. **Контракт и параметризация.** Шаг разделён на два подшага:
   - **2а (выполнен):** `PersistenceService` переезжает в Shell; знание о
     конкретных документах заменяется каталогом `IDocumentCatalog` +
     `IPersistableDocument` (контракт — в `Core.AvalonDock`, реализация — в
     приложении, регистрация — в `DocumentCatalog`).
   - **2б (выполнен):** `IAppModule` в `AvalonPad.Core`; редакторные команды
     (New/Open) вырезаются из `MainViewModel` в модуль (`AvalonPadModule`);
     «Сохранить»/«Закрыть» остаются в Shell — они работают с любым документом;
     меню «Файл»/«Панели инструментов» — из `MenuItems` модуля, «Настройки» и
     подменю «Темы» — статические в Shell; панели — из `Tools`;
     `MainViewModel` и `MainWindow` переезжают в Shell.
3. **Приложения схлопываются (выполнено, шаги 3а–3в).** AdamPad/WindowPad:
   `App.xaml(.cs)` + модуль + свои документы/панели; созданы пустые
   `AdamPad.Core` / `WindowPad.Core` (csproj + ссылка на `AvalonPad.Core`);
   настройки разделены (п. 7.4).
4. **`src/AvalonPad` удалён (выполнено).** Оба приложения собраны и
   поведенчески равны прототипу — эталон больше не нужен; история живёт в git.
5. **Косметика:** раскладка `base/`/`apps/`, актуализация README (см.
   `docs/code-review.md`, п. 6).

## 8. Подводные камни

- **Generic-наследование WPF не использовать:** `x:Class` не умеет наследовать
  `Window<T>`/`UserControl<T>`. Параметризация — контракты и DataTemplates.
- **Порядок DI:** модуль до restore workspace (см. п. 4).
- **AppSettings — базовый класс:** наследование и разделение настроек —
  п. 7.4.
- **ContentId** уникальны внутри приложения; layout-файлы у приложений свои
  (`%AppData%/AdamPad`, `%AppData%/WindowPad` — AppPathService берёт имя из
  сборки).
- **Не переименовывать namespaces на старте миграции** — нулевой дифф по
  потребителям; переименование — отдельное решение, не частью переезда.
  Итог: Shell переименован отдельным коммитом 27.11 (`AvalonPad.Shell.*` —
  одна сборка — свои namespaces, нет двойных имён с приложением);
  `src/AvalonPad` удалён (шаг 4); клоны уже на
  `AdamPad.*`/`WindowPad.*` (заданы при создании). Побочный эффект:
  workspace.json со старым именем SettingsViewModel восстановится без этой
  вкладки — каталог (шаг 2а) пропустит неизвестный тип с предупреждением.

### 7.4. Настройки по приложениям (выполнено на шаге 3в)

Решено: у каждого приложения — свой класс настроек, наследующий общий.
Существующий код почти готов, нужны три правки:

1. `AppSettings` (AvalonPad.Core) — снять `sealed`; оставить в нём только
   общие секции: тема, окно (Width/Height/IsMaximized), логи (путь, лимиты,
   шаблон, уровни). Поля `EditorFont*` временно остаются здесь (клоны пока
   содержат редактор); при расхождении приложений — переехать в настройки
   приложения, где останется редактор.
2. `SettingsService`: принимать экземпляр в конструкторе
   (`SettingsService(AppSettings current, IAppPathService paths)`) вместо
   `Current { get; } = new()`; отражение `typeof(AppSettings)` заменить на
   `Current.GetType()` в Load и Save — производные свойства подхватятся
   автоматически, контракт сериализации не меняется (JSON per-app уже в
   `%AppData%/<имя сборки>/`).
3. DI (в каждом приложении) — экземпляр создаётся в конструкторе `App()`
   до построения хоста, потому что логгеру нужны уровни из `Load()`:

   ```csharp
   AdamPadSettings settings = new();
   SettingsService settingsService = new(settings, appPathService);

   services.AddSingleton<AdamPadSettings>(settings);
   services.AddSingleton<ISettingsService>(settingsService);
   ```

   Сервисы робота инжектят `AdamPadSettings` напрямую — это тот же инстанс,
   что и за `ISettingsService.Current`. Обобщённый `SettingsService<T>` не
   нужен: Shell работает с интерфейсом через базовый тип, а резолв по
   интерфейсу с несколькими экземплярами был бы головной болью.

   Классы `AdamPadSettings`/`WindowPadSettings` живут в новых пустых
   `AdamPad.Core`/`WindowPad.Core` (csproj + ссылка на `AvalonPad.Core`),
   namespace `{App}.Core.Models`.

Фиксация из ревью §3.1 (`TryAdd`) выполнена отдельным шагом 3а.

## 9. Принятые решения и открытые вопросы

Принято:
- Монорепа вместо веток (27.11 дисциплина «сливаем базу в ветки» отклонена как
  merge hell).
- Строгая версия зависимостей: app-Core → `AvalonPad.Core`; Shell остаётся
  выше домена. Ссылка `app-Core → Shell` не добавляется; если появится законный
  кейс — изменить правило осознанно.
- Shell не ссылается на приложения и не знает их типы в компиляции: контент
  приходит через DI (`IAppModule`) и DataTemplates в App.xaml приложения
  (уже так работает). Ссылки — вниз, данные — вверх.
- Настройки: общее в `AppSettings` (Core), специфичное — в классах
  `<App>Settings : AppSettings` в app-Core; `SettingsService` принимает
  экземпляр и отражается по `Current.GetType()`. Выполнено на шаге 3в
  (п. 7.4).
- Открытие документов — через сервис оболочки, не напрямую из модулей.
- `OutputTool`/`PropertiesTool` — в Shell; если окажутся специфичными —
  переехать в модули.

Открыто:
- Название общей библиотеки: `AvalonPad.Shell` — рабочее; окончательное имя
  закрепить при создании проекта.
- Механика команд меню: решена на шаге 2б — `MenuRegistration(Header, Command,
  InputGesture, ParentHeader)` + `MenuItemViewModel` в Shell; разделитель —
  `Header == null`.

---

*Документ отражает договорённость от 26.11.2026. Изменения правил — только
осознанные, с правкой этого документа в том же коммите.*
