# План: #49 «F4.8: WPF automation for harness verification»

| Поле | Значение |
|---|---|
| Issue | [#49](https://github.com/askrinnik/GitHubBackup/issues/49) |
| Название | F4.8: WPF automation for harness verification |
| Полоса | Feature (метка `type:chore`; меняются конфигурация и документация AI Harness) |
| Сложность | M |
| Дата | 2026-11-13 |

## 1. Цель

Ассистент может сам проверить WPF-приложение на шаге проверки рабочего цикла: запустить `GitHubBackup.App`, прочитать окно как текст, найти элементы по `AutomationId`, выполнить действие и проверить результат. Для этого в `.mcp.json` подключается MCP-сервер Netwright. В бенчмарке появляется фикстура `bench-xaml`.

Текст issue устарел в двух местах:

- `docs/ai-harness.md` теперь называется `docs/ai/README.md`;
- `.ai/prompts/implement-issue.md` больше нет: рабочий цикл живёт в плагине `issue-kit`. Указание для шага проверки UI-задач уходит в проектное правило `.claude/rules/wpf-mvvm.md`. Оно загружается, когда ассистент читает файлы `GitHubBackup.App` или `*.xaml`, то есть как раз в UI-задачах.

## 2. Критерии приёмки

1. MCP-сервер подключён, выбор обоснован в `docs/ai/README.md` (в issue — `docs/ai-harness.md`)
2. Проверка UI показана на примере окна из #36
3. Фикстура `bench-xaml` добавлена, первый прогон записан
4. *(добавлено)* Шаг проверки UI-задач описан в проектном правиле `.claude/rules/wpf-mvvm.md` (в issue — `.ai/prompts/implement-issue.md`, которого больше нет)
5. *(добавлено)* `.ai/benchmarks/harness/README.md` и `docs/ai/README.md` описывают фикстуру и сервер; строка #49 убрана из «Отложенные задачи», фраза «MCP для автоматизации WPF выбирается в #49» убрана
6. *(добавлено)* Текст issue #49 обновлён под новые пути после документов (с согласия пользователя)

## 3. Изменения PRD

Нет: задача относится к харнессу (#1), а не к требованиям продукта.

## 4. Выбор сервера

Сравнивались `ricfanin/netwright` и `shanselman/FlaUI-MCP`. Выбран Netwright (решение пользователя).

| Критерий | Netwright 2.1.0 | FlaUI-MCP 0.2.1 |
|---|---|---|
| Установка | пакет NuGet, `dnx` из .NET 10 SDK, как сервер `nuget` | zip из GitHub Releases, нужен .NET 8 runtime |
| Запуск приложения | из `.csproj` (собирает и запускает) | по пути к `.exe` |
| Действия | в фоне, без захвата мыши и фокуса | реальным вводом |
| Ограничение приложений | `--allow GitHubBackup*` | нет |
| Токены (бенчмарк автора Netwright) | описания инструментов ≈1.4k, снимок формы ≈0.4k | ≈1.4k и ≈0.8k |
| Логи и падения | stdout/stderr, `Trace`, исключение со стеком | нет |
| Лицензия | MIT | MIT |

Оговорка: цифры токенов взяты из бенчмарка автора Netwright, а не измерены здесь. Свою цену в контексте покажет `bench-base` после подключения.

## 5. Файлы

| Файл | Что |
|---|---|
| `.mcp.json` | Сервер `netwright`: `cmd /c dnx Netwright@2.1.0 --yes -- --allow GitHubBackup*` (версия закреплена, см. «Открытые вопросы») |
| `.claude/settings.json` | **Правит пользователь** (ассистенту автоматический режим это запрещает): `"netwright"` в `enabledMcpjsonServers`, `"mcp__netwright"` в `permissions.allow` |
| `.claude/rules/wpf-mvvm.md` | Раздел `## Verifying the running app`: как проверять UI-задачу через Netwright |
| `.ai/benchmarks/harness/fixtures/bench-xaml/prompt.txt` | `bench-xaml: read src/GitHubBackup.App/Views/MainWindow.xaml with the Read tool, then answer with one word: done.` |
| `.ai/benchmarks/harness/fixtures/bench-xaml/fixture.json` | Как у `bench-cs`: `repetitions` 1, `tools`/`allowedTools` `Read`, `startContext` false |
| `.ai/benchmarks/harness/README.md` | Строка `bench-xaml` в таблице; строка про отложенную фикстуру #49 убрана |
| `docs/ai/README.md` | Строка `netwright` в таблице MCP, абзац с обоснованием выбора, раздел про проверку UI, строка `bench-xaml`; строка #49 и фраза «выбирается в #49» убраны; раздел «Отложенные задачи» убран, если в нём больше ничего нет |
| `.ai/benchmarks/harness/{run-history.csv,runs/*,reports/*}` | Результаты прогона (пишет скрипт) |
| `docs/plans/chore-49-wpf-automation-harness-verification.md` | Этот план; раздел «Проверка» с показом окна из #36 |

## 6. Подход

**Правило `wpf-mvvm.md`, новый раздел (английский, NFR-7).** Суть:

- In the verify step of an issue that changes a window or view, check its visible acceptance items in the running app through the `netwright` MCP server, in addition to the ViewModel and FlaUI tests.
- Launch with `desktop_app action=launch project=src/GitHubBackup.App/GitHubBackup.App.csproj`; read with `desktop_snapshot`; target elements by AutomationId selectors (`#MainContent`); assert with `desktop_expect`; read `desktop_logs` on a failure; close the app with `desktop_app action=close` when done.
- Prefer snapshots and Change Reports to screenshots; take `desktop_screenshot` only when layout matters.
- Never point the app at real repositories or a real token during verification: use a temporary content root, as the tests do.
- The check does not replace a UI test: a flow worth keeping becomes a FlaUI test in `GitHubBackup.App.UITests`.

Последний пункт оставляет FlaUI для регрессионных тестов: `Netwright.Testing` и `desktop_export_test` не используются, слой UI-тестов уже выбран (`tests.md`).

**Фикстура `bench-xaml`.** Как `bench-cs` и `bench-test`: читает стабильный файл `MainWindow.xaml` и измеряет, сколько токенов добавляют чтение и правило по путям `wpf-mvvm.md` (теперь с новым разделом). Фикстура не запускает приложение: бенчмарк должен быть стабильным и дешёвым.

**Показ проверки на окне из #36.** Эта сессия не может подгрузить новый MCP-сервер без перезапуска. Поэтому показ идёт отдельной сессией без интерфейса из корня репозитория:

```
claude -p "<prompt>" --mcp-config .mcp.json --strict-mcp-config --allowedTools "mcp__netwright__*"
```

Запрос: собрать и запустить `GitHubBackup.App`, снять снимок окна, проверить заголовок `GitHub Backup`, наличие `#MainContent` и `#HomeMessage` и текст сообщения, закрыть приложение. Вывод (вызовы и снимок) записывается в раздел «Проверка» этого плана и в комментарий к issue. Перед запуском `dnx` скачивает пакет `Netwright` 2.1.0 из nuget.org (согласие получено).

**Порядок.** `.mcp.json` и правило → показ проверки → документация → пользователь правит `settings.json` → прогон `bench-xaml` и `bench-base` (`bench-base` показывает цену инструментов Netwright в стартовом контексте) → обновление текста issue.

## 7. Проверка

| # | Пункт | Как проверить |
|---|---|---|
| 1 | Сервер подключён, выбор обоснован | `claude mcp list` или показ проверки запускает `netwright`; разделы в `docs/ai/README.md` |
| 2 | Проверка UI на окне из #36 | вывод сессии `claude -p` в разделе «Проверка» |
| 3 | `bench-xaml` и первый прогон | `Run-Harness.ps1 -DryRun`, затем прогон; строки в `run-history.csv`, отчёт в `reports/` |
| 4 | Правило | раздел в `wpf-mvvm.md`; `bench-xaml` показывает, что правило загружено (`RulesLoaded`) |
| 5 | Документы | `git diff` документов; `Select-String "#49"` по `docs/ai/README.md` и README бенчмарка ничего не находит |
| 6 | Issue | `gh issue view 49` после правки |

Сборка, тесты и формат проходят через `issue-kit:build-runner`, хотя код не меняется (шлюз обязателен).

### Показ проверки на окне из #36 (2026-11-13)

Сессия `claude -p --mcp-config .mcp.json --strict-mcp-config --allowedTools "mcp__netwright__*"` из корня репозитория, вызовы по одному:

| # | Вызов | Результат в Claude Code |
|---|---|---|
| 1 | `desktop_app action=launch project=src/GitHubBackup.App/GitHubBackup.App.csproj` | `{"ok":true}` |
| 2 | `desktop_expect target=#MainWindow assertion=exists` | `{"ok":true}` |
| 3 | `desktop_expect target=#HomeMessage assertion=visible` | `{"ok":true}` |
| 4 | `desktop_expect target=#HomeMessage assertion=text_contains expected="GitHub Backup is ready"` | `{"ok":true}` |
| 5 | `desktop_expect target=#HomeMessage assertion=text_contains expected="this text is not in the window"` (намеренный провал) | `error EXPECTATION_FAILED: … but its text is "GitHub Backup is ready. The screens of the application are not available yet."` |
| 6 | `desktop_screenshot` | изображение окна «GitHub Backup» со строкой сообщения |
| 7 | `desktop_app action=close` | `{"ok":true}` |

Находка: Netwright 2.1.0 отвечает текстом и `structuredContent: {"ok":true}`, а Claude Code показывает только `structuredContent` ([ricfanin/netwright#1](https://github.com/ricfanin/netwright/issues/1), пункт 01). Прямой запрос к серверу по JSON-RPC показал полный текст, например снимок:

```
- window "GitHubBackup.App.ViewModels.HomeViewModel" [e1] #MainWindow
  - custom "GitHub Backup is ready. The screens of the application are not available yet." [e2]
```

Отсюда обход в правиле: проверки через `desktop_expect` и скриншоты (решение пользователя).

Две находки про окно из #36 (вне объёма #49): имя окна в UI Automation — тип ViewModel (`GitHubBackup.App.ViewModels.HomeViewModel`), а не заголовок `GitHub Backup`; `ContentControl` с `AutomationId="MainContent"` в дереве UI Automation не виден (у `ContentControl` нет automation peer).

## 8. Вне объёма

- Регрессионные UI-тесты на `Netwright.Testing`: слой UI-тестов — FlaUI.
- Фикстура, которая запускает приложение через MCP: дорогая и нестабильная для бенчмарка.
- Окна #37–#39: их проверят задачи этих окон по новому правилу.

## 9. Открытые вопросы

1. **Закрепить версию Netwright?** Предлагаю `Netwright@2.1.0`: сервер получает доступ к UI Automation, и новая версия не должна приходить молча. Сервер `nuget` сейчас не закреплён; его не трогаю.
2. **Разрешение `mcp__netwright` без вопросов?** Предлагаю да: `--allow GitHubBackup*` не даёт трогать другие приложения. Правку `settings.json` делаете вы.
3. **Убрать раздел «Отложенные задачи» целиком**, если #49 — его единственная строка? Предлагаю да: документ описывает то, что есть.

## 10. Решения

- Сервер — Netwright; согласие на скачивание и запуск пакета получено.
- Версия закреплена: `Netwright@2.1.0`.
- `mcp__netwright` разрешён без вопросов; пользователь сам внёс `netwright` в `enabledMcpjsonServers` и `permissions.allow` файла `.claude/settings.json`.
- Раздел «Отложенные задачи» в `docs/ai/README.md` убирается целиком, если в нём больше ничего нет.

## 11. Задачи

- [x] `.mcp.json`: сервер `netwright`
- [x] `.claude/rules/wpf-mvvm.md`: раздел о проверке запущенного приложения
- [x] Показ проверки на окне из #36, вывод в раздел «Проверка»
- [x] Фикстура `bench-xaml`
- [x] `docs/ai/README.md`, `.ai/benchmarks/harness/README.md`
- [x] Пользователь: `settings.json`
- [x] Прогон `bench-*` (бенчмарк запускает сессии без MCP-серверов, поэтому цену инструментов Netwright в стартовом контексте `bench-base` не показывает)
- [x] Сборка, тесты, формат
- [x] Обновление текста issue #49
- [x] Follow-up: снятие обхода Netwright после исправления бага — #106
