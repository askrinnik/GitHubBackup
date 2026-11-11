# План: #83 «Capture logs in tests with FakeLogger»

| Поле | Значение |
|---|---|
| Issue | [#83](https://github.com/askrinnik/GitHubBackup/issues/83) |
| Название | Capture logs in tests with FakeLogger |
| Полоса | Test-authoring |
| Сложность | L |
| Дата | 2026-11-11 |

## 1. Цель

Тесты проверяют записи лога, которые требует PRD, по стабильным признакам: `EventId`, уровень и структурные свойства, а не текст сообщения. Для этого в тесты добавляется пакет `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger<T>`). Требования: FR-2.9 (предупреждение о недопустимом имени — сейчас не проверяется вовсе, тест передаёт `NullLogger`), а также FR-1.8, FR-1.6, FR-4.4, FR-5.2, FR-9.9, FR-2.4, у которых в `Core.Tests` уже есть проверки по тексту через самописный `RecordingLogger<T>`. Production-код не меняется.

## 2. Критерии приёмки

1. Пакет добавлен в PRD §8.3 и в src/Directory.Packages.props, уязвимостей нет
2. Тесты недопустимых имён в RepositoryDiscoveryServiceTests проверяют предупреждение InvalidName
3. Правило проверки логов описано в .claude/rules/tests.md и в навыке write-tests
4. Покрыто тестами, CI зелёный

Добавлено по сверке с кодом:

5. *(добавлено)* В `Core.Tests` логи проверяются только через `FakeLogger`. Проверок по тексту сообщения не остаётся; исключение — отрицательные проверки на секрет. Файл `src/GitHubBackup.Core.Tests/Logging/RecordingLogger.cs` удалён.
6. *(добавлено)* Если репозиторий исключён маской, `InvalidName` для него не пишется: исключение проверяется раньше имени (`RepositoryDiscoveryService.DiscoverAsync`).

## 3. Изменения PRD

`docs/PRD.md`:
- §8.3, строка «Тесты»: `xUnit v3, NSubstitute, Shouldly, WireMock.Net, FlaUI, NetArchTest, Microsoft.Extensions.Diagnostics.Testing`.
- В шапке версия `0.16` → `0.17 (черновик)`.
- §14, новая строка: `| 0.17 | 2026-11-11 | §8.3: в стек тестов добавлен Microsoft.Extensions.Diagnostics.Testing (FakeLogger) для проверки записей лога |`.

## 4. Затрагиваемые проекты и типы

- **`src/Directory.Packages.props`**: рядом с `Microsoft.Extensions.TimeProvider.Testing` (`10.11.0`) — `<PackageVersion Include="Microsoft.Extensions.Diagnostics.Testing" Version="10.11.0" />` (тот же репозиторий dotnet/extensions и та же нумерация).
- **`GitHubBackup.Core.Tests`**: `<PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" />`. Другие проекты ссылку не получают.
  - `Discovery/RepositoryDiscoveryServiceTests` — `NullLogger` заменить на поле `FakeLogger<RepositoryDiscoveryService> _logger`, добавить проверки `InvalidName`.
  - `Authentication/TokenProviderTests` — перевести с `RecordingLogger` на `FakeLogger` (FR-1.8, FR-11.1).
  - `Runs/BackupOrchestratorTests` — то же (FR-1.6, FR-4.4, FR-5.2, FR-9.9, временная папка, остановка запуска).
  - `Discovery/DiscoveryDecisionApplierTests` — то же (FR-2.4, FR-2.5).
  - `Logging/RecordingLogger.cs` — удалить, других пользователей нет.
- **Харнесс и документация:** `.claude/rules/tests.md`, `.claude/skills/_local.write-tests/SKILL.md`, `docs/ai/README.md` (строка с описанием стека `tests.md`).

## 5. Подход

1. PRD.
2. Пакет: `Directory.Packages.props` и `.csproj` проекта `Core.Tests`.
3. Тесты, по одному классу.
4. Правила и навык.

Ключевые решения:
- **Поиск записи:** `_logger.Collector.GetSnapshot().Where(r => r.Id.Id == N).ShouldHaveSingleItem()`, затем `record.Level.ShouldBe(...)` и `record.GetStructuredStateValue("Prop").ShouldBe("...")`. API: `FakeLogger(FakeLogCollector? collector = null, string? category = null)`, `FakeLogCollector.GetSnapshot(bool clear = false)`, `FakeLogRecord.Level`, `.Id`, `.StructuredState`, `.GetStructuredStateValue(string)`, `.Exception`. Значения свойств — строки, enum сравнивается как `"Account"`.
- **Номера событий** — именованные `private const int` в тест-классе (`EventId` есть только в атрибуте `[LoggerMessage]`).
- **Хелпер в `GitHubBackup.TestUtilities` не нужен:** пакет использует один проект, поиск записи — одна строка.
- **Текст сообщения допустим только в отрицательной проверке на секрет** (FR-10.6): `record.Message` и все значения `StructuredState` не содержат токен.
- **Риск сборки:** если `Diagnostics.Testing 10.11.0` транзитивно требует `Microsoft.Extensions.Logging*` новее закреплённой версии (`NU1605`/`NU1608` при `TreatWarningsAsErrors`), поднять закреплённую версию до требуемой 10.0.x и сообщить пользователю.

