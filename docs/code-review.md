# Аудит кода AvalonPad

> Составлено: 26 ноября 2026.
> Проверено по состоянию `master` на коммит `81f65fe` («Изменен диалог сохранения документа»).
> Охват: все 42 `.cs`-файла, XAML-представления, `csproj`. Каждый пункт ниже сверён с актуальным кодом, а не с README.

---

## 1. Решено в ходе аудита

### 1.1. Диалог закрытия изменённого документа — ✅ закрыто (коммиты `13cb28c`, `81f65fe`)

Было: `MessageBoxButton.YesNo` без варианта «продолжить работу над документом».
Стало: `IDialogService.ShowYesNoCancel` + switch-expression в `BaseDocumentViewModel.ConfirmClose()`.
Замечание снято.

Пара фактов для памяти (Win32-поведение `MessageBox`):

- У `YesNo` крестик в заголовке **отображается, но неактивен**, и Esc ничего не делает — это поведение системы, а не WPF.
- У `YesNoCancel` крестик и Esc активны и возвращают `Cancel` — ветка `_ => false` в switch покрывает и кнопку, и Esc, и `None`.

---

## 2. Высокий приоритет

### 2.1. Нет глобальных обработчиков исключений

Ни `DispatcherUnhandledException`, ни `AppDomain.UnhandledException` не зарегистрированы. Любое необработанное исключение в UI закроет приложение, и Serilog запишет только события до момента падения — причина останется вчерашней догадкой.

Между тем в `App()` уже есть культура «не валим на ошибках настроек» — она незакончена без страховочной сетки.

**Предложение** (`App.xaml.cs`):

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    DispatcherUnhandledException += (_, args) =>
    {
        Log.Fatal(args.Exception, "Необработанное исключение в UI-потоке");
        args.Handled = true; // или false, если политика — падать громко
    };

    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        Log.Fatal(args.ExceptionObject as Exception, "Фатальное исключение вне UI-потока");

    // ... существующий код
}
```

Если выбрать `args.Handled = true`, дополнить диалогом «Приложение восстановлено, детали в логе».

### 2.2. Узкие `catch` на путях восстановления

| Место | Ловит | Пропускает |
|---|---|---|
| `WorkspaceService.Load` | `JsonException` | `IOException`, `DirectoryNotFoundException` и др. — исключение уйдёт вверх в `OnWindowLoaded` |
| `LayoutSerializerService.Deserialize` | `IOException`, `XmlException`, `InvalidOperationException` | всё остальное — уйдёт в замыкание `Dispatcher.BeginInvoke` при старте |

Оба пути — восстановление при старте приложения. Повреждённый или занятый файл состояния не должен убивать запуск.

**Предложение:** сузить контракт до «восстановление не критично»: `catch (Exception ex)` с записью в `ILogService` и возвратом дефолтного состояния. Конкретные типы (`IOException`, `XmlException`) оставить как fast-path перед общим catch — это улучшит диагностику.

---

## 3. Средний приоритет

### 3.1. `SettingsService.Load()`: два скрытых дефекта

**а) Неизвестные поля JSON молча выбрасываются.**

```csharp
var value = jsonProp.Value.Deserialize(propInfo?.PropertyType ?? typeof(string), mSerializerOptions);
propInfo?.SetValue(Current, value);   // propInfo == null → значение просто потеряно
```

Если в `appsettings.json` окажется незнакомое поле (например, после отката версии), пользователь не узнает, что его настройка не работает: ни ошибки в `mErrorConfigProperties`, ни лога.

**Предложение:** при `propInfo is null` — `Log.Warning("Настройка {name} не распознана (лишняя в текущей версии)")`.

**б) Дубликат имени поля в JSON → падение всей загрузки.**

`mErrorConfigProperties.Add(name, msg)` бросит `ArgumentException` при втором вхождении того же ключа, исключение поймает только `App()` (строковый `mLoadException`). Поле настроек, встреченное дважды, не должно отключать загрузку остальных.

**Предложение:** `mErrorConfigProperties[jsonProp.Name] = ex.Message` — словарь перезапишет дубликат, остальная загрузка продолжится.

Заодно: поиск свойства через `FirstOrDefault(p => p.Name == jsonProp.Name)` регистрозависим — JSON-поле `themename` молча проигнорируется. Стоит `OrdinalIgnoreCase`.

### 3.2. `LayoutUpdateStrategy` всегда создаёт новую панель

Блок объединения с существующей pane закомментирован, а поиск `existingPane` остался как мёртвый код:

```csharp
var existingPane = layout.Descendents()...  // вычисляется и не используется

