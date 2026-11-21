# План: F4.17: Live log shows the whole session log, with a Clear button

- Issue: [#159](https://github.com/askrinnik/GitHubBackup/issues/159)
- Lane: Feature
- Сложность: L
- Дата: 2026-11-20

## 1. Цель

Живая лента лога показывает весь лог сеанса приложения, а не только текущий запуск (FR-12.3; связаны FR-10.2, FR-10.6, FR-12.9, FR-12.10). Лента подписана на `ILiveLogFeed` всё время жизни главного окна. `LiveLogSink` хранит последние 5000 записей и без подписчиков. Новый подписчик сначала получает буфер, затем новые записи: без потерь, без дублей, по порядку. Запуск бэкапа ленту не очищает. В правом верхнем углу панели ленты стоит кнопка-значок «Clear log», она очищает ленту.

Находка: `RepositoriesViewModel` зарегистрирован как **Transient** (`AppServiceCollectionExtensions.AddGitHubBackupApp`). Переход в Settings или History и обратно создаёт новый экземпляр и с ним новый `RunLog`. Поэтому `RunLogViewModel` становится singleton в DI и сам владеет подпиской на всё время жизни главного окна. Иначе после возврата лента пересобиралась бы из буфера sink, и записи, очищенные кнопкой Clear, вернулись бы.

## 2. Критерии приёмки

Из issue:
- [x] PRD (FR-12.3) содержит текст из раздела «Изменение PRD»; версия PRD повышена, строка в истории изменений добавлена
- [x] Лента показывает записи старта приложения, записи до запуска и записи после него (обновление дерева после запуска)
- [x] Записи ленты совпадают с записями файла лога за тот же период (уровни, порядок, маскирование)
- [x] Запуск бэкапа ленту не очищает; лимит 5000 записей соблюдается
- [x] Кнопка Clear в правом верхнем углу панели ленты очищает ленту, не занимает строку и не закрывает полосу прокрутки; после очистки новые записи появляются
- [x] Кнопка выглядит правильно в темах Light и Dark
- [x] README (раздел о прогрессе и живой ленте) описывает новое поведение
- [ ] Видимые пункты проверены в запущенном приложении (netwright), скриншоты до и после в обеих темах
- [ ] Покрыто тестами (`LiveLogSink`: буфер и повтор для подписчика; ViewModel: подписка на весь сеанс, Clear; FlaUI: `ClearRunLogButton`), CI зелёный

Добавлено проверкой покрытия:
- (добавлено) Лента и её очистка переживают переход в Settings/History и обратно: очищенные записи не возвращаются, новые продолжают появляться.
- (добавлено) Clear во время запуска удаляет показанные и ожидающие записи. Записи, пришедшие после Clear, показываются, даже если перенос в UI-поток был запланирован до Clear.
- (добавлено) При выходе подписка снимается: singleton освобождается вместе с хостом, повторный `Dispose` ничего не делает.
- (добавлено) Ровно 5000 записей хранятся все, 5001-я вытесняет самую старую (sink и `RunLogViewModel`).
- (добавлено) Запись, пришедшая во время подписки с другого потока, доставляется ровно один раз и по порядку.
- (добавлено, по решению 9.1) В CLI буфера нет: без подписчиков запись не рендерится и не маскируется.

## 3. Затронутые проекты и типы

**GitHubBackup.Infrastructure**
- `Logging/LiveLogOptions.cs` (новый): `BufferCapacity` (int, по умолчанию 0, `[Range(0, 100_000)]`). Задаётся кодом хоста, из конфигурации не читается.
- `Logging/LiveLogSink.cs`: кольцевой буфер ёмкостью `BufferCapacity`. Повтор буфера при `Subscribe` и доставка новых записей идут под одним `Lock`. Конструктор: `ISecretMasker, IOptions<LiveLogOptions>`.
- `Logging/ILiveLogFeed.cs`: документация контракта — повтор буфера и правило «обработчик вызывается под блокировкой ленты и не должен блокироваться».
- `Logging/FileLoggingServiceCollectionExtensions.cs`: `AddGitHubBackupLogging` регистрирует `LiveLogOptions` (`ValidateDataAnnotations().ValidateOnStart()`); новый `AddGitHubBackupLiveLogBuffer(int capacity)` вызывает хост.

**GitHubBackup.App**
- `ViewModels/RunLogViewModel.cs`: `partial`, `IDisposable`. Конструктор `(IUiDispatcher, ILiveLogFeed)` подписывается на ленту, `Dispose` отписывается. `[RelayCommand]` на `Clear()` даёт `ClearCommand`.
- `ViewModels/RepositoriesViewModel.cs`: вместо `ILiveLogFeed` получает `RunLogViewModel`. Из `StartBackupAsync` удаляются `RunLog.Clear()`, `Subscribe` и `Dispose` подписки.
- `Hosting/AppServiceCollectionExtensions.cs`: `AddSingleton<RunLogViewModel>()`, `AddGitHubBackupLiveLogBuffer(RunLogViewModel.MaxEntries)`.
- `Views/RepositoriesView.xaml`: `ListBox` в `RunLogPanel` оборачивается в `Grid`; поверх списка — `Button` (`Right`/`Top`, правый отступ на ширину вертикального скроллбара), glyph `&#xE894;`, `FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets"`, кисть текста темы, `ToolTip="Clear log"`, `AutomationProperties.Name="Clear log"`, `AutomationProperties.AutomationId="ClearRunLogButton"`, `Command="{Binding RunLog.ClearCommand, Mode=OneWay}"`. Шаблонный размер Fluent `Button` не меняется, только `Padding`.
- `Views/RepositoriesView.xaml.cs` (по решению 9.3): на `Loaded` вызывается `ScrollToNewest`.

**Документация**
- `docs/PRD.md`: текст из issue, версия 0.52 → 0.53, строка в истории изменений.
- `README.md`, пункт **Progress**: лента всего сеанса, лимит 5000, запуск не очищает, кнопка Clear не трогает файлы лога и OTLP.

## 4. Подход

1. **PRD.** Перенести текст из `## Изменение PRD` в FR-12.3, версия 0.53, строка истории.
2. **Sink.** Один `Lock _gate` охраняет `Queue<LiveLogEntry> _buffer` и `ImmutableArray<Subscription> _subscriptions`.
   - `Emit`: при `BufferCapacity == 0` и без подписчиков — сразу выход (не рендерить, не маскировать). Иначе рендер и маскирование **вне** блокировки; затем под `_gate` запись в буфер (при переполнении — удалить самую старую) и вызов обработчиков из снимка подписок.
   - `Subscribe`: под `_gate` отдаёт обработчику буфер по порядку, затем добавляет подписку.
   - `Unsubscribe`: под `_gate`; после возврата `Dispose` доставок нет.
   - Доставка под блокировкой даёт строгий порядок и исключает потери и дубли на стыке повтора и новых записей. Обработчик `RunLogViewModel.Add` берёт короткий lock и делает `BeginInvoke`, он не блокируется. `System.Threading.Lock` реентерабелен: обработчик, который сам пишет в лог, не вызовет deadlock.
3. **Регистрация.** В `AddGitHubBackupLogging` ёмкость 0; WPF-хост включает 5000.
4. **RunLogViewModel.** Singleton, подписка в конструкторе. Первое создание — при создании главного окна; более ранние записи приходят из буфера. При выходе `using (host)` освобождает singleton, подписка снимается.
5. **RepositoriesViewModel.** Удаляет свою подписку и очистку при запуске.
6. **XAML.** Кнопка поверх списка; правый отступ не меньше ширины вертикального скроллбара Fluent, подтверждается скриншот-тестом. Отступ резервируется всегда, кнопка не прыгает при появлении скроллбара.
7. **README.**

## 5. Тесты

**GitHubBackup.Infrastructure.Tests — `Logging/LiveLogSinkTests`**
- Существующие тесты работают с ёмкостью 0; `Emit_NoSubscribers_DoesNotRenderOrMask` → `Emit_NoSubscribersNoBuffer_DoesNotRenderOrMask`.
- `Subscribe_EntriesBuffered_ReplaysOldestFirstThenDeliversNew`, `Subscribe_BufferedEntryWithSecret_ReplaysMasked`, `Emit_ExactlyCapacity_KeepsAll`, `Emit_BeyondCapacity_DropsOldest`, `Subscribe_NoBuffer_ReplaysNothing`, `Subscribe_TwoSubscribers_EachReceivesBuffer`, `Emit_AfterUnsubscribe_DoesNotDeliverButStillBuffers`, `Subscribe_WhileOtherThreadEmits_DeliversEveryEntryOnceInOrder`.
- `FileLoggingServiceCollectionExtensionsTests`: ёмкость по умолчанию 0, `AddGitHubBackupLiveLogBuffer` настраивает, отрицательное значение не проходит валидацию.

**GitHubBackup.Infrastructure.IntegrationTests — `SerilogFileLoggingTests`**
- `Log_BeforeSubscribeWithBuffer_FeedMatchesTextFileLevelsOrderAndMasking`.

**GitHubBackup.App.Tests**
- `RunLogViewModelTests`: `Constructor_SubscribesToFeed`, `Dispose_UnsubscribesOnce`, `ClearCommand_ShownAndWaiting_RemovesBoth`, `Clear_FlushPostedThenNewEntry_ShowsOnlyNewEntry`, `ClearCommand_CanExecute_AlwaysTrue`.
- `RepositoriesViewModelTests`: заменить `StartBackup_RunEnds_UnsubscribesAndKeepsEntries`, `StartBackup_RunFails_Unsubscribes`, `StartBackup_NewRun_ClearsLogOfPreviousRun` тестами «подписка сохраняется, лог не очищается»; добавить `RunLog_EntriesBeforeAnyRun_AreShown`, `RunLog_IsInjectedInstance`.
- `MainWindowViewModelTests`: передавать `RunLogViewModel`.
- `AppHostTests`: `RunLogViewModel` — singleton, экземпляры `RepositoriesViewModel` делят `RunLog`, ёмкость буфера = `MaxEntries`, после остановки хоста запись не доходит до ленты.

**GitHubBackup.App.UITests**
- `RunLogTests` (FlaUI): записи старта видны; `ClearRunLogButton` очищает ленту, затем после dry run записи появляются; очищенное состояние сохраняется после перехода в Settings и обратно.
- `MainWindowScreenshotTests`: кнопка в правом верхнем углу `RunLogPanel`, правый край не заходит на вертикальный `ScrollBar`, глиф виден целиком (`LayoutMeasurement.InkBounds`) и контрастен — в Light и Dark. Тест один раз прогоняется без отступа под скроллбар и должен упасть.

## 6. Проверка

| Пункт | Как проверяется |
|---|---|
| PRD | Дифф `docs/PRD.md`: текст совпадает с issue, версия 0.53, строка истории |
| Записи старта, до и после запуска | `RunLogViewModelTests`, `RepositoriesViewModelTests`, FlaUI `RunLogTests`, netwright |
| Совпадение с файлом лога | Интеграционный тест «лента = файл», тесты маскирования sink |
| Запуск не очищает, лимит 5000 | `RepositoriesViewModelTests`, `LiveLogSinkTests`, `RunLogViewModelTests`, `AppHostTests` |
| Clear: угол, без строки, не на скроллбаре, новые записи | FlaUI, скриншот-тест, `RunLogViewModelTests`, `MainWindowLayoutTests` зелёные |
| Light и Dark | Скриншот-тест в обеих темах |
| README | Дифф |
| netwright | Тест-стенд `missing-repository` в Light и Dark: записи старта, кнопка и подсказка, клик очищает, dry run добавляет записи, Settings → назад; скриншоты панели до и после |
| Тесты, CI | Сборка, тесты, UI- и скриншот-тесты, `dotnet format`, CI pull request |

## 7. Вне объёма

- Уровни, маскирование, формат строки, правила прокрутки при новой записи, высоты панелей.
- Буфер и лента в CLI (решение 9.1).
- Сохранение ленты между запусками приложения, фильтр, поиск, копирование ленты.

## 8. Открытые вопросы

1. **Буфер в CLI.** Рекомендация: буфер только в WPF-хосте (`BufferCapacity = 0` по умолчанию). В CLI нет подписчиков; буфер там только рендерил бы и маскировал каждую запись и держал до 5000 строк. FR-12.3 — требование к UI. Лимит задаётся в одном месте (`RunLogViewModel.MaxEntries`).
2. **Прокрутка после возврата из Settings/History.** Новый `RepositoriesView` привязывается к уже заполненной коллекции и стоит на самых старых записях. Предложение: на `Loaded` вызывать `ScrollToNewest` (только по вертикали).

## 9. Решения

1. Буфер ленты включён только в WPF-хосте: `LiveLogOptions.BufferCapacity = 0` по умолчанию, WPF вызывает `AddGitHubBackupLiveLogBuffer(RunLogViewModel.MaxEntries)`. CLI работает как прежде.
2. При открытии `RepositoriesView` (`Loaded`) лента прокручивается к последней записи только по вертикали (`ScrollToNewest`).
3. Пользователь одобрил план и попросил пройти workflow до конца: commit, профиль сеанса, push, комментарий в issue, pull request.

## 10. Задачи

- [x] PRD: текст из issue в FR-12.3, версия 0.53, строка истории
- [x] `LiveLogOptions`, регистрация с валидацией, `AddGitHubBackupLiveLogBuffer(int)`
- [x] `LiveLogSink`: `Lock`, кольцевой буфер, повтор в `Subscribe`, доставка под блокировкой, ранний выход
- [x] Документация `ILiveLogFeed.Subscribe`
- [x] Тесты sink, регистрации, интеграционный тест «лента = файл»
- [x] `RunLogViewModel`: singleton, подписка в конструкторе, `IDisposable`, `ClearCommand`
- [x] `RepositoriesViewModel`: внедрить `RunLogViewModel`, убрать подписку и очистку при запуске
- [x] `AddGitHubBackupApp`: singleton и буфер
- [x] Обновить тесты App (`RunLogViewModelTests`, `RepositoriesViewModelTests`, `MainWindowViewModelTests`, `AppHostTests`)
- [x] XAML: `ClearRunLogButton`; прокрутка к последней записи на `Loaded`
- [x] FlaUI `RunLogTests` и скриншот-тест кнопки в обеих темах
- [x] README: пункт **Progress**
- [x] Сборка, тесты, UI- и скриншот-тесты, `dotnet format`
- [ ] Проверка в запущенном приложении: функциональные пункты проверены через UI Automation на тест-стенде (netwright не вернул данные); скриншоты «после» в Light и Dark ждут разблокированного экрана
