# План: #30 — F3.5: Run summary and exit codes

| | |
|---|---|
| Issue | [#30 F3.5: Run summary and exit codes](https://github.com/askrinnik/GitHubBackup/issues/30) |
| Лейн | Feature |
| Сложность | S |
| Дата | 2026-11-12 |

## 1. Цель

Закрыть FR-9.7, FR-9.8 и FR-9.10 (§6.9). По завершении `backup` CLI показывает таблицу по репозиториям (статус, архив, длительность, ошибка), итоги по статусам и путь к логу. Код возврата выбирается по приоритету `2` > `1` > `3` > `0`.

Почти всё уже реализовано: коды — в `BackupRunResult.ExitCode`, сводка — в `BackupSummaryRenderer`, связка — в `BackupCommandHandler`. Задача сводится к тестам по формулировкам приёмки, закрытию пробелов покрытия и одной правке, которая готовит итоги к статусам dry-run из #29.

## 2. Критерии приёмки

Из issue:

1. Unit-тесты вычисления кода возврата для всех комбинаций.
2. Snapshot-тест сводки.
3. Покрыто тестами, CI зелёный.

Добавлено по сверке с кодом:

- a) Строка итогов перечисляет только статусы обычного запуска: `Archived, UpToDate, Warning, Failed, Skipped`. Новые значения `BackupItemStatus` (#29) не меняют вывод обычного запуска.
- b) В полном snapshot есть все пять статусов, строка wiki (`owner/name.wiki`) и сообщение с `[`/`]`, которое выводится как есть, а не как разметка Spectre.
- c) Остановленный запуск с предупреждениями конфигурации пишет предупреждения в stdout, ошибку и путь к логу — в stderr, без таблицы и итогов.
- d) Длительность форматируется на границах (`0:00.0`, `999 ms` → `0:00.9`, `61 min` → `61:00.0`) и не зависит от культуры.

## 3. Изменения PRD

Не нужны.

## 4. Затрагиваемые проекты и типы

- `GitHubBackup.Core` / `ExitCode`: в `<summary>` члена `Critical` добавить неверные аргументы командной строки (FR-9.10, PRD 0.14).
- `GitHubBackup.Cli` / `BackupSummaryRenderer`: итоги строятся по явному списку `RunStatuses`, а не по `Enum.GetValues<BackupItemStatus>()`.
- `GitHubBackup.Core.Tests` / `Runs/BackupRunResultTests`: исчерпывающая теория кодов возврата.
- `GitHubBackup.Cli.Tests` / `BackupSummaryRendererTests`: полный snapshot, остановленный запуск с предупреждениями, теория длительностей.

## 5. Подход

1. Логика кодов не меняется. Критическая ошибка возникает только через `BackupRunResult.Stopped`. Коды `2` из `CliApplication` и `BackupCommandHandler` уже покрыты их тестами.
2. Теория `TheoryData<bool, bool, bool, ExitCode>` по трём независимым условиям (critical, есть `Failed`, есть события обнаружения), все 8 строк выписаны явно. Три факта с теми же сочетаниями поглощаются теорией и удаляются.
3. `RunStatuses` — явный список пяти статусов; вывод не меняется.
4. Полный snapshot сравнивается с raw string literal через существующий `Normalize`. Существующий `RenderAsync_SeveralStatuses_WritesTableTotalsAndLogPath` расширяется; дублирующий `RenderAsync_BranchFallback_…` удаляется.

## 6. Тесты

- `BackupRunResultTests.ExitCode_AllConditionCombinations_FirstMatchingFromTopWins` — 8 строк:

  | critical | failed | events | код |
  |---|---|---|---|
  | f | f | f | `Success` |
  | f | f | t | `DiscoveryEvents` |
  | f | t | f | `RepositoriesFailed` |
  | f | t | t | `RepositoriesFailed` |
  | t | f | f | `Critical` |
  | t | f | t | `Critical` |
  | t | t | f | `Critical` |
  | t | t | t | `Critical` |

- `BackupSummaryRendererTests.RenderAsync_SeveralStatuses_WritesTableTotalsAndLogPath` — полный snapshot.
- `BackupSummaryRendererTests.RenderAsync_RunStoppedWithConfigWarnings_WritesWarningsToOutputAndErrorsToErrorOutput`.
- `BackupSummaryRendererTests.RenderAsync_Duration_FormatsMinutesSecondsAndTenths` — при культуре `ru-RU`.

## 7. Вне рамок

- #29 dry-run: статусы `WouldArchive`/`WouldClone`, вывод «что было бы сделано», итоги dry-run. Заглушка `DryRunNotImplementedMessage` остаётся.
- Перечень событий обнаружения в сводке: FR-9.7 его не требует.
- Код возврата при отмене (NFR-4), код `2` за блокировку запуска, путь к логу для ошибок до старта хоста, сводка UI (FR-12.4).

## 8. Решения

- События обнаружения (добавлен, пропал, переименован) в сводке CLI не перечисляются: FR-9.7 их не требует. Альтернатива — изменить FR-9.7, чтобы код `3` был объясним по выводу.
- Итоги показывают и нулевые статусы (`Warning: 0`), формат сохраняется. Альтернатива — только ненулевые.
- Многострочные сообщения об ошибках отдельно не тестируются: перенос строк в ячейке — поведение Spectre.
- Три факта, которые повторяют строки новой теории кодов, удаляются (теория вместо копий фактов).

## 9. Задачи

- [x] `ExitCode.Critical`: дополнить `<summary>`
- [x] `BackupSummaryRenderer`: `RunStatuses` и итоги по нему
- [x] `BackupRunResultTests`: теория всех комбинаций, удалить поглощённые факты
- [x] `BackupSummaryRendererTests`: полный snapshot, удалить дублирующий тест
- [x] `BackupSummaryRendererTests`: остановленный запуск с предупреждениями
- [x] `BackupSummaryRendererTests`: теория длительностей
- [x] Сборка, тесты, `dotnet format --verify-no-changes`