## 6. Тесты

Проект `GitHubBackup.Core.Tests`.

**`RepositoryDiscoveryServiceTests`** (FR-2.9, `InvalidName` = EventId 3, Warning, свойство `Repository`):
- `DiscoverAsync_NewAccountRepositoryWithInvalidName_IsNotNew(name)`: одна запись Id 3, Warning, `Repository == $"octo/{name}"`.
- `DiscoverAsync_NewStandaloneRepositoryWithInvalidName_IsNotNew(owner, name)`: одна запись Id 3, Warning, `Repository == $"{owner}/{name}"`.
- `DiscoverAsync_RecordRenamedToInvalidNameInList_IsNeitherRenamedNorMissing(name)`: одна запись Id 3, Warning, `Repository == $"octo/{name}"`.
- `DiscoverAsync_RecordRenamedToInvalidNameByIdLookup_IsNeitherRenamedNorMissing(owner, name)`: одна запись Id 3, Warning, `Repository == $"{owner}/{name}"`.
- *Новый* `DiscoverAsync_ExcludedRepositoryWithInvalidName_LogsNoInvalidNameWarning`: аккаунт `octo` с репозиторием `con`, исключение `octo/*`; записей Id 3 нет.

**`TokenProviderTests`:**
- `GetTokenAsync_NoToken_LogsWarning`: одна запись Id 2, Warning (проверки текста удаляются).
- `GetTokenAsync_TokenFound_LogsSourceNameWithoutValue`: одна запись Id 1, Information, `Source == "my source"`; `record.Message` и все значения `StructuredState` не содержат секрет.

**`BackupOrchestratorTests`** (поле `FakeLogger<BackupOrchestrator> _logger`):

| Тест | Проверка |
|---|---|
| `RunAsync_BranchFallback_ReportsWarningWithArchive` | Id 7, Warning, `Warning == item.Message` |
| `RunAsync_DetectorFails_ReportsFailedAndProcessesNext` (FR-9.9) | одна запись Id 8, Error, `Exception.Message` |
| тест с `BackupConfigException` | записи Id 1: Error, значения `Error` — обе ошибки |
| тест с двумя удалёнными остатками | ровно две записи Id 2 |
| `RunAsync_LeftoverCannotBeDeleted_LogsWarningAndContinues` | Id 3, Warning, `Path` |
| `RunAsync_TemporaryFolderIsLink_LeavesItsContentAndContinues` | Id 10, Warning, `Path` |
| `RunAsync_ListedRepositoryMatchesExclusion_...` (FR-1.6) | Id 11, Warning, `Repository`, `Mask`, `Scope == "Global"` |
| `RunAsync_AccountMaskMatchesAccountRepository_IsExcluded` | Id 11, `Repository`, `Mask`, `Scope == "Account"` |
| `RunAsync_WikiNotFound_SkipsWithoutResultAndLogs` (FR-5.2) | одна запись Id 13, Information; записей уровня Error нет |
| тест ошибки API wiki 502 | Id 14, Error |
| `RunAsync_RepositoryFilter_SkipsDiscoveryAndProcessesOnlyThatRepository` | Id 15, `Repository` |
| `RunAsync_DiscoveryFails_ReturnsCriticalWithoutApplyingOrProcessing` | Id 1, Error, `Error == failure.Message` |

**`DiscoveryDecisionApplierTests`:**
- Тест FR-2.4: Id 3, Warning, `Repository == "octo/app"`.
- `ApplyAsync_Returned_MarksActiveAndReports`: Id 4, Information, `Repository == "acme/lib"`.
- `ApplyAsync_RenameWithArchiveFolderWhoseTargetExists_FallsBackToKeepingFolder`: Id 7, Warning, `OldFolder`, `NewFolder`.

## 7. Проверка

