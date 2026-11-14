# План: F4.5: Settings and token editor

| | |
|---|---|
| Issue | [#40](https://github.com/askrinnik/GitHubBackup/issues/40) |
| Lane | Feature |
| Сложность | L |
| Дата | 2026-11-14 |

## 1. Цель

В UI-приложении появляется редактор настроек (FR-12.5). Он редактирует `backup-config.json`: `backupRoot`, `sourcesRoot`, `discoverAccessibleRepositories`, глобальные исключения, аккаунты (url, path, exclude, ветка и путь их репозиториев), отдельные репозитории (url, branch, path). Ещё он редактирует пути к git и 7-Zip, сохраняет токен в Credential Manager и проверяет токен: кто авторизован, scopes, лимит. Перед сохранением работает та же валидация, что при загрузке (FR-3.6–FR-3.8, §7.3). Проверку выполняет общий `IBackupConfigValidator`, логика в хосте не дублируется (FR-12.7). Запросы к API идут только через абстракцию над Octokit и только на настроенный `ApiBaseUrl` (FR-11.5). Цепочка токенов не меняется (FR-11.1).

## 2. Критерии приёмки

Из issue (дословно):

1. Невалидные настройки не сохраняются, ошибки показаны у полей
2. Unit-тесты ViewModel
3. Покрыто тестами, CI зелёный

Из PRD, в issue нет:

4. (добавлено, FR-12.5) Переключатель `discoverAccessibleRepositories` можно менять, и он сохраняется.
5. (добавлено, FR-3.8) Если клоны оказываются внутри дерева архивов, редактор показывает предупреждение. Сохранение оно не блокирует.
6. (добавлено, §7.3) При сохранении не теряются поля, которых в UI нет: `id` и `status` записей, `name` репозиториев аккаунта, `schemaVersion`. Файл пишется атомарно, рядом остаётся `.bak` (FR-2.8, NFR-2).

Добавлено при сверке с кодом:

7. Ошибка ввода-вывода при сохранении (`IOException`, `UnauthorizedAccessException`) показывается диалогом с замаскированным текстом. Поля остаются заполненными, файл не меняется.
8. Отмена или уход со страницы с несохранёнными изменениями сначала спрашивает подтверждение (`ICloseGuard`). Отмена ничего не пишет.
9. Пустой токен сохранить нельзя: ошибка показывается у поля, текст тот же, что у `set-token` (`The token cannot be empty.`). Сбой записи в хранилище (`TokenStoreException`) показывается у поля токена.
10. Проверка токена различает исходы:
    - успех: login, scopes или пометка, что scopes не сообщаются (fine-grained PAT), остаток и лимит запросов, время сброса;
    - анонимный режим: токена нет нигде, показывается только лимит;
    - неверный или истёкший токен (401);
    - другая ошибка API;
    - отмена (`CheckTokenCancelCommand`).
11. Проверяется токен, введённый в поле. Если поле пустое, проверяется действующий токен из `ITokenProvider`, и показывается его источник.
12. После сохранения токена редактор предупреждает, если источник с более высоким приоритетом (`GitHub:Token`) перекрывает Credential Manager (FR-11.1).
13. Значение токена не попадает в лог, в сообщения об ошибках и в файлы (S1, FR-10.6). После сохранения поле токена очищается.
14. Пока идёт запуск бэкапа, редактор открыть нельзя: кнопка Settings неактивна. Это защищает от потерянной записи, ведь оркестратор тоже пишет `backup-config.json`.

## 3. Изменения требований

**Противоречие.** §7.1 называет `appsettings.json` файлом «только чтение». FR-12.5 требует редактировать пути к git и 7-Zip, а они живут в `Tools:GitPath` и `Tools:SevenZipPath` внутри `appsettings.json`. Источника настроек, доступного на запись, в коде нет: `GitHubBackupHostBuilder.Create` читает только `appsettings.json`, user-secrets, переменные `GitHubBackup__` и командную строку.

Варианты:

| Вариант | Суть | Оценка |
|---|---|---|
| A | Пути к инструментам в `backup-config.json` | Смешивает «что бэкапить» с «чем», два источника одного ключа. Не рекомендуется. |
| **B** | Новый необязательный `user-settings.json` рядом с exe, его пишет только UI; хост читает его сразу после `appsettings.json` | **Рекомендуется.** CLI видит тот же файл через общий `GitHubBackupHostBuilder`. |
| C | UI переписывает секцию `Tools` в `appsettings.json` | Публикация затирает файл. Не рекомендуется. |
| D | Редактор только показывает действующие пути, редактирование — отдельный issue | Тоже требует правки FR-12.5. |

**Проект текста для варианта B** (версия 0.24):

- §7.1, в схему после строки `appsettings.json`:
  `├── user-settings.json        ← пути к инструментам из редактора настроек (необязательный, пишет UI)`
- §7.2, «Источники конфигурации»: после п. 1 новый п. 2, остальные сдвигаются:
  «2. `user-settings.json` рядом с exe — необязательный. Редактор настроек (FR-12.5) пишет в него только `Tools:GitPath` и `Tools:SevenZipPath`, атомарно (NFR-2). Значения проверяются по тем же правилам, что и в `appsettings.json`. Изменения вступают в силу при следующем запуске программы.»
- FR-12.5, первый пункт: «Корень бэкапов, корень клонов (`sourcesRoot`), аккаунты, …». Второй пункт: «Пути к git и 7-Zip (пишутся в `user-settings.json`, §7.2; действуют после перезапуска).»
- §14: `| 0.24 | <дата> | §7.1, §7.2: необязательный файл user-settings.json — источник путей к git и 7-Zip, который пишет редактор настроек; FR-12.5: корень клонов sourcesRoot и место хранения путей к инструментам |`

**`sourcesRoot`** включается в редактор: FR-3.4 разрешает задать его в `backup-config.json`, а предупреждение FR-3.8 касается именно его.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**

- `GitHub/IGitHubTokenInspector.cs` (новый): `Task<GitHubTokenInfo> InspectAsync(string? token, CancellationToken)`; при `null` проверяет анонимный доступ.
- `GitHub/GitHubTokenInfo.cs` (новый, record): `Login?`, `Scopes?` (`null` — сервер не прислал `X-OAuth-Scopes`), `RateLimit`.
- `GitHub/GitHubRateLimit.cs` (новый, record): `Limit`, `Remaining`, `ResetsAt`.
- `BackupConfiguration/IBackupConfigStore.cs`: новый метод `BackupConfigValidationResult Validate(BackupConfig config)` с clone-корнем по умолчанию.
- `Tools/ToolPaths.cs`, `Tools/IToolPathsStore.cs` (новые, вариант B): `Current`, `Validate`, `SaveAsync`.

**GitHubBackup.Infrastructure**

- `GitHub/OctokitGitHubTokenInspector.cs` (новый): свой `Connection` на `ApiBaseUrl` с переданным токеном; с токеном `GET /user` и `ApiInfo.OauthScopes`/`RateLimit`; без токена `GET /rate_limit`; ошибки через `OctokitExceptionMapper`; токен регистрируется в `ISecretMasker`.
- `GitHub/OctokitConnectionFactory.cs` (новый): общий `CreateBaseAddress` и `ProductVersion` для `OctokitGitHubApi` и инспектора.
- `BackupConfiguration/JsonBackupConfigStore.cs`: `Validate`; `SaveAsync` вызывает его.
- `Configuration/JsonToolPathsStore.cs` (новый, B): пишет `{"Tools":{…}}` в `user-settings.json` через `AtomicFileWriter`.
- `Configuration/ToolsOptionsValidator.cs`: общая проверка абсолютного пути.
- `Hosting/GitHubBackupHostBuilder.cs` (B): `user-settings.json` как необязательный источник после `appsettings.json`.
- `InfrastructureServiceCollectionExtensions.cs`: регистрации.

**GitHubBackup.App**

- `ViewModels/SettingsViewModel.cs` (новый, `ObservableValidator`, `ICloseGuard`): загрузка, поля, коллекции строк, `SaveCommand`, `CancelCommand`, `SaveTokenCommand`, `CheckTokenCommand` (с отменой), `SaveToolsCommand`, `GeneralErrors`, `Warnings`, `IsDirty`.
- `ViewModels/AccountSettingsViewModel.cs`, `AccountRepositorySettingsViewModel.cs`, `RepositorySettingsViewModel.cs` (новые): поля строки с location-ключом; строки хранят исходные `id`, `status`, `name`.
- `ViewModels/SettingsErrorMapper.cs` (новый): раскладывает сообщения валидатора по полям.
- `ViewModels/RepositoriesViewModel.cs`: `OpenSettingsCommand` (CanExecute: `!IsRunning`).
- `Views/SettingsView.xaml(.cs)`, `Behaviors/PasswordBoxBinding.cs` (новые), кнопка в `Views/RepositoriesView.xaml`, `DataTemplate` в `App.xaml`, регистрация в `Hosting/AppServiceCollectionExtensions.cs`.

**Документы:** `docs/PRD.md`, `README.md`, этот план.

## 5. Подход

1. PRD 0.24 после согласования.
2. Core: контракты.
3. Infrastructure: инспектор, `Validate`, `JsonToolPathsStore`, источник в host builder.
4. App: ViewModel-и, view, навигация.

Ключевые решения:

- **Ошибки у полей.** Каждое сообщение валидатора начинается со своего location (контракт `IBackupConfigValidator`). `SettingsErrorMapper` берёт самый длинный location-префикс, за которым идёт пробел или конец. Ошибка уровня записи попадает на поле `path` этой записи; нераспознанное — в `GeneralErrors`. Тест в Core закрепляет формат.
- **Порядок сохранения.** `store.Validate(config)`: есть ошибки — показать у полей, `SaveAsync` не вызывать; нет — показать предупреждения FR-3.8 и сохранить. `IOException`/`UnauthorizedAccessException` — диалог с маскированием.
- **Редактирование.** Правка поля очищает его ошибку; полная проверка при Save; мгновенные подсказки через `BackupConfigValidator.IsAbsolutePath` и `IsValidBranch`.
- **Модель.** VM правит глубокую копию `BackupConfig`; новые записи получают `id = 0` и `status = Active`.
- **Навигация.** Редактор — страница главного окна (`INavigationService`); после Save или Cancel возврат на `RepositoriesViewModel`.
- **Токен.** Сохранение через `ITokenStore`, как в `SetTokenCommandHandler`; проверка через отдельный инспектор, не через кэширующий `OctokitGitHubApi`.
- **Пути к инструментам (B).** Показываются действующие значения; сохранение пишет `user-settings.json`; подсказка «Takes effect after restart».

## 6. Тесты

- **Core.Tests:** `BackupConfigValidatorTests.Validate_AnyError_MessageStartsWithLocation`.
- **Infrastructure.Tests:** `OctokitGitHubTokenInspectorTests` (WireMock: classic token, нет scopes, анонимно, 401, 5xx, токен только на настроенный API, регистрация в маскере); `JsonBackupConfigStoreTests.Validate_*`; `JsonToolPathsStoreTests`; тесты host builder на `user-settings.json`; резолв из DI.
- **App.Tests:** `SettingsViewModelTests` (загрузка, сохранение, FR-3.6/3.7/3.8, ошибки I/O, отмена, строки, токен, проверка токена, инструменты); `SettingsErrorMapperTests`; `RepositoriesViewModelTests.OpenSettings_*`; резолв `SettingsViewModel`.

## 7. Проверка

| Пункт | Как |
|---|---|
| 1, 5, 6, 7 | `SettingsViewModelTests.Save_*`, `JsonBackupConfigStoreTests.Validate_*` |
| 2 | `SettingsViewModelTests`, `SettingsErrorMapperTests` |
| 3 | build, тесты без smoke, `dotnet format`, CI |
| 4 | `Save_ToggledDiscoverAccessible_SavesValue` |
| 8 | `Cancel_*`, `CanCloseAsync_Dirty_*` |
| 9–13 | `SaveToken_*`, `CheckToken_*`, `OctokitGitHubTokenInspectorTests` |
| 14 | `OpenSettings_Running_CannotExecute` |

## 8. Вне рамок

- Проверка существования файлов git и 7-Zip в редакторе (это делает `IToolLocator` при запуске).
- Защита от одновременной правки `backup-config.json` из CLI во время открытого редактора.
- Удаление токена из Credential Manager.

## 9. Открытые вопросы

1. Пути к инструментам: вариант B или D?
2. Кэш токена в `OctokitGitHubApi`: подсказка «перезапустите приложение» и отдельный bug, или чинить здесь?
3. Ручное добавление репозиториев в аккаунт в редакторе?
4. Редактор как страница главного окна, а не модальное окно?
5. Предупреждение FR-3.8: баннер без вопроса или подтверждение перед записью?

## Решения

Ревью 2026-11-14:

1. Пути к инструментам — вариант B: `user-settings.json`, правка PRD 0.24 по тексту раздела 3.
2. Кэш токена в `OctokitGitHubApi` в этом issue не меняется. После сохранения токена редактор показывает подсказку «Restart the application to use the new token»; сброс кэша — отдельный bug.
3. Репозитории в аккаунт вручную не добавляются: редактор меняет ветку и путь и удаляет записи. Отдельные репозитории добавлять можно.
4. Редактор — страница главного окна через `INavigationService`; предупреждение FR-3.8 — баннер, сохранение не блокируется.

После security review:

5. Из `user-settings.json` читаются только `Tools:GitPath` и `Tools:SevenZipPath`; другие ключи игнорируются, чтобы файл не мог перенаправить токен (уточнение §7.2 в той же версии PRD 0.24).
6. Проверка токена создаёт свой `HttpClientAdapter` и освобождает его после запроса, чтобы повторные проверки не оставляли открытые соединения.

## Задачи

- [x] Согласовать вопросы и внести правку PRD 0.24
- [x] Core: `GitHubTokenInfo`, `GitHubRateLimit`, `IGitHubTokenInspector`
- [x] Core: `IBackupConfigStore.Validate`; тест формата location
- [x] Core: `ToolPaths`, `IToolPathsStore` (B)
- [x] Infrastructure: `OctokitConnectionFactory`, `OctokitGitHubTokenInspector` и тесты
- [x] Infrastructure: `JsonBackupConfigStore.Validate` и тесты
- [x] Infrastructure: `JsonToolPathsStore`, источник `user-settings.json`, тесты (B)
- [x] Infrastructure: регистрации в DI
- [x] App: `SettingsErrorMapper`, строки, `SettingsViewModel` и тесты
- [x] App: `PasswordBoxBinding`, `SettingsView.xaml`, навигация, кнопка Settings, регистрация
- [x] README
- [x] Сборка, тесты, `dotnet format`, проверка комментариев
