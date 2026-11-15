# План: #140 F3.18 — файлы настроек разделены по назначению: `appsettings.json` и `ui-settings.json`

| Поле | Значение |
|---|---|
| Issue | [#140](https://github.com/askrinnik/GitHubBackup/issues/140) |
| Заголовок | F3.18: Settings files split by purpose: appsettings.json and ui-settings.json |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-15 |

## 1. Цель

Сейчас одна настройка может жить в двух файлах. Поставляемый `appsettings.json` правит пользователь, `user-settings.json` пишет UI, и второй файл молча переопределяет первый. Задача — разделить файлы по назначению:

- `appsettings.json` описывает, как работает программа. Его читают оба exe. Файл не поставляется. Его правят и пользователь, и редактор настроек, и эти правки равноправны.
- `ui-settings.json` хранит только состояние UI: тему, положение окна и высоты панелей. Его читает и пишет только `GitHubBackup.exe`.
- `user-settings.json` программа больше не использует.

Значения по умолчанию всех ключей задаются в коде. Требования: FR-12.5, FR-12.9, FR-12.10, §7.1, §7.2, NFR-8, а также NFR-2 (атомарная запись).

## 2. Критерии приёмки

Из issue:

1. PRD (§7.1, §7.2, FR-12.5, FR-12.9, FR-12.10, NFR-8) описывает новую раскладку; версия PRD повышена, строка в истории изменений добавлена.
2. Без `appsettings.json` обе программы запускаются со значениями по умолчанию; `validate` проходит.
3. Save в редакторе пишет пути к инструментам и телеметрию в `appsettings.json`, сохраняя остальные ключи и разделы; при отсутствии файла создаёт его.
4. Ручная правка `appsettings.json` действует после перезапуска и видна в редакторе.
5. Тема, положение окна и высоты панелей пишутся только в `ui-settings.json`; CLI этот файл не читает.
6. `user-settings.json` не читается и не пишется.
7. `scripts/publish.ps1` не кладёт `appsettings.json` в папку публикации.
8. README и `docs/development.md` описывают новую раскладку.
9. Видимые пункты проверены в запущенном приложении (netwright).
10. Покрыто тестами, CI зелёный.

Добавлено по итогам проверки покрытия:

- A1. Без файла поведение не меняется, в том числе `Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime = Warning` и `Tools:SevenZipPath`.
- A2. Если `appsettings.json` при старте не является корректным JSON или его корень не объект, запуск завершается с кодом `2`; сообщение называет файл.
- A3. Save ищет существующие ключи и разделы без учёта регистра: дубликатов не появляется.
- A4. Если `appsettings.json` при Save содержит некорректный JSON или не-объект, Save этих групп завершается ошибкой с именем файла; файл не меняется, редактор остаётся открытым.
- A5. Save пишет только 4 ключа; остальные ключи и разделы остаются со своими значениями.
- A6. Если `ui-settings.json` некорректен, UI берёт значения по умолчанию, старт не ломается. CLI файл не открывает.
- A7. `publish.ps1 -Clean` считает папку с `appsettings.json` чужой и ничего не удаляет.
- A8. Прежняя версия `appsettings.json` после Save остаётся в `appsettings.json.bak` (`AtomicFileWriter`).

## 3. Изменения требований (PRD 0.40)

- §7.1: новое дерево (`appsettings.json` необязательный, правят пользователь и редактор; `ui-settings.json` только UI; `.bak` обоих), абзац «публикация кладёт только два exe, обновление заменяет только exe».
- §7.2: файл необязателен и не поставляется, умолчания в коде; абзац «Кто пишет файл» (4 ключа, равноправие правок, регистр, создание, атомарность и `.bak`, комментарии не сохраняются, некорректный файл — ошибка Save); новый список источников: умолчания → `appsettings.json` → user-secrets (Development) → `GitHubBackup__` → командная строка; `ui-settings.json` и `user-settings.json` в конфигурацию не входят; абзац про `Serilog:MinimumLevel:Override` — умолчание в коде.
- Новый §7.4 `ui-settings.json`: корень `{Theme, MainWindow}`, таблица ключей без изменений, правила умолчаний и порчи файла.
- FR-12.5: пути и телеметрия пишутся в `appsettings.json`; ручные правки видны в редакторе; ошибка записи при некорректном файле.
- FR-12.9, FR-12.10: `ui-settings.json` (§7.4).
- NFR-2: атомарная запись файлов настроек. NFR-8: публикация содержит только два exe.
- Строка истории 0.40.

## 4. Решения

Приняты без отдельного ревью плана: пользователь поручил реализовать задачу до конца, если вопросов по планированию не осталось. Каждое решение выносится в описание pull request для ревью.

- **D1.** user-secrets остаются источником, только в Development, между `appsettings.json` и переменными окружения.
- **D2.** `src/GitHubBackup.Cli/appsettings.json` и элементы `<None … appsettings.json>` в csproj Cli и App удаляются. От умолчаний в коде файл отличается только `Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime = Warning`; это значение переходит в `SerilogOptions.Override`.
- **D3.** Save переписывает файл как JSON с отступами; ключи, значения и разделы сохраняются, комментарии и форматирование — нет. Ключи сравниваются без учёта регистра; при замене пишется каноническое имя (`GitPath`). Некорректный JSON или не-объект — ошибка Save с именем файла, файл не перезаписывается.
- **D4.** Некорректный `appsettings.json` при старте — ошибка конфигурации (`ConfigurationFileException`, код `2`). Отсутствие файла ошибкой не считается. Поведение пустого файла задаёт провайдер JSON .NET; оно фиксируется тестом и описывается в README, если это ошибка.
- **D5.** В `ui-settings.json` бывшее содержимое раздела `Ui` лежит в корне. Недопустимое значение даёт умолчание, некорректный JSON — все умолчания. Испорченный файл UI не перезаписывает, ошибка записи идёт в лог (как сегодня).
- **D6.** Чтение `user-settings.json` с allowlist удаляется: `appsettings.json` — файл пользователя. Редактор пишет только свои 4 ключа, это проверяет тест.
- **D7.** `publish.ps1` публикует только exe; `appsettings.json` убирается из списка `-Clean`, папка с ним считается чужой; проверка токена удаляется.
- **D8.** Если пользователь держит `GitHub:Token` в `appsettings.json`, после Save его копия есть и в `appsettings.json.bak` в той же папке. Это принимается и упоминается в README в разделе о токене.

## 5. Затронутые проекты и типы

**GitHubBackup.Infrastructure**

- `Hosting/GitHubBackupHostBuilder`: `AddJsonFile(..., optional: true)`; удалить `UserSettingsFileName` и `ReadUserSettings`; добавить `UiSettingsFileName = "ui-settings.json"`; доки.
- `Configuration/AppSettingsFile` (новый): атомарная частичная перезапись `appsettings.json` (`JsonDocument` → `Utf8JsonWriter` → `AtomicFileWriter`), gate `SemaphoreSlim`, ошибка `IOException` с именем файла.
- `Configuration/UiSettingsFile` (или логика в `JsonUiSettingsStore`): весь `ui-settings.json` с корнем `{Theme, MainWindow}`.
- `Configuration/JsonSettingsText` (по желанию): общие настройки чтения и записи JSON, BOM, пустой файл.
- `Configuration/UserSettingsFile`: удалить.
- `JsonToolPathsStore`, `JsonTelemetrySettingsStore`: пишут через `AppSettingsFile`; `Current` не меняется.
- `JsonUiSettingsStore`: `ui-settings.json`.
- `Logging/SerilogOptions`: умолчание `Override["Microsoft.Hosting.Lifetime"] = "Warning"`.
- `InfrastructureServiceCollectionExtensions`: регистрации.
- Доки `ConfigurationFileException`, `ConfigurationErrorMessages`, `GitHubBackupHostSettings`.

**GitHubBackup.Core:** без изменений.

**GitHubBackup.Cli, GitHubBackup.App:** удалить `appsettings.json` и элементы csproj, поправить комментарий csproj.

**scripts/publish.ps1:** справка, список `-Clean`, удалить проверку токена.

**Документы:** `docs/PRD.md`, `README.md`, `docs/development.md`.

## 6. Тесты

**GitHubBackup.Infrastructure.Tests**

- `AppSettingsFileTests`: нет файла → создаёт; пробельный файл; другие разделы и ключи сохраняются (сравнение текста целиком); ключ в другом регистре заменяется без дубликата; раздел дважды в разном регистре; раздел не объект; комментарии и висячие запятые; BOM; некорректный JSON → `IOException` с именем файла, файл не изменён; корень не объект; `.bak`.
- `JsonToolPathsStoreTests`, `JsonTelemetrySettingsStoreTests`: только свои ключи, `null` для пустого, `Current` до и после Save, валидация до записи, некорректный файл, сторы не трогают чужие разделы.
- `JsonUiSettingsStoreTests`: путь и корень; нет файла; некорректный JSON; недопустимые значения; регистр; запись; испорченный файл не перезаписывается; `appsettings.json` не трогается; `user-settings.json` игнорируется.
- `SerilogOptions`: умолчание `Warning`, переопределение, другой ключ `Override` не убирает умолчание.
- `ConfigurationErrorMessagesTests`: убрать сценарий «файл не найден».

**GitHubBackup.Infrastructure.IntegrationTests**

- `AppSettingsHostTests` вместо `UserSettingsHostTests`: без файла — умолчания и старт; некорректный JSON и не-объект — `ConfigurationFileException`; пустой файл — фиксация поведения; `user-settings.json` игнорируется; `ui-settings.json` вне конфигурации; порядок источников; Save → новый хост читает сохранённое; ручные ключи сохраняются; некорректный `ui-settings.json` не ломает старт; UI пишет только `ui-settings.json`.
- `OpenTelemetryHostTests`: включение через `appsettings.json`.

**GitHubBackup.Cli.Tests**

- `CliApplicationTests`: без файла — умолчания; битый JSON — код `2`.
- `CliInstallation`: параметр `writeAppSettings`.
- `CliEndToEndTests`: `validate` без файла; с некорректным `ui-settings.json`; с `user-settings.json` с неверным путём.
- `PublishedApplicationTests`: в папке только два exe; `-Clean` на папке с `appsettings.json` отказывает.
- `ShippedAppSettingsTests`: удалить. `UserSecretsSourceTests`: Development без файла читает user-secret.

**GitHubBackup.App.Tests:** `AppStartupTests` — без файла старт с умолчаниями, некорректный файл — диалог ошибки; `AppTestSettings` — доки.

**GitHubBackup.TestUtilities:** `BuildOutput` не копирует `appsettings.json`, `ui-settings.json` и `.bak`; `TemporaryContentRoot` — доки.

**GitHubBackup.App.UITests:** `AppInstallation` (`UiSettingsPath`, `AppSettingsPath`, `writeAppSettings`); `ThemeTests`, `MainWindowLayoutTests` — `ui-settings.json`, `appsettings.json` не меняется; `SettingsEditorTests` — Save пишет в `appsettings.json` с сохранением разделов, ручная правка видна, некорректный файл — ошибка и редактор открыт, старт без файла.

## 7. Проверка

| Критерий | Как проверяется |
|---|---|
| 1 | Дифф PRD: версия 0.40, строка истории, разделы |
| 2 | `AppSettingsHostTests`, `CliEndToEndTests.Validate_NoAppSettings_*`, `AppStartupTests`, netwright N1 |
| 3 | `AppSettingsFileTests`, тесты сторов, UI `SettingsEditorTests`, netwright N2 |
| 4 | `AppSettingsHostTests` (новый хост читает сохранённое), UI-тест ручной правки, netwright N5 |
| 5 | `JsonUiSettingsStoreTests`, `AppSettingsHostTests`, `CliEndToEndTests`, netwright N3, N4 |
| 6 | `AppSettingsHostTests`, `CliEndToEndTests`, grep `user-settings` в `src` пуст, netwright N6 |
| 7 | `PublishedApplicationTests` |
| 8 | Ревью README и `docs/development.md`, grep `user-settings` пуст |
| 9 | Netwright N1–N8 |
| 10 | Сборка, тесты, format, UI-тесты, CI |

**Netwright.** Вывод `GitHubBackup.App` копируется во временную папку без файлов настроек, `DOTNET_ENVIRONMENT=Production`.

- N1. Без `appsettings.json` приложение стартует, виден первый запуск, диалога ошибки нет; `#SevenZipPathBox` показывает умолчание.
- N2. Save с путём git и телеметрией создаёт `appsettings.json` только с `Tools` и `OpenTelemetry`.
- N3. Тема Dark пишется в `ui-settings.json`, `appsettings.json` не меняется.
- N4. После закрытия `ui-settings.json` содержит `MainWindow`, в `appsettings.json` нет ключей UI.
- N5. Ручная правка `tools:gitpath` видна после перезапуска; Save заменяет ключ без дубликата, `Serilog` сохраняется, есть `.bak`.
- N6. `user-settings.json` с путём и темой не влияет и не меняется.
- N7. Испорченный `appsettings.json` при старте — диалог ошибки с именем файла.
- N8. Испорченный файл при Save — ошибка, редактор открыт, файл не изменён.

## 8. Вне рамок

- Перенос значений из `user-settings.json`: старый файл игнорируется.
- Сохранение комментариев и форматирования `appsettings.json` при Save.
- Перенос регистрации `IUiSettingsStore` из Infrastructure в App.
- Удаление старых `user-settings.json` из установленных папок.

## 9. Задачи

- [x] PRD 0.40
- [x] `SerilogOptions.Override`: умолчание, тесты
- [x] `AppSettingsFile` и тесты
- [x] Сторы инструментов и телеметрии на `AppSettingsFile`
- [x] `ui-settings.json`: файл и стор, тесты
- [x] Удалить `UserSettingsFile`, регистрации
- [x] `GitHubBackupHostBuilder`: необязательный файл, без `user-settings.json`
- [x] Доки исключений и сообщений конфигурации
- [x] Удалить исходный `appsettings.json` и элементы csproj
- [x] `scripts/publish.ps1`
- [x] Тестовые утилиты и установки
- [x] Интеграционные, CLI, App и UI-тесты
- [x] README и `docs/development.md`
- [x] Grep `user-settings` пуст в production-коде `src`, в `scripts`, README и development.md (в тестах имя остаётся: они проверяют, что старый файл не читается и не пишется)
- [x] Сборка, тесты, format, UI-тесты
- [x] Netwright N1–N8
- [x] Проверка добавленных комментариев
