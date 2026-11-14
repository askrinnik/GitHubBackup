# План: #44 F5.2: README: installation, token, settings, Task Scheduler

| | |
|---|---|
| Issue | [#44](https://github.com/askrinnik/GitHubBackup/issues/44) |
| Lane | Feature (только документация) |
| Сложность | M |
| Дата | 2026-11-14 |

## 1. Цель

Пользователь должен настроить и запустить бэкап с нуля только по README. Для этого README описывает:
- установку из опубликованной папки;
- создание токена (classic PAT, `repo` + `read:org`) и причины, по которым fine-grained PAT не подходит (FR-11.4, FR-11.1);
- `appsettings.json` и `backup-config.json` с примерами (§7.1–§7.3, §6.3), включая сценарий с OneDrive;
- команды CLI и коды возврата (§6.9);
- создание задачи в Планировщике заданий (П-2, §12, фаза 5).

Новое правило: документация по программе пишется на английском. Поэтому сначала меняется NFR-7 в PRD, затем README полностью переписывается на английском. PRD, планы, `docs/ai/` и тексты issues остаются на русском; существующие документы, кроме README, не переводятся.

## 2. Критерии приёмки

- [ ] По README можно настроить и запустить бэкап с нуля (из issue, дословно; проверяет пользователь вручную).
- [x] (добавлено, FR-11.4) README объясняет, почему fine-grained PAT может не дать доступ к репозиториям организаций и к чужим репозиториям, где пользователь коллаборатор.
- [x] (добавлено, FR-11.1) README предупреждает, что токен в командной строке (`--GitHub:Token=…`) видят другие процессы и что он остаётся в истории shell. README рекомендует Credential Manager (`set-token`) или переменную окружения.
- [x] (добавлено, §7.1) README говорит, что папка приложения должна быть доступна на запись (не `C:\Program Files`), если `sourcesRoot` не переопределён.
- [x] (добавлено) README говорит, что `backup-config.json` нужно создать до первого запуска. Без файла `backup`, `validate` и редактор настроек сообщают `The backup configuration file '…' does not exist.` (`JsonBackupConfigStore.ReadAsync`), CLI завершается с кодом `2`.
- [x] (добавлено) Каждый пример JSON в README проходит текущую проверку. Пример `appsettings.json` совпадает с `src/GitHubBackup.Cli/appsettings.json`.
- [x] (добавлено) Команды, параметры и коды возврата в README совпадают с `CliCommandLine` и `ExitCode`.
- [x] (добавлено, новое правило) README написан на английском. NFR-7, `CLAUDE.md`, `.claude/rules/docs.md` и `docs/ai/README.md` описывают правило одинаково.
- [x] (добавлено) Ни один факт из нынешнего README не потерян: каждый раздел перенесён в README или в руководство разработчика.
- [x] (добавлено) В примерах только нейтральные значения: `C:\\Users\\Me\\OneDrive\\GitHubBackups`, `octocat`, `my-org`. Пример из PRD §7.3 не копируется дословно.

## 3. Изменения требований

Шаг «PRD first»: правка PRD согласуется с пользователем до правки README.

`docs/PRD.md`:
- Шапка: `Версия документа` `0.27 (черновик)` → `0.28 (черновик)`, `Дата` `2026-11-14`.
- §10, NFR-7, новая формулировка:

  `| NFR-7 | **Язык.** Код, комментарии, логи, сообщения CLI и UI — на английском. Заголовки issues, коммиты, pull request и комментарии к issues и PR — на английском. Документация по программе — `README.md` и руководство разработчика `docs/development.md` — на английском. PRD, планы (`docs/plans/`), документация AI-харнесса (`docs/ai/`) и тексты issues — на русском |`

  Если материалы для разработчика остаются в README, убрать «и руководство разработчика `docs/development.md`».
- §14, новая строка:

  `| 0.28 | 2026-11-14 | NFR-7: README и документация по программе (руководство разработчика `docs/development.md`) — на английском; PRD, планы, документация AI-харнесса и тексты issues остаются на русском |`

Другие требования не меняются. Инструкция для Планировщика — текст в README (П-2).

## 4. Затронутые файлы

Production-код и тесты не меняются.

| Файл | Изменение |
|---|---|
| `docs/PRD.md` | NFR-7, версия, дата, строка истории |
| `README.md` | полностью переписан на английском, сначала руководство пользователя |
| `docs/development.md` (новый, английский) | материалы для разработчика из нынешнего README, без потерь |
| `CLAUDE.md` | «Language (NFR-7)»: English — `README.md`, `docs/development.md`; Russian — PRD, `docs/plans/`, `docs/ai/`, тексты issues. «Repository layout»: `development.md` под `docs/` |
| `.claude/rules/docs.md` | первый пункт: `README.md` и `docs/development.md` на английском; PRD, plans, `ai/` на русском (NFR-7) |
| `.claude/rules/update-docs-on-code-change.md` | `docs/development.md` рядом с `README.md` в строках о сборке, тестах и CI |
| `docs/ai/README.md` | раздел «Языки» (по-русски): документация по программе (`README.md`, `docs/development.md`) — на английском; PRD, планы, `docs/ai/` и тексты issues — на русском; язык планов задаёт `plans.language` |

Не меняются: `issue-kit.config.json` (`plans.language` = Russian), `.github/ISSUE_TEMPLATE/*.yml`, `.github/pull_request_template.md`, существующие планы.

## 5. Подход

### Порядок работы

1. Пользователь согласует NFR-7 (0.28).
2. Правила харнесса: `CLAUDE.md`, `.claude/rules/docs.md`, `.claude/rules/update-docs-on-code-change.md`, `docs/ai/README.md`.
3. Сверка фактов с кодом (список ниже).
4. `docs/development.md`, затем новый `README.md`.
5. Проверка (раздел 7).

### Руководство разработчика

Рекомендация: материалы для разработчика — в `docs/development.md`; в README раздел «Development» из 3–5 строк со ссылкой. Причины: README уже ~400 строк, руководство пользователя добавит ~200; аудитории разные. Описания поведения (`clonePath`, проверка клона, `--dry-run`, запись без `id`, обнаружение, отмена, телеметрия) касаются пользователя и остаются в README.

### Структура README (английский)

1. **GitHubBackup** — что делает программа. Устаревшая строка статуса удаляется.
2. **Requirements** — Windows 11, .NET Desktop Runtime 10, Git 2.4x+, Git LFS 3.x, 7-Zip, classic PAT.
3. **Installation** — артефакт CI `GitHubBackup` или `scripts/publish.ps1`; папка, доступная на запись; раскладка папки (§7.1); оба exe делят конфигурацию.
4. **GitHub token** — classic PAT `repo` + `read:org`; SAML SSO; **Why not a fine-grained token** (FR-11.4); `set-token` / **Save token**; цепочка источников, предупреждение о `--GitHub:Token=…`, анонимный режим.
5. **Configuration** — `appsettings.json` (копия исходного и таблица ключей §7.2), источники и приоритет, `user-settings.json`, `backup-config.json`: минимальный и полный пример, таблица ключей и правил (§7.3, FR-3.1–FR-3.8).
6. **OneDrive scenario** — архивы в OneDrive, клоны локально; предупреждение FR-3.8; папку приложения не класть в OneDrive без `sourcesRoot`; схема раскладки FR-3.9.
7. **First run** — `validate`, `backup --dry-run`, `backup`; сводка и лог.
8. **Command-line reference** — команды и параметры, `--Section:Key=value`, ошибки аргументов, блокировка, неинтерактивный ввод, отмена, статусы, коды возврата; подразделы: discovery mode, entries without `id`, `clonePath`, clone check, `--dry-run`.
9. **Scheduled backups (Task Scheduler)** — `GitHubBackup.Cli.exe backup --silent`; ежедневный триггер; тот же пользователь, что выполнил `set-token`; не включать «Do not store password»; альтернатива — переменная `GitHubBackup__GitHub__Token`; «Last Run Result» `0x0`–`0x3`; один запуск одновременно; шаги в `taskschd.msc` и необязательный пример PowerShell.
10. **Desktop app (WPF)** — дерево, прогресс, сводка, обнаружение, история, редактор настроек целиком.
11. **Logs, history and telemetry** — `logs\`, `data\history.db`, OpenTelemetry целиком.
12. **Development** — ссылки на `docs/development.md` и `docs/PRD.md`, правила языка.

### `docs/development.md` (английский)

Структура репозитория; SDK (`global.json`); сборка, запуск, тесты; MTP; категории тестов, `PublishedApplicationTests`, `PUBLISHED_APP_PATH`; UI-тесты; публикация; миграции; Defender и Dev Drive; CI; шаблоны issues и PR; `TreatWarningsAsErrors`, `Directory.Packages.props`.

### Сверка фактов с кодом

- `src/GitHubBackup.Cli/appsettings.json` — значения по умолчанию.
- Валидаторы §7.2: `GitHubOptionsValidator`, `ToolsOptionsValidator`, `HistoryOptionsValidator`, `OpenTelemetryOptionsValidator`, `BackupOptionsValidator`, `SerilogOptionsValidator`.
- `GitHubBackupHostBuilder`, `GitHubBackupHostSettings` — источники конфигурации, `user-settings.json`, корень содержимого.
- `BackupConfigValidator`, `BackupConfig`, `AccountConfig`, `AccountRepositoryConfig`, `RepositoryConfig`, `RepositoryStatus`, `BackupConfigJsonContext`, `JsonBackupConfigStore`.
- `CliCommandLine`, `ExitCode`.
- `*TokenSource`, `CredentialManagerTokenStore`.
- `ToolLocator`.
- `scripts/publish.ps1`.

## 6. Тесты

Код не меняется, новых автотестов нет; ни один тест не читает `README.md`. Регрессия: сборка, тесты и формат остаются зелёными.

## 7. Проверка

| Пункт | Как проверить |
|---|---|
| Настройка с нуля | Публикация во временную папку; пройти README: токен, минимальный `backup-config.json`, `validate`, `backup --dry-run`, `backup`; коды возврата, архив, клон, лог |
| Примеры JSON | Каждый пример `backup-config.json` — `GitHubBackup.Cli.exe validate --config <example>`, код `0`; пример `appsettings.json` сравнить с исходным; `user-settings.json` рядом с exe и `validate` |
| Команды и коды | `--help` всех команд; сверка с `ExitCode` |
| Планировщик | Создать задачу по инструкции, запустить, проверить «Last Run Result» и лог |
| Ссылки и якоря | Все относительные ссылки и якоря `README.md` и `docs/development.md` |
| Правило языка | Поиск противоречий в `CLAUDE.md`, `.claude/rules/`, `docs/ai/README.md`, `README.md` |
| Сохранность фактов | Каждый раздел старого README найден в новом README или в `docs/development.md` |
| Регрессия | Сборка, тесты, проверка формата |

## 8. Вне рамок

- Перевод `docs/PRD.md`, `docs/plans/**`, `docs/ai/**`, шаблонов issues и PR.
- Команда регистрации задачи Планировщика (П-2).
- Создание `backup-config.json` программой при первом запуске.
- Установщик и подпись exe.

## 9. Открытые вопросы

1. `docs/development.md` (рекомендация) или всё в README? От ответа зависит формулировка NFR-7.
2. Можно ли дать пример PowerShell с `Register-ScheduledTask`? Предположение: да, это текст инструкции, а не команда программы.
3. Объяснение про fine-grained PAT сверяется с docs.github.com при написании. Достаточно ли этого уровня детализации?
4. Достаточно ли нейтральных значений `C:\\Users\\Me\\OneDrive\\GitHubBackups`, `octocat`, `my-org`?
5. Редактор настроек не создаёт `backup-config.json`, если файла нет. Предположение: README велит создать файл вручную.
6. Проверка «с нуля» с настоящим `backup` обращается к GitHub с вашим токеном. Делаю её я на одном публичном репозитории или вы вручную?

## Decisions

Ревью 2026-11-14:

1. Материалы для разработчика выносятся в `docs/development.md`; формулировка NFR-7 — с упоминанием этого файла.
2. Пример PowerShell с `Register-ScheduledTask` в README допускается вместе с шагами в GUI.
3. Уровень детализации про fine-grained PAT достаточен; формулировки сверяются с docs.github.com.
4. Нейтральные значения в примерах (`C:\\Users\\Me\\OneDrive\\GitHubBackups`, `octocat`, `my-org`) приняты.
5. Создание `backup-config.json` при первом запуске выносится в отдельный issue #122 (F4.10) и делается до переписывания README. Раздел «First run» README описывает поведение, которое даст #122.
6. Проверку «с нуля» с настоящим `backup` и задачей Планировщика пользователь выполняет сам в приложении. Агент проверяет примеры через `validate --config`, `--help` и ссылки.
7. Сборка, тесты и проверка формата не запускаются: меняются только документация и файлы харнесса (решение пользователя).

## 10. Задачи

- [x] Согласовать NFR-7 и выбор `docs/development.md`; обновить `docs/PRD.md` (NFR-7, 0.28, дата, строка §14)
- [x] Обновить «Language (NFR-7)» и «Repository layout» в `CLAUDE.md`
- [x] Обновить `.claude/rules/docs.md` и `.claude/rules/update-docs-on-code-change.md`
- [x] Обновить раздел «Языки» в `docs/ai/README.md`
- [x] Сверить значения по умолчанию, правила проверки, команды и коды с кодом
- [x] Написать `docs/development.md`
- [x] Переписать `README.md` на английском
- [x] Проверить, что все факты старого README перенесены
- [x] `validate --config` для каждого примера; сравнить пример `appsettings.json` с исходным
- [x] Сверить `--help` с таблицей CLI
- [ ] Пройти README с нуля, включая задачу Планировщика (проверяет пользователь)
- [x] Проверить ссылки и якоря
- [ ] Сборка, тесты, проверка формата (пропущено по решению пользователя)
