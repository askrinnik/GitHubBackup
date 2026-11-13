# План: #48 «F3.11: Harness benchmark fixture for documentation sync»

| Поле | Значение |
|---|---|
| Issue | [#48](https://github.com/askrinnik/GitHubBackup/issues/48) |
| Название | F3.11: Harness benchmark fixture for documentation sync |
| Полоса | Feature (метка `type:test`, но меняется конфигурация AI Harness, а не тесты существующего поведения; как в #47) |
| Сложность | S |
| Дата | 2026-11-13 |

## 1. Цель

В бенчмарке харнесса появляется фикстура `quality-docs-sync`. Её `seed.patch` добавляет в код параметр CLI и настройку `appsettings.json`, которых нет в документации. Ассистент должен назвать документы, которые нужно обновить в том же изменении, и не назвать лишних. Судья оценивает ответ по `ground-truth.md`. Источник требований — AI Harness (#1); документ `docs/ai-harness.md` из issue теперь называется `docs/ai/README.md`.

## 2. Критерии приёмки

1. Фикстура проходит предварительную проверку (`Run-Harness.ps1 -DryRun`)
2. Первый прогон записан в `run-history.csv`, отчёт закоммичен
3. *(добавлено)* `seed.patch` применяется к текущему дереву и откатывается без следа (`git status` после прогона показывает только результаты)
4. *(добавлено)* Фикстура описана в `.ai/benchmarks/harness/README.md` и `docs/ai/README.md`; строка #48 убрана из отложенных задач

## 3. Изменения PRD

Нет: задача относится к харнессу. Патч фикстуры намеренно не описан в PRD и в репозиторий не попадает (применяется только на время сессии).

## 4. Файлы

| Файл | Что |
|---|---|
| `.ai/benchmarks/harness/fixtures/quality-docs-sync/prompt.txt` | Запрос: какие документы обновить вместе с изменением `{{PATCH_FILES}}` |
| `.ai/benchmarks/harness/fixtures/quality-docs-sync/fixture.json` | `repetitions` 3, `tools`/`allowedTools` `Read,Grep,Glob`, `startContext` false |
| `.ai/benchmarks/harness/fixtures/quality-docs-sync/seed.patch` | Новый параметр и новая настройка (ниже) |
| `.ai/benchmarks/harness/fixtures/quality-docs-sync/ground-truth.md` | Эталон и правила оценки |
| `.ai/benchmarks/harness/README.md` | Строка фикстуры в таблице, строка про отложенные фикстуры (остаётся #49) |
| `docs/ai/README.md` | Строка фикстуры в таблице, строка #48 убрана из «Отложенные задачи» |
| `.ai/benchmarks/harness/{run-history.csv,runs/*,reports/*}` | Результаты первого прогона (пишет скрипт) |

## 5. Подход

**`seed.patch`** (строится по текущему коду: правка, `git diff`, откат; проверка `git apply --check`):

- `src/GitHubBackup.Cli/CliCommandLine.cs` — параметр `backup --no-color` («Write plain console output without colors.»), добавлен в `backup.Options`. Поведение — только вывод консоли, поэтому в PRD его место одно: FR-9.2.
- `src/GitHubBackup.Core/Configuration/BackupOptions.cs` — свойство `CompressionLevel` (по умолчанию `5`) и константы `MinCompressionLevel = 0`, `MaxCompressionLevel = 9`, с XML-документацией.
- `src/GitHubBackup.Core/Configuration/BackupOptionsValidator.cs` — правило «`Backup:CompressionLevel` must be between 0 and 9».
- `src/GitHubBackup.Cli/appsettings.json` — `"CompressionLevel": 5` в секции `Backup`.

Патч не подключает параметр и настройку к поведению: фикстура проверяет синхронизацию документов, а не реализацию.

**`prompt.txt`:** «The working tree holds an uncommitted change to these files: {{PATCH_FILES}}. It adds a new option of the backup command and a new application setting. Which documents of this repository must be updated in the same change, and what must change in each? Name only documents that really need an update. Do not edit any files.»

**`ground-truth.md`** (Max 4):

| # | Пункт |
|---|---|
| 1 | `docs/PRD.md`, FR-9.2: строка `--no-color` в таблице параметров `backup` |
| 2 | `docs/PRD.md`, §7.2: ключ `Backup:CompressionLevel` в примере `appsettings.json` и правило проверки при старте (0–9) |
| 3 | `docs/PRD.md`: новая версия в шапке и строка в «История изменений документа» (§14); упоминание правила PRD-first (изменение PRD должно быть согласовано до кода) засчитывается как плюс, но не требуется |
| 4 | `README.md`: строка `--no-color` в таблице команд раздела «Консольное приложение» (и перечень параметров в строке «Статус») |

Частично (0.5): документ назван без нужного места (например, «PRD» без FR-9.2 или без §7.2 в одном пункте).

Ложные срабатывания — документ без реальной причины меняться: `CLAUDE.md`, `docs/ai/README.md`, `.claude/rules/*`, существующие планы в `docs/plans/`, шаблоны issues и PR, `.ai/benchmarks/**`; также утверждения, что README описывает ключи `appsettings.json` по одному (не описывает). Реальные дополнительные находки (настройка не используется в `SevenZipArchiver`, нет тестов валидатора) — не ложные срабатывания.

**Прогон.** `Run-Harness.ps1 -Fixture 'quality-docs-sync' -Note "Fixture quality-docs-sync"`: 3 сессии и 3 оценки судьи, около 5 минут, расходует часть недельного лимита плана. `Dirty` в истории будет `yes` (план и документы не закоммичены).

## 6. Тесты

Кода решения нет. Проверка — `git apply --check`, `-DryRun`, первый прогон и полный `build-runner` (решение не меняется).

## 7. Проверка

| Критерий | Как |
|---|---|
| 1 | Вывод `Run-Harness.ps1 -DryRun -Fixture quality-docs-sync` |
| 2 | Строки в `run-history.csv`, файл отчёта в `reports/`; коммит — на шаге 12 |
| 3 | `git status --short` после прогона: только результаты и файлы фикстуры |
| 4 | Строки в `.ai/benchmarks/harness/README.md` и `docs/ai/README.md` |

## 8. Вне рамок

- Фикстура #49 (`bench-xaml`).
- Изменения скриптов бенчмарка.
- Патч нужно обновлять, когда меняются `CliCommandLine.cs`, `BackupOptions.cs`, `BackupOptionsValidator.cs` или `appsettings.json` (отметка в `ground-truth.md` и в таблице фикстур).

## 9. Решения

- Документ из issue `docs/ai-harness.md` → `docs/ai/README.md` (переименован ранее).
- Полоса Feature: метка `type:test`, но тестов существующего поведения нет.
- Пользователь утвердил план как есть: параметр `--no-color` и настройка `Backup:CompressionLevel`; первый прогон — только `quality-docs-sync`, 3 повтора.
- Первый прогон: 4/4 во всех трёх повторах, ложных срабатываний нет. Это потолок: фикстура ловит ухудшение, но не покажет улучшение харнесса.

## 10. Задачи

- [x] `seed.patch`, `prompt.txt`, `fixture.json`, `ground-truth.md`
- [x] `git apply --check`, `-DryRun`
- [x] Документация
- [x] Первый прогон
- [x] `build-runner`
