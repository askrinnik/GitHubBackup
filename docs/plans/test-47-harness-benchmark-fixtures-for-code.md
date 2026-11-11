# План: #47 «F1.13: Harness benchmark fixtures for code»

| Поле | Значение |
|---|---|
| Issue | [#47](https://github.com/askrinnik/GitHubBackup/issues/47) |
| Название | F1.13: Harness benchmark fixtures for code |
| Полоса | Feature (метка `type:test`, но меняется конфигурация AI Harness, а не тесты существующего поведения) |
| Сложность | M |
| Дата | 2026-11-11 |

## 1. Цель

В бенчмарке харнесса (`.ai/benchmarks/harness/fixtures/`) появляются четыре фикстуры, которым нужен код решения: две измеряют стоимость правил по путям при чтении файла (`bench-cs`, `bench-test`), две оценивают качество ответа по эталону (`quality-service-convention`, `quality-security`). Первый прогон записан в `run-history.csv`, отчёт лежит в `reports/`. Источник требований — AI Harness (#1), а не PRD продукта; документ `docs/ai-harness.md` из issue теперь называется `docs/ai/README.md`.

## 2. Критерии приёмки

1. Фикстуры проходят предварительную проверку (`Run-Harness.ps1 -DryRun`)
2. Первый прогон записан в `run-history.csv`, отчёт закоммичен
3. Фикстуры описаны в `docs/ai-harness.md` (теперь `docs/ai/README.md`; также таблица в `.ai/benchmarks/harness/README.md`)
4. *(добавлено)* `seed.patch` фикстуры `quality-security` применяется к текущему дереву и откатывается без следа (`git status` чистый после прогона)
5. *(добавлено)* `bench-cs` загружает правила `csharp` и `security`, `bench-test` — `csharp` и `tests` (колонка `RulesLoaded`)

## 3. Изменения PRD

Нет: задача относится к харнессу.

## 4. Файлы

| Файл | Что |
|---|---|
| `.ai/benchmarks/harness/fixtures/bench-cs/{prompt.txt,fixture.json}` | Чтение `src/GitHubBackup.Core/ExitCode.cs`; инструменты `Read` |
| `.ai/benchmarks/harness/fixtures/bench-test/{prompt.txt,fixture.json}` | Чтение `src/GitHubBackup.Core.Tests/Processes/ProcessResultTests.cs`; инструменты `Read` |
| `.ai/benchmarks/harness/fixtures/quality-service-convention/{prompt.txt,fixture.json,ground-truth.md}` | Запрос на сервис очистки `<sourcesRoot>\.tmp\` (FR-7.4, §9 шаг 4), оценка по чеклисту |
| `.ai/benchmarks/harness/fixtures/quality-security/{prompt.txt,fixture.json,seed.patch,ground-truth.md}` | Ревью безопасности файлов патча, 7 подброшенных уязвимостей |
| `.ai/benchmarks/harness/README.md` | Таблица фикстур, строка про отложенные фикстуры |
| `docs/ai/README.md` | Список фикстур, строка #47 в таблице отложенных задач |
| `.ai/benchmarks/harness/{run-history.csv,runs/*,reports/*}` | Результаты первого прогона (пишет скрипт) |

## 5. Подход

**Стабильные файлы для `bench-*`.** Небольшие файлы, которые не менялись с момента создания: `ExitCode.cs` (enum кодов выхода, FR-9.8, 23 строки) и `ProcessResultTests.cs` (21 строка). Промпт: прочитать файл и ответить одним словом. `tools` и `allowedTools` = `Read`, `repetitions` = 1, `startContext` = false, как требует скилл для фикстур с чтением файла.

**`quality-service-convention`.** Промпт описывает требование FR-7.4 (очистка `<sourcesRoot>\.tmp\` после захвата блокировки) и просит показать сервис: типы, файлы, регистрацию, тест, без правки файлов. Требование есть в PRD, поэтому ответ не упирается в PRD-first. Инструменты `Read,Grep,Glob`, 3 повтора. Чеклист `ground-truth.md` (Max 5):
1. интерфейс в `Core`, реализация в нижнем слое, который может её держать (`Core` через `IFileSystem` или `Infrastructure`), хост не содержит логики;
2. регистрация в DI в методе слоя (`AddInfrastructure`/расширение слоя), не в `Program`;
3. диск через `IFileSystem`, время (если нужно) через `TimeProvider`; нет статических `File`/`Directory`/`DateTime.Now`;
4. XML-документация у типов и членов, `<inheritdoc />` у реализации;
5. тест в парном тестовом проекте (`MockFileSystem` или `TemporaryContentRoot`), имена по правилам `tests.md`.

Ложные срабатывания: утверждения о несуществующих типах решения как о существующих.

**`quality-security`.** `seed.patch` меняет 5 файлов; промпт просит ревью безопасности `{{PATCH_FILES}}` без правки. Инструменты `Read,Grep,Glob`, 3 повтора. Уязвимости (`ground-truth.md`, Max 7):

| # | Файл, место | Уязвимость | Правило |
|---|---|---|---|
| 1 | `GitClient.GetRemoteRefsAsync` | заголовок авторизации в аргументах `git -c …extraHeader=…` | S1, FR-11.3 |
| 2 | `GitClient.CloneAsync` | URL с токеном пишется в лог без маскирования | S1, FR-10.6 |
| 3 | `GitClient.CloneAsync` | токен в URL remote (`https://x-access-token:<token>@…`) → `.git/config` и архив | S1, NFR-1 |
| 4 | `GitEnvironment.Create` | нет `GIT_TERMINAL_PROMPT=0` | FR-11.2, P1 |
| 5 | `RepositoryPathResolver.GetCloneFolder` | путь из имени владельца и репозитория без проверки имён и границ корня | I1 |
| 6 | `GitClient.ValidateBranch` + `UpdateAsync` | ветка с `-` больше не отклоняется и стоит в `git fetch origin <branch>` без `--` | I2 |
| 7 | `SevenZipArchiver.Validate` | имя файла архива не проверяется → `..\` выводит архив за пределы папки архивов | I1/F |

Патч строится по текущему коду (`git diff` правки, затем откат), проверяется `git apply --check` и `-DryRun`.

**Прогон.** `Run-Harness.ps1 -Fixture 'bench-cs,bench-test,quality-*-convention,quality-security' -Note "Fixtures for code: bench-cs, bench-test, quality-service-convention, quality-security"` (без `quality-prd-first`, чтобы не тратить лимит). Около 12 минут, использует часть лимита плана. `Dirty` в истории будет `yes` (план и документы не закоммичены).

## 6. Тесты

Кода решения нет. Проверка — `-DryRun`, `git apply --check`, первый прогон и полный `build-runner` (решение не должно измениться).

## 7. Проверка

| Критерий | Как |
|---|---|
| 1 | Вывод `Run-Harness.ps1 -DryRun` для 4 фикстур |
| 2 | Строки в `run-history.csv`, файл отчёта в `reports/`; коммит — на шаге 12 |
| 3 | Разделы в `docs/ai/README.md` и `.ai/benchmarks/harness/README.md` |
| 4 | `git status --short` после прогона: только результаты |
| 5 | Колонка `RulesLoaded` строк `bench-cs` и `bench-test` |

## 8. Вне рамок

- Фикстуры #48 (`docs`) и #49 (`bench-xaml`).
- Когда #32 реализует очистку `.tmp`, промпт `quality-service-convention` ссылается на существующий код; фикстуру тогда нужно перенацелить (отметка в `ground-truth.md`).
- Изменения скриптов бенчмарка.

## 9. Решения

- Документ из issue `docs/ai-harness.md` → `docs/ai/README.md` (переименован ранее).
- Полоса Feature: метка `type:test`, но тестов существующего поведения нет.
- Открытых вопросов на ревью плана не было; пользователь заранее разрешил переходить к реализации.
- Первый прогон: `quality-security` 7/7 во всех трёх повторах. Это потолок: фикстура ловит ухудшение, но не покажет улучшение харнесса.

## 10. Задачи

- [x] Фикстуры `bench-cs`, `bench-test`
- [x] Фикстура `quality-service-convention`
- [x] Фикстура `quality-security` (`seed.patch`, `ground-truth.md`)
- [x] `-DryRun`
- [x] Документация
- [x] Первый прогон
- [x] `build-runner`
