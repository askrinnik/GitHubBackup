# План: F4.1 — каркас WPF-приложения: Generic Host, DI, MVVM

| | |
|---|---|
| Issue | [#36](https://github.com/askrinnik/GitHubBackup/issues/36) — F4.1: WPF app skeleton: Generic Host, DI, MVVM |
| Режим | Feature |
| Сложность | L |
| Дата | 2026-11-13 |

Требования: [§6.12](../PRD.md#612-ui-приложение-githubbackupexe-wpf--mvvm) (FR-12.7), [§8.3](../PRD.md#83-технологии-и-пакеты).

## 1. Цель

`GitHubBackup.App` запускается на том же Generic Host, что и CLI. Хост строит `GitHubBackupHostBuilder.Create`, поэтому сервисы Core и Infrastructure, `IOptions` + `ValidateOnStart` и Serilog регистрируются тем же кодом (FR-12.7, §8.3). Приложение получает MVVM-каркас на `CommunityToolkit.Mvvm`: ViewModel создаются через DI, есть навигация между представлениями и сервис диалогов. Логи пишутся в те же файлы `logs\githubbackup-yyyyMMdd.{log,json}` (§7.1). Реальные экраны FR-12.1..12.6 в эту задачу не входят. Вместо них будет одна заглушка.

## 2. Критерии приёмки

1. Приложение запускается, ViewModel создаются через DI.
2. Логика из Core не дублируется.
3. Есть тесты, CI зелёный.
4. Логи App попадают в те же файлы `logs\`, что и логи CLI (из текста задачи).
5. Есть навигация между представлениями и сервис диалогов. Их абстракции тестируются без окон WPF (из текста задачи).
6. *(добавлено)* Хост WPF не использует `ConsoleLifetime`. Хост запускается в `Application.Startup` и останавливается при закрытии главного окна, без блокировки UI-потока.
7. *(добавлено)* Ошибка конфигурации при сборке или старте хоста не роняет процесс. Тексты ошибок берутся из `ConfigurationErrorMessages.TryGet`, пользователь видит их через сервис диалогов, процесс завершается с `ExitCode.Critical`. Если хост уже собран (ошибка в `StartAsync`), ошибка также пишется в лог.
8. *(добавлено)* В лог пишутся старт и остановка App: версия и окружение, по образцу `CliLog`.
9. *(добавлено)* Архитектурный тест: типы `GitHubBackup.App.ViewModels` не зависят от UI-типов WPF.

## 3. Затрагиваемые проекты и типы

**src/Directory.Packages.props**: новый `<PackageVersion Include="CommunityToolkit.Mvvm" …/>` (последняя стабильная 8.4.x) в новой группе `Label="UI"`.

**GitHubBackup.App**

- `GitHubBackup.App.csproj`: `PackageReference CommunityToolkit.Mvvm`. `appsettings.json` подключается ссылкой на `..\GitHubBackup.Cli\appsettings.json` (`Link`, `CopyToOutputDirectory=PreserveNewest`): файл один, копии нет. `<UserSecretsId>GitHubBackup</UserSecretsId>` с комментарием, как в Cli.
- `App.xaml`: без `StartupUri`, `ShutdownMode="OnExplicitShutdown"`. В ресурсах: `DataTemplate DataType=HomeViewModel` → `HomeView`.
- `App.xaml.cs`: `async void OnStartup`. Создаёт `AppStartup` с `MessageBoxDialogService` и вызывает `StartAsync(GitHubBackupHostSettings.ForCurrentProcess())`. Если вернулся `null`, вызывает `Shutdown((int)ExitCode.Critical)`. Иначе получает `MainWindow` из DI и показывает его. `Closed` → `await AppStartup.StopAsync(host)` → `Shutdown(0)`.
- `Hosting/AppHost.cs` (internal static): `BuildHost(GitHubBackupHostSettings)` = `GitHubBackupHostBuilder.Create(settings)` + `AddGitHubBackupApp()` + `Build()`. Аналог `CliApplication.BuildHost`.
- `Hosting/AppServiceCollectionExtensions.cs`: блок C# 14 `extension(IServiceCollection)` с методом `AddGitHubBackupApp()`. Регистрирует `IHostLifetime` → `DesktopHostLifetime`, `INavigationService`, `IDialogService` → `MessageBoxDialogService`, `MainWindowViewModel` и `MainWindow` как singleton, `HomeViewModel` как transient.
- `Hosting/DesktopHostLifetime.cs`: `IHostLifetime`, оба метода сразу завершаются. Нет Ctrl+C и сообщений «Press Ctrl+C». Жизнью процесса управляет WPF.
- `Hosting/AppStartup.cs` (internal sealed, без типов WPF): `Task<IHost?> StartAsync(settings, ct)` и `Task StopAsync(IHost, ct)`. Ловит ошибки конфигурации на `Build` и на `StartAsync`, как `CliApplication.RunAsync`. Показывает их через `IDialogService.ShowError`, после ошибки `StartAsync` пишет их в лог и освобождает хост. После старта пишет `AppLog.Started`, при остановке `AppLog.Stopped`.
- `Hosting/AppLog.cs`: `[LoggerMessage]` для `Started` (Id 1), `Stopped` (Id 2) и `ConfigurationInvalid` (Id 3, Error, `{Errors}`).
- `Navigation/INavigationService.cs` и `NavigationService.cs`: `CurrentViewModel`, событие `CurrentViewModelChanged`, `NavigateTo<TViewModel>() where TViewModel : ObservableObject`. ViewModel берётся из `IServiceProvider`. Это фабрика корня композиции, а не service locator во ViewModel; XML-док это объясняет.
- `Dialogs/IDialogService.cs`: `ShowError(title, message)`, `ShowInformation(title, message)`, `bool Confirm(title, message)`.
- `Dialogs/MessageBoxDialogService.cs`: реализация через `MessageBox`. Владелец окна — `Application.Current?.MainWindow`, если оно есть.
- `ViewModels/MainWindowViewModel.cs` (`partial`, `ObservableObject`): поле `[ObservableProperty] _currentViewModel`, которое синхронизируется с событием навигации. Конструктор переходит на `HomeViewModel`.
- `ViewModels/HomeViewModel.cs`: заглушка с одним свойством-текстом.
- `Views/MainWindow.xaml(.cs)`: перенос из корня. Конструктор принимает `MainWindowViewModel` и ставит его в `DataContext`. `ContentControl Content="{Binding CurrentViewModel, Mode=OneWay}"` с `AutomationProperties.AutomationId`.
- `Views/HomeView.xaml(.cs)`: `UserControl`-заглушка.

**GitHubBackup.App.Tests**: ссылки на `GitHubBackup.TestUtilities` и `Microsoft.Extensions.Diagnostics.Testing`, новые тестовые классы (см. §5).

**GitHubBackup.ArchitectureTests**

- `LayerRules`: новые `AppViewModelsNamespace` и `ViewModelForbiddenWpfTypes`: `System.Windows.Controls`, `System.Windows.Threading`, `System.Windows.Media`, `System.Windows.Window`, `System.Windows.MessageBox`, `System.Windows.Application`, `System.Windows.Visibility`. Это не весь `System.Windows`: `ICommand` из `RelayCommand` лежит в `System.Windows.Input`.
- Новый класс `AppViewModelDependencyTests`.
- Новая фикстура `Fixtures/WpfDependentViewModelFixture` и кейс в `RuleDetectionTests`.

## 4. Подход

1. Пакет: добавить `CommunityToolkit.Mvvm` в `Directory.Packages.props` и проверить `dotnet list package --vulnerable` (правило D1).
2. Абстракции UI без WPF: `IDialogService`, `INavigationService` и `NavigationService`, затем ViewModel.
3. Хостинг: `DesktopHostLifetime`, `AppServiceCollectionExtensions`, `AppHost`, `AppLog`, `AppStartup`. Порядок try/catch повторяет `CliApplication.RunAsync`: отдельно `Build` и отдельно `StartAsync`, фильтр `ConfigurationErrorMessages.TryGet`. Другие исключения не перехватываются, как и в CLI.
4. WPF-слой: `MessageBoxDialogService`, Views, `App.xaml` и `App.xaml.cs`.
5. Тесты и архитектурное правило.

Ключевые решения:

- **Один построитель хоста.** App не регистрирует сервисы Core и Infrastructure сам. Он вызывает `GitHubBackupHostBuilder.Create` и добавляет только свои UI-сервисы (FR-12.7, критерий 2). Поэтому логи идут в `LogFiles` под `ContentRootPath` (папка exe). Файловый sink уже `shared: true`, так что App и CLI могут писать в один файл одновременно (критерий 4).
- **ConsoleLifetime.** `HostApplicationBuilder` регистрирует его даже при `DisableDefaults`. Поэтому `AddGitHubBackupApp` заменяет `IHostLifetime` на `DesktopHostLifetime`.
- **Без блокировки.** `OnExit` синхронный, а `.Wait()` запрещён. Поэтому включён `ShutdownMode=OnExplicitShutdown`: хост останавливается асинхронно в `Closed` главного окна, затем вызывается `Shutdown`.
- **Диалог ошибки без DI.** Если хост не собрался, контейнера ещё нет. Поэтому `App.xaml.cs` создаёт `MessageBoxDialogService` напрямую и передаёт его в `AppStartup`. Тесты передают NSubstitute.
- **Тексты.** Сообщения на английском (NFR-7): заголовок «GitHub Backup», первая строка «Configuration error:», затем по строке на каждую ошибку.
- **Настройки.** `appsettings.json` берётся ссылкой на файл Cli. Общую папку публикации сделает задача NFR-8.

## 5. Тесты

**GitHubBackup.App.Tests**. Окна не создаются. Хост проверяется на временной папке через `TemporaryContentRoot`, как в `CliApplicationTests`. Внутри класса лежит константа валидного `appsettings.json` с `GitHub:Token`, чтобы цепочка токенов не обращалась к Credential Manager.

- `AppHostTests`
  - `BuildHost_ValidConfiguration_ResolvesMainWindowViewModel`
  - `BuildHost_ValidConfiguration_ReplacesConsoleLifetime`: `IHostLifetime` является `DesktopHostLifetime`
  - `BuildHost_ValidConfiguration_UsesSharedCoreServices`: например, `IOptions<BackupOptions>` разрешается из того же построителя
  - `BuildHost_HomeViewModel_IsTransient`
- `AppStartupTests`
  - `StartAsync_ValidConfiguration_ReturnsStartedHostAndShowsNoDialog`
  - `StartAsync_ValidConfiguration_WritesStartEntryToSharedLogFiles`: в `logs\` есть файлы с именами `LogFiles.GetFileName`, текстовый лог содержит «GitHubBackup.App»
  - `StartAsync_MissingAppSettings_ShowsConfigurationErrorAndReturnsNull`: ошибка в `Build`
  - `StartAsync_InvalidOption_ShowsConfigurationErrorAndReturnsNull`: ошибка в `StartAsync`
  - `StartAsync_InvalidOption_LogsConfigurationErrors`: запись в текстовом логе
  - `StopAsync_StartedHost_WritesStopEntry`
- `NavigationServiceTests`: тестовая ViewModel в `ServiceCollection`
  - `NavigateTo_RegisteredViewModel_SetsCurrentViewModel`
  - `NavigateTo_RegisteredViewModel_RaisesCurrentViewModelChanged`
  - `NavigateTo_UnregisteredViewModel_Throws`
  - `CurrentViewModel_BeforeNavigation_IsNull`
- `MainWindowViewModelTests`
  - `Constructor_NavigatesToHome`
  - `NavigationChanged_UpdatesCurrentViewModelAndRaisesPropertyChanged`
- Старый `AppAssemblyTests` остаётся.

**GitHubBackup.ArchitectureTests**

- `AppViewModelDependencyTests.Types_ViewModels_DoNotUseWpfUiTypes`
- `RuleDetectionTests.Types_FixtureUsingWindow_ViolatesViewModelRule`
- `RuleDetectionTests.Types_FixtureUsingRelayCommand_SatisfiesViewModelRule`: `ICommand` разрешён

## 6. Проверка

| Критерий | Как проверить |
|---|---|
| 1 | `AppHostTests.BuildHost_ValidConfiguration_ResolvesMainWindowViewModel`, `AppStartupTests.StartAsync_ValidConfiguration_*`. Вручную: запустить `GitHubBackup.App.exe` из `bin`, открывается окно с заглушкой |
| 2 | Ревью: App вызывает только `GitHubBackupHostBuilder.Create`. Тест `BuildHost_ValidConfiguration_UsesSharedCoreServices` |
| 3 | Сборка, тесты и `dotnet format --verify-no-changes`, CI |
| 4 | `StartAsync_ValidConfiguration_WritesStartEntryToSharedLogFiles`. Вручную: после запуска App в `bin\…\logs\` есть запись о старте |
| 5 | `NavigationServiceTests`, `MainWindowViewModelTests`. `IDialogService` подменяется в `AppStartupTests` |
| 6 | `BuildHost_ValidConfiguration_ReplacesConsoleLifetime`, `StopAsync_StartedHost_WritesStopEntry`. Вручную: после закрытия окна процесс завершается |
| 7 | `StartAsync_MissingAppSettings_*`, `StartAsync_InvalidOption_*`. Вручную: испортить `appsettings.json` рядом с exe, появляется MessageBox, код выхода 2 |
| 8 | Тесты лога в `AppStartupTests` |
| 9 | `AppViewModelDependencyTests`, `RuleDetectionTests` |

## 7. Вне рамок

- Экраны FR-12.1..12.6, `IUiDispatcher`, `IMessenger` и перечитывание `backup-config.json` (NFR-SYS-2) — следующие задачи F4.x.
- Модульные тесты `MessageBoxDialogService`, `App.xaml.cs` и Views: им нужен реальный WPF.
- UI-тест FlaUI «окно открывается» входит в [#42](https://github.com/askrinnik/GitHubBackup/issues/42) (F4.7: FlaUI UI tests) вместе с тестами экранов. `AutomationId` в `MainWindow` и `HomeView` уже есть.
- Глобальный обработчик `DispatcherUnhandledException` и `AppDomain.UnhandledException`.
- Передача аргументов `--Section:Key=value` из командной строки WPF в хост.
- Общая папка вывода обоих exe и перенос `appsettings.json` из проекта Cli в общее место (NFR-8, задача публикации).
- Остановка WPF по `IHostApplicationLifetime.StopApplication`.

## 8. Открытые вопросы

1. Ошибку на этапе `Build()` (например, нет `appsettings.json`) нельзя записать в лог: логгер Serilog создаётся вместе с хостом. Предложение: показывать её только в диалоге, как CLI пишет её только в stderr. Ошибку `StartAsync` показывать и писать в лог.
2. Добавлять ли сейчас FlaUI и один UI-тест запуска окна, или вынести это в отдельную задачу (сейчас вынесено)?
3. `appsettings.json` подключается в App ссылкой на `src/GitHubBackup.Cli/appsettings.json`. Подходит, или перенести файл в общее место уже сейчас?
4. Глобальный обработчик необработанных исключений вынесен за рамки. Оставить его вне задачи?
5. В `App.Tests` будет своя константа валидных настроек. Перенос `CliApplicationTests.ValidAppSettings` в `TestUtilities` — отдельный рефакторинг.

## 9. Решения ревью

Пользователь принял план и предложенные ответы на открытые вопросы (2026-11-13):

1. Ошибка в `Build()` показывается только в диалоге. Ошибка в `StartAsync` показывается в диалоге и пишется в лог.
2. FlaUI и UI-тест запуска окна входят в [#42](https://github.com/askrinnik/GitHubBackup/issues/42) (F4.7: FlaUI UI tests); новая задача не нужна.
3. `appsettings.json` подключается в App ссылкой на файл проекта Cli. Перенос в общее место — задача публикации (NFR-8).
4. Глобальный обработчик необработанных исключений — вне задачи.
5. `App.Tests` держит свою константу валидных настроек. Перенос `CliApplicationTests.ValidAppSettings` в `TestUtilities` не делается.

## 10. Задачи

- [x] Добавить `CommunityToolkit.Mvvm` в `src/Directory.Packages.props` (группа `UI`), проверить `dotnet list package --vulnerable`
- [x] Обновить `GitHubBackup.App.csproj`: `PackageReference`, ссылка на `appsettings.json` из Cli, `UserSecretsId` с комментарием
- [x] `Dialogs/IDialogService`, `Dialogs/MessageBoxDialogService`
- [x] `Navigation/INavigationService`, `Navigation/NavigationService`
- [x] `ViewModels/MainWindowViewModel`, `ViewModels/HomeViewModel`
- [x] `Hosting/DesktopHostLifetime`, `Hosting/AppServiceCollectionExtensions` (`AddGitHubBackupApp`), `Hosting/AppHost.BuildHost`, `Hosting/AppLog`, `Hosting/AppStartup`
- [x] `Views/MainWindow` (перенос, DI-конструктор, `ContentControl`, `AutomationId`), `Views/HomeView`
- [x] `App.xaml`: без `StartupUri`, `ShutdownMode=OnExplicitShutdown`, `DataTemplate`; `App.xaml.cs`: асинхронный старт и остановка
- [x] Добавить в `App.Tests.csproj` ссылки на `TestUtilities` и `Microsoft.Extensions.Diagnostics.Testing`
- [x] Тесты `AppHostTests`, `AppStartupTests`, `NavigationServiceTests`, `MainWindowViewModelTests`
- [x] Архитектурные тесты: `LayerRules` (правило ViewModel), `AppViewModelDependencyTests`, фикстура и кейсы в `RuleDetectionTests`
- [x] XML-доки на всех типах и членах, проверка комментариев по `csharp.md`
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
- [x] Ручной запуск: окно открывается, есть запись в `logs\`, при испорченном `appsettings.json` появляется MessageBox и код выхода 2
- [x] Проверить, описывает ли `README.md` состояние WPF-приложения, и при необходимости обновить
