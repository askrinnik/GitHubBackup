# План: #112 F2.8: Discover only repositories listed in backup-config.json

| Поле | Значение |
|---|---|
| Issue | [#112](https://github.com/askrinnik/GitHubBackup/issues/112) |
| Заголовок | F2.8: Discover only repositories listed in backup-config.json |
| Lane | Feature |
| Сложность | M |
| Дата | 2026-11-13 |

## 1. Цель

В `backup-config.json` появляется необязательный ключ `discoverAccessibleRepositories` (bool). Если ключа нет, он равен `false`. При записи файла (FR-2.8) ключ сохраняется, `schemaVersion` остаётся `1`. Требования: FR-1.6, FR-2.1, FR-2.2, FR-2.3, FR-2.4, FR-2.5, FR-2.8, FR-9.4, FR-9.5, FR-9.10, §7.3.

- **`false`, строгий режим (по умолчанию).** Запрос `/user/repos` выполняется как раньше. Новыми становятся только репозитории, чей owner без учёта регистра совпадает с аккаунтом из `accounts` (FR-2.2). Репозитории других владельцев не предлагаются, не добавляются в настройки и не бэкапятся: в интерактивном режиме, с `--silent` и с `--dry-run`. Код `3` из-за них не возникает.
- **`true`.** Поведение остаётся текущим, включая FR-2.3.
- **Оба режима.** Отдельные репозитории (`repositories`) бэкапятся. Пропавшие, вернувшиеся и переименованные записи распознаются как раньше (FR-2.4, FR-2.5).

**Почему фильтр стоит только в классификации новых.** `RepositoryDiscoveryService.DiscoverAsync` ищет записанные репозитории в собранном списке по ID. Только если записи там нет, он вызывает `GET /repos/{owner}/{repo}` и затем `GET /repositories/{id}`. Если отфильтровать сам собранный список, каждая отдельная запись чужого владельца даст лишние запросы, а переименование в список для чужого владельца потеряется. Поэтому собранный список и `DiscoveryResult.Repositories` остаются полными, а репозиторий вне аккаунтов пропускается только при выборе новых. `HasChanges`, код `3`, автодобавление в `--silent` и события истории считаются из классов результата, поэтому другие правки в ядре не нужны.

## 2. Критерии приёмки

Из issue:
- [x] PRD обновлён: §7.3, FR-2.1, FR-2.3 (условие), связанные места, версия и строка истории
- [x] Тест: `backup-config.json` без ключа читается как `false`; ключ сохраняется при записи файла
- [x] Тест (`false`): при пустом `accounts` и двух записях в `repositories` discovery не возвращает новых репозиториев, хотя `/user/repos` вернул другие репозитории пользователя
- [x] Тест (`false`): репозитории организаций и чужих владельцев из `/user/repos` не попадают в `New`, если их owner нет в `accounts`
- [x] Тест (`false`): приватный репозиторий отслеживаемого аккаунта, который пришёл только из `/user/repos`, попадает в `New`
- [x] Тест (`false`): `backup --silent` не добавляет в `backup-config.json` репозитории владельцев вне `accounts`; они не дают код возврата `3`
- [x] Тест (`true`): репозиторий другого владельца из `/user/repos` попадает в `New` как отдельный; `--silent` добавляет его в `repositories`; код возврата `3`
- [x] Тест (оба режима): пропавший и переименованный отдельный репозиторий определяются как раньше
- [x] README обновлён
- [ ] Покрыто тестами, CI зелёный

Добавлено проверкой покрытия:
- [x] *(добавлено)* В обоих режимах отдельная запись, которая есть в `/user/repos`, сопоставляется без `GetRepositoryAsync` и `GetRepositoryByIdAsync`.
- [x] *(добавлено)* В обоих режимах перенос отдельной записи к owner вне `accounts` распознаётся как переименование (через поиск по ID).
- [x] *(добавлено)* В строгом режиме чужой репозиторий с недопустимым именем не даёт предупреждения FR-2.9.
- [x] *(добавлено)* `"discoverAccessibleRepositories": null` и значение не типа bool дают ошибку загрузки с JSON-путём.
- [x] *(добавлено)* `--dry-run` в строгом режиме не показывает чужие репозитории как новые и не даёт из-за них код `3`.

## 3. Изменения требований (PRD 0.22 → 0.23)

- **Шапка:** версия 0.23.
- **FR-1.6:** глобальная маска для репозиториев вне аккаунтов нужна, только если включён `discoverAccessibleRepositories`.
- **FR-2.1:** `/user/repos` запрашивается всегда; ключ задаёт, какие собранные репозитории могут стать новыми (`false` — только отслеживаемые аккаунты, `true` — также FR-2.3); записанные репозитории сопоставляются со всем собранным списком при любом значении ключа.
- **FR-2.3:** действует только при `discoverAccessibleRepositories: true`.
- **FR-9.4, FR-9.5, FR-9.10 (код `3`):** новые — по правилу FR-2.1.
- **FR-12.5:** редактор настроек включает ключ (реализация — #40).
- **§7.3:** ключ в примере после `exclude`, описание и значение по умолчанию, правило валидации (`true`/`false`; `null` и другой тип — ошибка).
- **§14:** строка 0.23.

§9 не меняется.

## 4. Затронутые проекты и типы

- **GitHubBackup.Core**
  - `BackupConfiguration/BackupConfig.cs`: свойство `DiscoverAccessibleRepositories` между `Exclude` и `Accounts`.
  - `Discovery/RepositoryDiscoveryService.cs`: в цикле классификации новых репозиторий вне аккаунтов пропускается, если задан фильтр аккаунта или ключ равен `false`. Проверка стоит до проверки имени (FR-2.9). Сопоставление записанных репозиториев и сбор не меняются.
- **GitHubBackup.Infrastructure:** изменений нет; `BackupConfigJsonContext` подхватывает свойство, `null` даёт `JsonException` с путём.
- **GitHubBackup.Cli / GitHubBackup.App:** изменений нет.
- **Тесты:** `Core.Tests/Discovery/RepositoryDiscoveryServiceTests.cs`, `Infrastructure.Tests/BackupConfiguration/{JsonBackupConfigStoreTests,JsonBackupConfigStoreSaveTests,BackupConfigSamples}.cs`, `Cli.Tests/{BackupConfigJson,CliEndToEndTests,BackupHistoryEndToEndTests}.cs`.
- **Документы:** `docs/PRD.md`, `README.md`.

## 5. Тесты

**`RepositoryDiscoveryServiceTests`.** Флаг `true` в тестах, которые проверяют новые отдельные репозитории (`UserRepositoryOutsideAccounts_IsNewStandalone`, `NewStandaloneRepositoryWithInvalidName_IsNotNew`, `ListedAndLookedUpRepositories_AreInRepositoriesById`, `AccountFilter_SkipsNewStandaloneAndOtherAccounts`, `ExcludedRepository_IsNeitherNewInAccountNorNewStandalone`). Новые:
- `DiscoverAsync_StrictNoAccountsTwoStandalone_ReturnsNoNewDespiteOtherUserRepositories`
- `DiscoverAsync_StrictOrganizationAndOtherOwnerRepositories_AreNotNew`
- `DiscoverAsync_StrictPrivateTrackedAccountRepositoryOnlyFromUserRepos_IsNew` (owner в другом регистре)
- `DiscoverAsync_StrictOtherOwnerWithInvalidName_LogsNoInvalidNameWarning`
- `DiscoverAsync_AccessibleOtherOwnerRepository_IsNewStandalone`
- Theory `(bool)`: `StandaloneRecordNotFound_IsMissing`, `StandaloneRecordInUserRepos_MatchedWithoutLookup`, `StandaloneRecordRenamedInUserRepos_IsRenamed`, `StandaloneRecordTransferredToOtherOwner_IsRenamedThroughIdLookup`, `UnavailableStandaloneRecordInUserRepos_IsReturned`.

**Infrastructure.Tests.** `DocumentedSample` синхронизирован с §7.3; чтение без ключа → `false`; чтение `true`; `null` и `"yes"` → ошибка с путём `$.discoverAccessibleRepositories`; запись файла без ключа даёт `false`; загрузка и сохранение сохраняют `true`; ожидаемый JSON обновлён.

**Cli.Tests.** `BackupConfigJson.WithDiscoverAccessibleRepositories()`, `Build()` всегда выводит ключ. `CliEndToEndTests`: accessible-режим с `--silent` добавляет репозиторий в `repositories`, код `3`; строгий `--silent` — код `0`, файл не изменён, архивов чужих владельцев нет. `BackupHistoryEndToEndTests`: флаг в существующем dry-run тесте; строгий dry-run — чужой репозиторий не событие.

## 6. Вне рамок

- UI-редактор настроек для ключа (FR-12.5, #40).
- Лог-сообщение о числе пропущенных чужих репозиториев.
- CLI-опция для переключения режима.
- Контрактные тесты Octokit: запросы к API не меняются.

## 7. Задачи

- [x] PRD: FR-1.6, FR-2.1, FR-2.3, FR-9.4, FR-9.5, FR-9.10, FR-12.5, §7.3, версия 0.23, строка §14
- [x] `BackupConfig.DiscoverAccessibleRepositories` с XML-doc
- [x] `RepositoryDiscoveryService.DiscoverAsync`: фильтр owner вне аккаунтов только в классификации новых
- [x] `RepositoryDiscoveryServiceTests`: флаг в затронутых тестах, новые тесты обоих режимов
- [x] Infrastructure.Tests: образец, чтение, запись, ошибки типа
- [x] Cli.Tests: `BackupConfigJson`, end-to-end тесты `--silent` и `--dry-run`
- [x] README
- [x] Сборка, тесты, `dotnet format --verify-no-changes`

## 8. Решения

- Пользователь разрешил сразу переходить к реализации, если нет вопросов по планированию. Изменение PRD задано в issue, поэтому PRD правится в том же изменении без отдельного согласования текста.
- Ключ записывается при каждом сохранении, в том числе `false` (как `sourcesRoot: null`): первое сохранение файла без ключа добавит `"discoverAccessibleRepositories": false`.
- Место ключа в JSON — после `exclude`, перед `accounts`.
