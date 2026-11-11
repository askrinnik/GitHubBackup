# План: #21 «F2.2: Exclusion masks»

- Issue: [#21](https://github.com/askrinnik/GitHubBackup/issues/21)
- Lane: Feature (area:core)
- Сложность: S
- Дата: 2026-11-11
- Пакет: `/implement-issues 21–25`

## 1. Цель

Нужен доменный компонент `IExclusionMatcher` в `GitHubBackup.Core`. Он решает, исключён ли репозиторий масками `exclude` из `backup-config.json` (FR-1.6, §6.1). Маски поддерживают `*` и `?` и сравниваются без учёта регистра. Маска аккаунта (`AccountConfig.Exclude`) сравнивается с именем репозитория. Глобальная маска (`BackupConfig.Exclude`) сравнивается с полным именем `owner/repo`. Если репозиторий есть в списке и подпадает под исключение, исключение главнее: репозиторий не бэкапится, в лог пишется предупреждение.

## 2. Критерии приёмки

Из issue:

1. Unit tests for masks and priority
2. Covered by tests, CI green

Добавлено по PRD и сверке с кодом:

- `*` совпадает с любой последовательностью символов, в том числе пустой; `?` — ровно с одним символом. Остальные символы (`.`, `-`, `_`, `[`, `+`, `(`, `$`, `\`, `^`) сравниваются буквально.
- Маска без wildcard требует точного совпадения без учёта регистра.
- Маски аккаунта применяются к репозиториям, чей owner равен login аккаунта (без учёта регистра); маски другого аккаунта не применяются.
- Глобальная маска без `/` не совпадает ни с одним `owner/repo`; глобальная `*` исключает всё.
- Пустые списки масок ничего не исключают.
- Matcher возвращает сработавшую маску и её уровень (для текста предупреждения).
- Порядок проверки: сначала маски аккаунта по порядку в файле, потом глобальные.
- Запись списка, подпадающая под исключение, не бэкапится в запуске orchestrator, в лог пишется Warning с маской (FR-1.6, последний абзац).

## 3. Изменения PRD

Не нужны: FR-1.6 описывает поведение полностью.

## 4. Затрагиваемые проекты и типы

**GitHubBackup.Core**, `BackupConfiguration/`:

- `IExclusionMatcher`: `ExclusionMatch? Match(BackupConfig config, string owner, string name)`; `null` — не исключён.
- `ExclusionMatcher` (`sealed`): находит аккаунты, чей login (`GitHubUrl.TryParseAccount`) равен `owner` по `OrdinalIgnoreCase`, проверяет их `Exclude` на `name`, затем `config.Exclude` на `owner/name`. Аккаунты с невалидным URL пропускает.
- `ExclusionMatch` (`sealed record`): `(string Mask, ExclusionScope Scope)`.
- `ExclusionScope` (enum): `Account`, `Global`.
- `WildcardMask` (`internal static`): `bool IsMatch(string mask, string text)` — итеративный сопоставитель с откатом на последнюю `*`, O(n·m), без `Regex`, сравнение через `char.ToUpperInvariant`.
- `CoreServiceCollectionExtensions.AddGitHubBackupCore`: `TryAddSingleton<IExclusionMatcher, ExclusionMatcher>()`.

**GitHubBackup.Core**, `Runs/`: `BackupOrchestrator` (или место, где строится список `BackupWorkItem`) пропускает исключённые записи и пишет Warning через новое событие `BackupOrchestratorLog`.

**GitHubBackup.Core.Tests**: `WildcardMaskTests`, `ExclusionMatcherTests`, тест orchestrator на исключённую запись.

## 5. Подход

1. `WildcardMask` без регулярных выражений: пользовательский текст не попадает в `Regex` (правило I4), нет ReDoS. `FileSystemName.MatchesSimpleExpression` отклонён: это `System.IO.Enumeration`, а `Core` не берёт I/O-API.
2. `ExclusionMatcher` без состояния, логов и I/O; `BackupConfig` приходит аргументом, поэтому один singleton годится и для orchestrator, и для discovery (#22).
3. Регистрация в DI.
4. Подключение к orchestrator: исключённая запись списка не попадает в работу запуска, в лог — Warning с `owner/repo`, маской и уровнем.

## 6. Тесты

**`WildcardMaskTests`** (theory): точное совпадение и регистр; `*` (`test-*`, `*-archive`, `*` на пустой строке, `a*b*c`, `**`, `*a`); `?` (`re?o`, `?` на пустой строке); спецсимволы буквально (`my.repo`, `a+b`, `[ab]`, `a_b-c`, `^a$`, `a\*`); пустая маска; `/` в глобальной форме (`someone/*`, `*/repo`); длинный текст с `*a*a*a*a*b` проходит быстро.

**`ExclusionMatcherTests`**: пустые списки → `null`; маска аккаунта совпадает с именем; owner в другом регистре; маска чужого аккаунта не применяется; маска аккаунта не видит `owner/`; глобальная маска по полному имени; глобальная без `/` не совпадает; глобальная `*`; при совпадении обеих возвращается маска аккаунта; owner без аккаунта — только глобальные; невалидный URL аккаунта игнорируется.

**Orchestrator**: запись списка под исключением не бэкапится, пишется Warning; неисключённые записи бэкапятся как раньше.

## 7. Проверка

| Пункт | Как проверить |
|---|---|
| Маски, регистр, спецсимволы | `WildcardMaskTests` |
| Уровни, приоритет, пустые списки | `ExclusionMatcherTests` |
| Исключение главнее записи списка | тест orchestrator |
| CI green | `build-runner`: rebuild, тесты, `dotnet format --verify-no-changes` |

## 8. Вне рамок

- Классификация discovery с учётом исключений (FR-2.2, FR-2.3) — #22.
- Изменения валидатора: проверка масок на `null` уже есть.
- `[...]`, путевая семантика `**`, regex-маски: PRD их не требует.

## 9. Решения

- Маски аккаунта применяются к любому репозиторию с тем же owner, в том числе к записи в группе `repositories`: matcher смотрит на owner, а не на группу.
- Правило «исключение главнее записи + Warning» подключается в orchestrator в этом же issue (оно названо в задаче #21), а не откладывается в #25. Исключённая запись не попадает в результаты запуска.
- Пробелы по краям маски не обрезаются.
- Свой `WildcardMask` вместо `Regex` и `FileSystemName`.

## 10. Задачи

- [x] `WildcardMask.cs`
- [x] `ExclusionScope.cs`, `ExclusionMatch.cs`, `IExclusionMatcher.cs`, `ExclusionMatcher.cs`
- [x] Регистрация в `AddGitHubBackupCore`
- [x] Пропуск исключённых записей в orchestrator с Warning
- [x] `WildcardMaskTests`, `ExclusionMatcherTests`, тест orchestrator
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
