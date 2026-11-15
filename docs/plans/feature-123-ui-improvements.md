# План: #123 «F4.11: UI improvements»

| Поле | Значение |
|---|---|
| Issue | [#123](https://github.com/askrinnik/GitHubBackup/issues/123) |
| Направление | Feature |
| Сложность | L |
| Дата | 2026-11-14 |

## 1. Цель

Улучшить главное окно и редактор настроек по PRD 0.29. Требования: FR-12.1 (выровненные колонки дерева, GridSplitter между деревом, лентой лога и сводкой), FR-12.4 (сводка ограничена по высоте и прокручивается), FR-12.5 (четыре группы настроек, тема в «General»), FR-12.8 (иконка), FR-12.9 (тема Fluent: System/Light/Dark, применяется сразу), FR-12.10 (положение, размер, развёрнутость окна и высоты панелей), §7.1/§7.2 (раздел `Ui` в `user-settings.json`: запись атомарная, недопустимое значение заменяется значением по умолчанию, CLI раздел игнорирует, другие ключи не проходят в конфигурацию).

## 2. Критерии приёмки

Пункты issue:

1. Длинный список обнаруженных репозиториев в сводке прокручивается и не вытесняет дерево и ленту лога.
2. Разделители между деревом и лентой лога и между лентой лога и сводкой меняют высоту панелей мышью.
3. Имя, статус и дата последнего архива в дереве стоят в выровненных колонках при любой длине имени. Заголовков колонок нет.
4. Редактор настроек показывает четыре группы с заголовками: General, Accounts and repositories, Tools, GitHub token.
5. Оба exe, окна UI и панель задач показывают иконку приложения (круговые стрелки вокруг архива), один `.ico` с размерами 16..256 px; источник в репозитории, рендер воспроизводим, без загрузок из сети.
6. Список «Theme» (System по умолчанию, Light, Dark) переключает тему Fluent сразу во всех окнах; ViewModel не знают типов WPF; выбор сохраняется между запусками.
7. Положение и размер главного окна, развёрнутое состояние и высоты панелей восстанавливаются при следующем запуске; положение вне мониторов заменяется положением по умолчанию.
8. `user-settings.json` пишется атомарно (NFR-2); недопустимое значение раздела `Ui` заменяется значением по умолчанию; CLI раздел `Ui` игнорирует; существующий механизм записи используется повторно; раздел `Ui` не пропускает другие ключи.
9. Тесты: ViewModel (тема, сохранение и восстановление положения), чтение и запись раздела `Ui`, UI-тест FlaUI (группы настроек, разделители, переключение темы).

Добавлено по проверке покрытия:

- (добавлено) Сохранение путей к инструментам оставляет раздел `Ui` в файле. Запись `Ui` оставляет раздел `Tools` как был. Отсутствующий ключ `Tools` не превращается в `null`: `null` в `user-settings.json` перекрыл бы `SevenZipPath` из `appsettings.json`.
- (добавлено) Если файла нет, он пустой, в нём нет раздела `Ui`, `Ui` не объект или JSON повреждён после старта, UI берёт значения по умолчанию и запускается. Повреждённый JSON при старте остаётся ошибкой конфигурации, как сейчас.
- (добавлено) Если окно закрыто свёрнутым, сохраняются его `RestoreBounds`, и при запуске оно открывается в обычном состоянии. Развёрнутое окно сохраняет восстановленные границы и флаг `IsMaximized`.
- (добавлено) Ошибка записи при закрытии записывается в лог и не мешает выходу из приложения.
- (добавлено) Список «Theme» доступен, даже если `backup-config.json` не читается или ещё не создан. Смена темы не считается несохранённым изменением редактора: Cancel и Save на неё не влияют.
- (добавлено) Тема применяется на уровне `Application`. Окно обнаружения получает ту же тему, в том числе если оно открыто позже.
- (добавлено) Фиксированные цвета (`LightYellow` у предупреждений редактора, `SystemColors.WindowBrushKey` у `TreeLoadError`) читаются в тёмной теме.

## 3. Изменения требований

PRD называет раздел `Ui`, но не задаёт его ключи и допустимые значения. Предлагаемое дополнение §7.2 (версия 0.30, строка в истории изменений):

```json
"Ui": {
  "Theme": "System",
  "MainWindow": { "Left": 100, "Top": 100, "Width": 1000, "Height": 700, "IsMaximized": false,
                  "LogPanelHeight": 160, "SummaryPanelHeight": 220 }
}
```

Правила: `Theme` принимает `System`, `Light` или `Dark` без учёта регистра, иначе `System`. Размеры окна конечны и лежат в пределах min..max, иначе берётся положение по умолчанию. Высоты панелей проверяются по отдельности; недопустимая заменяется значением по умолчанию.

## 4. Затронутые проекты и типы

**GitHubBackup.Core**

- `Ui/UiTheme` (новый): enum `System`, `Light`, `Dark`.
- `Ui/WindowPlacement` (новый): record `Left`, `Top`, `Width`, `Height`, `IsMaximized` и `IsValid()`.
- `Ui/UiSettings` (новый): record `Theme`, `WindowPlacement? MainWindow`, `LogPanelHeight`, `SummaryPanelHeight`; `Default` и `Sanitize()`; константы min, max и default.
- `Ui/IUiSettingsStore` (новый): `LoadAsync(ct)` и `SaveAsync(UiSettings, ct)`, по образцу `IToolPathsStore`.

**GitHubBackup.Infrastructure**

- `Configuration/UserSettingsFile` (новый, internal): читает и пишет `user-settings.json` через `IFileSystem`, `JsonDocument` и `AtomicFileWriter`. Знает только `Tools:GitPath`, `Tools:SevenZipPath` и раздел `Ui`; помнит, какие ключи `Tools` есть в файле. `SemaphoreSlim` выстраивает чтение-изменение-запись по очереди. Нечитаемый или повреждённый файл при чтении даёт значения по умолчанию, а запись в него отклоняется с исключением, и файл с его `.bak` остаётся нетронутым (находка ревью безопасности); пустой файл считается отсутствующим. При копировании чужого раздела переносятся только известные ключи.
- `Configuration/JsonToolPathsStore` (изменён): `SaveAsync` пишет через `UserSettingsFile` и сохраняет `Ui`.
- `Configuration/JsonUiSettingsStore` (новый, internal): читает свойства по отдельности через `TryGet`, затем `Sanitize()`.
- `InfrastructureServiceCollectionExtensions` (изменён): регистрация `UserSettingsFile` и `IUiSettingsStore` (singleton).
- `Hosting/GitHubBackupHostBuilder.ReadUserSettings`: код не меняется — белый список оставляет `Ui:*` вне `IConfiguration`, поэтому CLI его не видит; правится XML-doc.

**GitHubBackup.App**

- `Shell/IThemeService`, `Shell/WpfThemeService` (новые): `Apply(UiTheme)` задаёт `Application.Current.ThemeMode`; точечное подавление `WPF0001` с комментарием.
- `Shell/IMonitorLayout`, `Shell/Win32MonitorLayout` (новые): `IsOnAnyMonitor(WindowPlacement)` через `MonitorFromRect(..., MONITOR_DEFAULTTONULL)` (`[LibraryImport]`).
- `ViewModels/UiPreferences` (новый, singleton): `Theme`, `LogPanelHeight`, `SummaryPanelHeight`, `MainWindowPlacement`; `LoadAsync`, `SaveAsync` (ошибка записи — в лог).
- `ViewModels/ThemeOption` (новый): значение и подпись.
- `MainWindowViewModel`, `RepositoriesViewModel`, `SettingsViewModel` (изменены): placement, `Layout`, `ThemeOptions`/`SelectedTheme`.
- Конвертеры высот строк (`GridLength`).
- `Views/RepositoriesView.xaml`: `Grid` из пяти строк со splitter-ами, сводка в `ScrollViewer`, `RunSummaryDiscovery` с `MaxHeight="160"`, узел репозитория — `Grid` с `SharedSizeGroup`. Новые AutomationId: `TreeLogSplitter`, `LogSummarySplitter`, `RepositoryNameText`, `RepositoryStatusText`, `RepositoryLastArchiveText`.
- `Views/SettingsView.xaml`: четыре `GroupBox` (`SettingsGeneralGroup`, `SettingsAccountsGroup`, `SettingsToolsGroup`, `SettingsTokenGroup`) и `ThemeComboBox`.
- `Views/MainWindow.xaml(.cs)`: `Icon`, применение placement до `Show`, `RecordPlacement` в `Closing`.
- `Views/DiscoveryReviewWindow.xaml`: `Icon`.
- `App.xaml`, `App.xaml.cs`, `Hosting/AppServiceCollectionExtensions`, `GitHubBackup.App.csproj` (`ApplicationIcon`, ресурс иконки).

**GitHubBackup.Cli**

- `GitHubBackup.Cli.csproj`: `ApplicationIcon`.

**Иконка и документация**

- `assets/icon/GitHubBackup.Icon.xaml` — векторный источник (WPF `DrawingGroup`).
- `assets/icon/GitHubBackup.ico` — сгенерирован и закоммичен.
- `scripts/render-icon.ps1` — рендер XAML в PNG-кадры 16..256 через WPF и сборка ICO.
- `CLAUDE.md` и `docs/development.md` (раскладка, команда рендера иконки), `README.md` (руководство пользователя на английском), `docs/ai/README.md` (при необходимости), `docs/PRD.md` (§7.2).

## 5. Подход

1. Core: типы `Ui` и unit-тесты.
2. Infrastructure: `UserSettingsFile`, перевод `JsonToolPathsStore` на него без изменения вывода для файла без `Ui`, затем `JsonUiSettingsStore`. Отдельный store читает файл напрямую, без `IConfiguration`: `Ui` не попадает в конфигурацию хоста, CLI его не видит, недопустимые значения не валят `ValidateOnStart`.
3. App: сервисы, `UiPreferences`, ViewModel. Высоты панелей живут в singleton `UiPreferences`, потому что `RepositoriesViewModel` transient.
4. XAML: локальные TwoWay-привязки высот строк. Запасной вариант — `DragCompleted` в code-behind копирует `ActualHeight` (чисто визуальная синхронизация).
5. Тема: `Application.ThemeMode`; если `WPF0001` в .NET 10 остаётся, подавить только в `WpfThemeService.Apply`.
6. Иконка рисуется в XAML и рендерится средствами WPF; `.ico` коммитится, сборка от скрипта не зависит.
7. Документация обновляется в том же изменении.

## 6. Тесты

- Core.Tests: `UiSettingsTests` (`Sanitize`, границы), `WindowPlacementTests`.
- Infrastructure.Tests: `JsonUiSettingsStoreTests` (нет файла, пустой, повреждённый, нет `Ui`, неверные типы и значения, точный JSON записи, сохранение `Tools`, `.bak`, отмена); дополнение `JsonToolPathsStoreTests` (сохраняет `Ui`).
- Infrastructure.IntegrationTests: `UserSettingsHostTests` (`Ui` вне `IConfiguration`, хост стартует с недопустимым `Ui`, полный цикл).
- App.Tests: `UiPreferencesTests`, дополнения `MainWindowViewModelTests`, `SettingsViewModelTests`, `RepositoriesViewModelTests`.
- App.UITests: `SettingsEditorTests` (группы), `ThemeTests` (тёмная тема и её сохранение), `MainWindowLayoutTests` (splitter, восстановление, окно вне экрана, развёрнутое окно, колонки дерева).

## 7. Проверка

| Пункт | Как |
|---|---|
| 1 | Netwright: сводка с длинным списком обнаруженных; XAML-ревью `MaxHeight` и `ScrollViewer` |
| 2 | `MainWindowLayoutTests` (splitter), netwright |
| 3 | `MainWindowLayoutTests` (колонки), netwright-скриншот |
| 4 | `SettingsEditorTests` |
| 5 | Иконки exe после `pwsh -File scripts/publish.ps1`, заголовок окна и панель задач на скриншоте; кадры `.ico` |
| 6 | `SettingsViewModelTests`, `UiPreferencesTests`, `ThemeTests` |
| 7 | `MainWindowViewModelTests`, `MainWindowLayoutTests` |
| 8 | `JsonUiSettingsStoreTests`, `JsonToolPathsStoreTests`, `UserSettingsHostTests` |
| 9 | Сами тесты |

## 8. Вне рамок

- Положение и размер других окон не сохраняются.
- Заголовки колонок дерева не нужны.
- Ширина колонок дерева при виртуализации растёт по показанным строкам.
- Свой дизайн тем поверх Fluent не делается.
- Иконка не рендерится в CI.

## 9. Открытые вопросы

1. Ключи и пределы раздела `Ui` (п. 3): ширина 400..10000, высота 300..10000, лента лога 60..2000 (по умолчанию 160), сводка 80..2000 (по умолчанию 220).
2. Тема сохраняется при закрытии окна вместе с положением или сразу при выборе?
3. Окно вне экрана, но `IsMaximized: true`: развёрнутым на основном мониторе или в обычном положении по умолчанию?
4. Источник иконки в XAML с рендером через WPF вместо SVG.
5. ICO с PNG-кадрами всех размеров; DIB-кадры для малых размеров — если 16 px выглядит плохо.
6. Проверка «вне мониторов» с DPI системы: возможна небольшая неточность у края экрана при смешанном DPI.

## 10. Решения

Ревью плана 2026-11-14, пользователь утвердил рекомендации по всем открытым вопросам:

1. Ключи и пределы раздела `Ui` приняты как в п. 3 и п. 9.1. PRD §7.2 дополнен, версия 0.30.
2. Тема записывается в `user-settings.json` сразу при выборе. Положение окна и высоты панелей записываются при закрытии главного окна.
3. Окно вне мониторов с `IsMaximized: true` открывается развёрнутым на основном мониторе.
4. Источник иконки — XAML (`DrawingGroup`), рендер средствами WPF; SVG не используется.
5. ICO из PNG-кадров всех размеров; DIB-кадры для малых размеров — только если значок 16 px выглядит плохо.
6. Небольшая неточность проверки «вне мониторов» у края экрана при смешанном DPI допустима.

## 11. Задачи

- [x] PRD §7.2: ключи и правила раздела `Ui` (версия 0.30)
- [x] Core: `UiTheme`, `WindowPlacement`, `UiSettings`, `IUiSettingsStore` и тесты
- [x] Infrastructure: `UserSettingsFile`
- [x] Infrastructure: перевести `JsonToolPathsStore` на `UserSettingsFile`, тесты на сохранение `Ui`
- [x] Infrastructure: `JsonUiSettingsStore` и регистрация, тесты
- [x] IntegrationTests: `Ui` вне `IConfiguration`, старт с недопустимым `Ui`, полный цикл
- [x] App: `IThemeService`/`WpfThemeService`, `IMonitorLayout`/`Win32MonitorLayout`
- [x] App: `UiPreferences`, `ThemeOption`, изменения ViewModel и тесты
- [x] App: `App.xaml.cs` — тема до окна, сохранение при закрытии
- [x] App: `MainWindow` — placement до `Show`, `RecordPlacement` в `Closing`
- [x] App: `RepositoriesView.xaml` — splitter-ы, высоты, прокрутка сводки, колонки дерева
- [x] App: `SettingsView.xaml` — четыре группы, `ThemeComboBox`, цвета для тёмной темы
- [x] Иконка: XAML-источник, `scripts/render-icon.ps1`, `.ico`, `ApplicationIcon` в App и Cli, `Icon` у окон
- [x] UITests: группы, тема, splitter и восстановление, окно вне экрана, развёрнутое окно, колонки
- [x] Проверка в netwright
- [x] Документация: `README.md` (руководство пользователя, на английском: тема, положение окна), `CLAUDE.md` и `docs/development.md` (раскладка `assets/`, команда рендера иконки), `docs/ai/README.md` при необходимости
- [x] Гейт: build, test, UI tests, format