| Пункт | Как проверить |
|---|---|
| 1 | diff `docs/PRD.md` и `src/Directory.Packages.props`; `dotnet list src/GitHubBackup.slnx package --vulnerable --include-transitive` — уязвимостей нет |
| 2 | `dotnet test --project src/GitHubBackup.Core.Tests/GitHubBackup.Core.Tests.csproj --no-build --filter-class "*RepositoryDiscoveryServiceTests"` |
| 3 | diff `.claude/rules/tests.md` и `.claude/skills/_local.write-tests/SKILL.md` |
| 4 | полная сборка, тесты решения, `dotnet format --verify-no-changes`; CI пулл-реквеста |
| 5 | Grep `RecordingLogger` и `.Message.Contains` в `src/GitHubBackup.Core.Tests` — только отрицательная проверка на секрет |

## 7a. Infrastructure-проекты (решение на ревью)

`Infrastructure.Tests` (`BackupConfiguration/RecordingLogger.cs`: уровень и текст) и `Infrastructure.IntegrationTests` (`Processes/CapturingLogger.cs`: уровень, `EventId`, текст, свойства как объекты, потокобезопасно) тоже переводятся на `FakeLogger`. `FakeLogger` покрывает все их функции: уровень, `EventId`, текст, структурные свойства, исключение, потокобезопасный сбор, плюс области. Отличие одно: значения свойств — строки, поэтому сравнение идёт со строковым представлением.

- Оба проекта получают `PackageReference` на `Microsoft.Extensions.Diagnostics.Testing`.
- Все пользователи `RecordingLogger`/`CapturingLogger` (`ProcessRunnerTests`, `GitClientTests`, тесты конфигурации, `GitClientEmptyRepositoryTests`, `GitClientBranchTests`, `GitClientTokenTests`, `SevenZipArchiverTests` и другие) переходят на `FakeLogger`; запись ищется по `EventId`, проверяются уровень и свойства. Текст читается только в отрицательных проверках на секрет (FR-10.6, FR-11.3) и там, где у записи нет `EventId` и свойств, которые несут проверяемое значение.
- Файлы `RecordingLogger.cs` и `CapturingLogger.cs` (и тип `CapturedEntry`) удаляются.
- Требования, которые эти тесты закрывают: FR-10.4 / FR-4.7 / FR-7.7 (команда, построчный вывод со свойствами `Tool`, `Stream`, `Repository`, код возврата), FR-10.6 (маскирование), FR-4.5 (пустой репозиторий), FR-4.4.
- Если для какого-то теста `FakeLogger` не даёт того, что давал самописный логгер, — остановиться и сообщить, а не оставлять два вида логгера молча.

## 8. Вне рамок

- **FR-3.8** — предупреждение возвращает `BackupConfigValidator` как результат валидации, а не пишет в лог; покрыто `BackupConfigValidatorTests`.
- **Тесты с `NullLogger`, которые логи не проверяют** — не трогаются, как просит issue.

## 9. Решения

1. `Core.Tests`: все три класса с `RecordingLogger` переводятся на `FakeLogger`, `RecordingLogger` удаляется.
2. Если пакет потребует более новую `Microsoft.Extensions.Logging.Abstractions` (`NU1605`/`NU1608`), её закреплённая версия поднимается до минимальной требуемой в линейке 10.0.x; об этом сообщается в отчёте.
3. Infrastructure-проекты переводятся на `FakeLogger` в этой же задаче (раздел 7a), follow-up issue не нужен. Сложность — L.

## 10. Задачи

- [x] `docs/PRD.md`: §8.3, версия 0.17, строка истории
- [x] `src/Directory.Packages.props`: `Microsoft.Extensions.Diagnostics.Testing` `10.11.0`
- [x] `GitHubBackup.Core.Tests.csproj`: `PackageReference`
- [x] Сборка; при `NU1605`/`NU1608` поднять `Microsoft.Extensions.Logging.Abstractions`
- [x] `dotnet list … package --vulnerable --include-transitive`
- [x] `RepositoryDiscoveryServiceTests`: `FakeLogger`, проверки в четырёх теориях, новый тест про исключённый репозиторий
- [x] `TokenProviderTests`: перевод на `FakeLogger`
- [x] `BackupOrchestratorTests`: перевод проверок по таблице
- [x] `DiscoveryDecisionApplierTests`: перевод трёх проверок
- [x] Удалить `Logging/RecordingLogger.cs` и лишние `using`
- [x] `Infrastructure.Tests`, `Infrastructure.IntegrationTests`: `PackageReference`, перевод тестов на `FakeLogger`, удалить `RecordingLogger.cs`, `CapturingLogger.cs`
- [x] `.claude/rules/tests.md`: пункт **Logs** в «Stack»
- [x] `.claude/skills/_local.write-tests/SKILL.md`: пункт в Checklist
- [x] `docs/ai/README.md`: описание `tests.md`
- [x] Сборка, тесты, формат