/*if (existingPane != null)
{
    existingPane.Children.Add(anchorable);
    return true;
}*/
```

Следствие: каждый новый anchorable (Properties, Output и будущие панели) получает **собственную** pane 260/180px, а захардкоженные пустые панели в `MainWindow.xaml` (`LayoutAnchorablePane DockWidth="260"`) дублируются, а не используются.

**Предложение — выбрать одно:**
1. Вернуть ветку: если открытая панель нужной стороны есть — добавлять вкладку туда (флаг «объединять» как свойство `AvalonToolViewModel`).
2. Удалить мёртвый код и заодно мёртвые пустые панели из XAML, оставив стратегию единственным размещением.

### 3.3. `ToolboxService.mLastPanes` дублирует встроенный механизм

README это честно признаёт: собственный словарь повторяет то, что AvalonDock делает через `ILayoutPreviousContainer.PreviousContainer` (в коде даже комментарий «не доверяем ему»).

Дополнительное замечание сверх README: `UpdateLastPositions()` вызывается на **каждый** `LayoutRoot.Updated` (включая изменение ширины панеля при перетаскивании) и для каждой панели обходит всё дерево через `Descendents()` — O(N·M). При двух панелях это незаметно, но паттерн хрупкий: каждая новая панель удваивает нагрузку и добавляет новые источники рассинхронизации словаря.

**Предложение:** вынести решение из README в код — либо довериться `PreviousContainer` (удалить словарь целиком), либо перенести восстановление позиции в `LayoutUpdateStrategy`. Текущее промежуточное состояние — самый дорогой из трёх вариантов.

---

## 4. Низкий приоритет

### 4.1. Автосохранение настроек на каждое `PropertyChanged`

`PersistenceService.SettingsPropertyChanged → SettingsService.Save()` перезаписывает `appsettings.json` на каждое движение свойства — включая каскад от смены темы (`ThemeService` меняет `ThemeName` → `PropertyChanged` → `Save`). Атомарная запись (`tmp` + `File.Move`) защищает целостность, но не количество записей на диск.

**Предложение:** тот же debounce, что уже реализован для layout (500 мс) — код почти готов к переиспользованию.

### 4.2. `MainViewModel.OpenFile`: регистрозависимое сравнение путей

```csharp
d.FilePath == path
```

На Windows пути регистронезависимы: один файл, открытый под разными регистрами, даст дубли вкладок.

**Предложение:** `string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)`.

### 4.3. Гвард звёздочки в `AvalonDocumentViewModel.Title`

`if (title.EndsWith(modifiedChar)) return title;` — документ с именем, естественно заканчивающимся на `*` (например, `report*`), никогда не покажет признак несохранённых изменений. Мелочь, но гвард лучше вести по `IsModified`, а не по эвристике в заголовке.

Также сеттер вызывает `OnPropertyChanged(nameof(Title))` после `SetProperty` — двойное уведомление, безвредно, но лишнее.

### 4.4. `SettingsView`: заблокированные поля

Уровни логов и путь к логам показываются в UI с `IsEnabled="False"` — менять можно только руками в `appsettings.json`. Если это сознательно (read-only просмотр) — стоит скрыть метки-описания, обещающие выбор значений (`«Verbose, Debug, Information…»`); если нет — TODO на ComboBox с `LogEventLevel`.

---

## 5. Косметика: темы

`Window.Background="#FFFFFF"` и фон меню `#F0F0F0` захардкожены. При выборе `VS2013Dark` док-панели потемнеют, а окно, меню и фон вкладок останутся светлыми — тема выглядит наполовину применённой.

**Предложение:** цвета окна/меню через `DynamicResource` или добавить их в `AppSettings`, как уже сделано со шрифтом редактора.

---

## 6. README отстаёт от кода

Заявлен актуальным на `15d4d28`, а `master` уже на `81f65fe`. Что устарело:

| README | Код |
|---|---|
| `Restore()` / `Save()` без параметров | `Restore(Window)` / `Save(Window)` — сохранение размеров и `IsMaximized` (`95a3a4b`, `d4435fd`) |
| `AppSettings.ShowLineNumbers` | свойства нет; добавлены `EditorFontFamily` / `EditorFontSize` (`e59fe3c`) |
| Диалог `ShowQuestion` | переименован в `ShowYesNo`, добавлен `ShowYesNoCancel` (`13cb28c`, `81f65fe`) |
| — (не описано) | Eager-resolve `SettingsViewModel` в `App.OnStartup` — фикс молча теряемого сообщения мессенджера |

**Рекомендация:** актуализировать README и завести привычку обновлять строку «актуально на коммит …» в том же коммите, где меняется код, — сейчас она живёт отдельно от кода.

---

## 7. Что сделано хорошо

Стоит зафиксировать, чтобы не потерять при рефакторинге:

- **Атомарная запись настроек** (`appsettings.json.tmp` + `File.Move(overwrite)`) — редкая дисциплина для такого размера проекта.
- **Двухфазная подписка в `DockEventBinder`** (`manager.LayoutChanged` → переподписка на `LayoutRoot.Updated`) — правильное решение известной ловушки AvalonDock.
- **Debounce layout-сохранения** с маршалингом в UI-поток — корректно для `DependencyProperty`.
- **Eager-resolve `SettingsViewModel`** в `OnStartup` — тонкая ловушка с `WeakReferenceMessenger` закрыта осознанно, с комментарием «почему».
- **`IAppPathService`** — все пути централизованы, fallback на `%Temp%` при недоступном `%AppData%`.
- **Первый запуск без исключений** (`IsFirstRun` вместо `FileNotFoundException`) — контракт, который приятно поддерживать.
- **Ошибки загрузки настроек не валят приложение** — дефолты плюс диагностика.
- **Отсутствие циклов зависимостей сервисов** — поток строго однонаправленный.
- **Комментарии с мотивацией** (`WindowLifecycleService` — почему это не `IHostedService`) — редкая и ценная привычка.

---

## 8. Порядок работ (предложение)

1. `App()` — глобальные обработчики исключений (п. 2.1) — 15 минут, закрывает самый большой риск.
2. Сузить catch в `WorkspaceService` / `LayoutSerializerService` (п. 2.2) — 10 минут.
3. Решить судьбу `existingPane` в `LayoutUpdateStrategy` (п. 3.2) — половина дня с тестом на реальном запуске.
4. Мелкие точечные правки пп. 3.1, 4.1–4.3 — по одному коммиту на пункт.
5. Актуализировать README (п. 6) в том же коммите с чисткой.

---

*Документ составлен по состоянию `master` на коммит `81f65fe` (26 Nov 2026). Пункты 2–5 проверены по коду репозитория; пункт 1.1 — по коммитам `13cb28c`, `81f65fe`.*
