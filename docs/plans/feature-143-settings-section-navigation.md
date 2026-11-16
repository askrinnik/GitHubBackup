# План: #143 F4.14: редактор настроек с навигацией по разделам и master-detail для аккаунтов

| Поле | Значение |
|---|---|
| Issue | [#143](https://github.com/askrinnik/GitHubBackup/issues/143) |
| Название | F4.14: Settings editor with section navigation and account master-detail |
| Лейн | Feature |
| Сложность | L |
| Дата | 2026-11-15 |

## 1. Цель

Сейчас все группы настроек стоят на одной странице внутри общего `ScrollViewer`. Редактор нужно разделить на шесть разделов: «General», «Accounts», «Repositories», «Tools», «Telemetry» и «GitHub token». Навигация стоит слева, как в Windows 11 Settings, выбранный раздел показан справа. Общей прокрутки нет. Таблицы репозиториев занимают оставшуюся высоту раздела, прокручиваются сами, сохраняют виртуализацию и получают фильтр по части имени. Раздел с ошибкой отмечен значком в навигации. Save с ошибками открывает первый раздел с ошибкой. Содержимое полей, правила проверки и правила записи не меняются. Требования: FR-12.5 и FR-12.9 (§6.12). PRD повышается до 0.43.

## 2. Критерии приёмки

Из issue (дословно):

- PRD (FR-12.5, FR-12.9) описывает разделы и навигацию; версия PRD повышена, строка в истории изменений добавлена
- Редактор показывает навигацию слева и один раздел справа; при открытии выбран «General»
- Колесо мыши над таблицей репозиториев прокручивает только таблицу; вложенной прокрутки нет
- «Accounts»: список аккаунтов и детали выбранного с таблицей репозиториев и фильтром; добавление и удаление аккаунта работают
- «Repositories»: таблица отдельных репозиториев с фильтром; добавление и удаление работают
- Раздел с ошибкой отмечен в навигации; Save с ошибками открывает первый раздел с ошибкой
- Save и Cancel работают для всех разделов, как до изменения
- README описывает новый вид редактора, если описывает старый
- Видимые пункты проверены в запущенном приложении (netwright)
- Покрыто тестами (ViewModel и FlaUI), CI зелёный

Добавлено проверкой покрытия:

- (добавлено) Аккаунт с ошибкой отмечен значком в списке аккаунтов. Если ошибка в аккаунте, Save выбирает первый аккаунт с ошибкой и очищает фильтр его таблицы.
- (добавлено) Нет аккаунтов: детали показывают сообщение `No accounts. Add an account.`, кнопка удаления не видна.
- (добавлено) Фильтр не нашёл ни одной строки: показано `No repositories match the filter.`. Пустой фильтр или фильтр из пробелов показывает все строки в порядке файла.
- (добавлено) Если фильтр активен, «Add repository» очищает фильтр, и новая пустая строка видна: таблица прокручивается к ней.
- (добавлено) Удаление выбранного аккаунта выбирает соседний: следующий, а если его нет, то предыдущий. Удаление последнего аккаунта сбрасывает выбор.
- (добавлено) Если `backup-config.json` не читается (`IsLoaded=false`), навигация работает. Поля конфигурации в «General», «Accounts» и «Repositories» недоступны, как сейчас. Тема, «Tools», «Telemetry» и «GitHub token» доступны.
- (добавлено) Ошибки без своего поля (`GeneralErrors`) видны над Save и Cancel в любом разделе. Раздел они не отмечают и не переключают.

## 3. Изменения требований

Шапка PRD: `Версия документа` = `0.43 (черновик)`, `Дата` = `2026-11-15`.

**FR-12.5.** Первая фраза заменяется. Перед существующими подпунктами добавляются два подпункта:

> - **FR-12.5. Редактор настроек.** Настройки разделены на разделы. Слева редактор показывает навигацию по разделам, справа — один выбранный раздел: «General» (корни, исключения, `discoverAccessibleRepositories`, тема), «Accounts» (список аккаунтов и детали выбранного аккаунта: адрес, папка архивов, исключения, таблица его репозиториев), «Repositories» (таблица отдельных репозиториев), «Tools», «Telemetry» и «GitHub token». При каждом открытии редактора выбран раздел «General».
>   - **Прокрутка.** У редактора нет общей прокрутки: прокручивается только содержимое раздела, и прокручиваемый список не стоит внутри другой прокручиваемой области. Таблицы репозиториев занимают оставшуюся высоту раздела и прокручиваются сами. Над каждой таблицей стоит поле фильтра: остаются строки, имя которых (у отдельного репозитория — адрес) содержит введённый текст без учёта регистра. Добавление репозитория очищает фильтр.
>   - **Ошибки в разделах.** Раздел, в поле которого есть ошибка, отмечен значком в навигации; аккаунт с ошибкой отмечен значком в списке аккаунтов. Если Save находит ошибки, редактор открывает первый по порядку навигации раздел с ошибкой, а в «Accounts» — первый аккаунт с ошибкой. Ошибки, которые не относятся ни к одному полю, редактор показывает над кнопками Save и Cancel в любом разделе.

В подпункте **«Сохранение»** меняются две фразы:

- «Одна кнопка Save внизу редактора сохраняет все изменённые группы» → «Одна кнопка Save внизу редактора, общая для всех разделов, сохраняет все изменённые группы».
- «Отдельных кнопок сохранения у групп нет» → «Отдельных кнопок сохранения у разделов нет».

Слово «группа» в смысле хранилища (конфигурация, пути, телеметрия, токен) остаётся.

**FR-12.9:** «Список «Theme» в группе «General» редактора настроек» → «Список «Theme» в разделе «General» редактора настроек».

**§14, новая строка после 0.42:**
`| 0.43 | 2026-11-15 | FR-12.5: редактор настроек разделён на разделы с навигацией слева (General, Accounts, Repositories, Tools, Telemetry, GitHub token), при открытии выбран «General»; нет общей прокрутки, таблицы репозиториев прокручиваются сами и фильтруются по части имени; «Accounts» — список аккаунтов и детали выбранного; раздел и аккаунт с ошибкой отмечены, Save с ошибками открывает первый раздел с ошибкой. FR-12.9: «группа» → «раздел» «General». |`

## 4. Затронутые проекты и типы

**GitHubBackup.App**

- `ViewModels/SettingsSection.cs` (новый): enum `General, Accounts, Repositories, Tools, Telemetry, Token` в порядке навигации.
- `ViewModels/SettingsSectionItem.cs` (новый, `ObservableObject`): `Section`, `Title`, `AutomationId` (`SettingsSection<Section>`), `[ObservableProperty] IsSelected`, `[ObservableProperty] HasErrors`.
- `ViewModels/FilteredItems.cs` (новый, `ObservableObject`, generic `FilteredItems<T>`): видимое подмножество `ObservableCollection<T>` по ключу `Func<T,string>`. Состав: `[ObservableProperty] Filter`, `ObservableCollection<T> Items`, `HasNoMatch`. Подписка на `CollectionChanged` источника. Типов WPF нет.
- `ViewModels/SettingsViewModel.cs` (изменён): `Sections` и именованные `GeneralSection`…`TokenSection`; `[ObservableProperty] SelectedSection`, по умолчанию General; `[ObservableProperty] SelectedAccount` и `HasSelectedAccount`; `RepositoryView` (`FilteredItems<RepositorySettingsViewModel>`, ключ `Url`). Пересчёт ошибок разделов, выбор первого раздела с ошибкой в `SaveAsync`, правила выбора аккаунта в `Populate`, `AddAccount` и `RemoveAccount`, очистка фильтра в `AddRepository`.
- `ViewModels/AccountSettingsViewModel.cs` (изменён): `DisplayName` (URL без пробелов или `(new account)`, `[NotifyPropertyChangedFor]` от `Url`); `HasAnyErrors` (своя ошибка или ошибка репозитория) с `internal RefreshHasAnyErrors()`; `RepositoryView` (`FilteredItems<AccountRepositorySettingsViewModel>`, ключ `Name`).
- `Behaviors/ScrollToAddedItem.cs` (новый): attached behavior для `ItemsControl`. Когда в `ItemsSource` добавлен элемент в конце, behavior прокручивает внутренний `ScrollViewer` к концу. Это чисто визуальная логика.
- `Views/SettingsView.xaml` (переписан, раскладка ниже). `SettingsView.xaml.cs` не меняется.

**GitHubBackup.App.Tests**

- `ViewModels/SettingsViewModelTests.cs`: новые случаи.
- `ViewModels/FilteredItemsTests.cs` (новый).

**GitHubBackup.App.UITests**

- `AppSession.cs`: `Select(string automationId)` через `SelectionItem`.
- `SettingsEditorTests.cs`: выбор раздела в существующих тестах и новые тесты.
- `ThemeTests.cs` и `FirstRunTests.cs` работают в «General», это раздел по умолчанию, их не меняем.

**Документы:** `docs/PRD.md`, `README.md` (раздел Settings editor и строка про Theme).

## 5. Подход

Порядок работ: PRD → `FilteredItems<T>` с тестами → модель разделов и аккаунтов во ViewModel с тестами → XAML и behavior → FlaUI → README → netwright.

1. **Модель разделов.** `Sections` хранит шесть `SettingsSectionItem`. `OnSelectedSectionChanged` выставляет `IsSelected` у элементов. Контейнер каждого раздела привязан к `{Binding GeneralSection.IsSelected}` и т.п. через `BooleanToVisibilityConverter`. ViewModel создаётся как transient, поэтому при каждом открытии выбран General без дополнительного кода.
2. **Все разделы строятся один раз.** Невыбранные разделы `Collapsed`. `ContentControl` или `TabControl`, у которых содержимое пересоздаётся, не используются: `PasswordBox` токена и заголовков, привязка `ElementName=TelemetryHeadersTextBox` и введённый текст должны жить при переключении разделов. Невыбранный раздел не виден в UIA, поэтому FlaUI-тесты сначала выбирают раздел.
3. **Где ошибка раздела.** Метод `RefreshSectionErrors()`:
   - General: `GetErrors` у `BackupRoot`, `SourcesRoot`, `Exclude`.
   - Accounts: `HasAnyErrors` хотя бы одного аккаунта.
   - Repositories: `HasErrors` хотя бы одной строки.
   - Tools: `GetErrors` у `GitPath`, `SevenZipPath`.
   - Telemetry: `GetErrors` у `OtlpEndpoint`, `TelemetryHeaders`.
   - Token: `TokenError is not null`.

   `GeneralErrors` разделы не отмечают. Значки следуют тем же ошибкам, которые видны у полей.
4. **Когда пересчёт.** Сгенерированный сеттер с `[NotifyDataErrorInfo]` проверяет значение уже после `OnXxxChanged`. Поэтому пересчёт запускает `ErrorsChanged`, а не `OnXxxChanged`. ViewModel подписывает один обработчик на `ErrorsChanged` у себя и у каждой дочерней формы: аккаунтов, их репозиториев и отдельных репозиториев. Подписка ставится в `Populate`, `AddAccount` и `AddRepository`, при удалении снимается. Пересчёт также идёт в `OnTokenErrorChanged`, после удаления строки или аккаунта и в `SaveAsync`.
5. **Save с ошибками.** В ветке ошибок вызывается `RefreshSectionErrors()`, затем выбирается первый раздел с `HasErrors`. Для Accounts выбирается первый аккаунт с `HasAnyErrors`, у него очищается фильтр. Для Repositories очищается фильтр отдельных репозиториев. Если ошибки есть только в `GeneralErrors` или запись завершилась ошибкой, раздел не меняется.
6. **Выбор аккаунта.** `Populate` выбирает первый аккаунт или `null`. `AddAccount` выбирает новый. `RemoveAccount` выбранного аккаунта выбирает элемент с тем же индексом, затем предыдущий, затем `null`. Удаление невыбранного аккаунта выбор не меняет. Кнопка «Remove account» стоит рядом с «Add account» над списком и удаляет выбранный аккаунт.
7. **Фильтр во ViewModel, без `ICollectionView`.** Сравнение: `Contains(filter.Trim(), OrdinalIgnoreCase)`. Пустой фильтр показывает все строки. Список перестраивается при смене фильтра и при Reset. Добавленный элемент дописывается в конец, если подходит под фильтр; удалённый убирается. Правка строки не перефильтровывает список. `AddRepository` сначала очищает фильтр. `ToConfig` и сопоставление ошибок читают исходные коллекции, поэтому порядок файла не меняется.
8. **Раскладка `SettingsView`.**
   - Корень — `DockPanel`. Внизу Save и Cancel, над ними `GeneralErrors`. Вверху `LoadError`, `NewConfigMessage` и `Warnings`.
   - В центре `Grid` из двух колонок. Слева `ListBox` `SettingsSectionList`; у пунктов `AutomationId` и `Name` из элемента, заголовок и значок ошибки (`SettingsSectionErrorIcon`, `Name="Has errors"`). У каждого пункта навигации — значок раздела и название: глиф Segoe Fluent Icons хранится в `SettingsSectionItem.Glyph`, значок декоративный и не меняет `Name` пункта.
   - Справа разделы в одной ячейке, каждый — `GroupBox` с прежними `AutomationId` (`SettingsGeneralGroup`, `SettingsAccountsGroup` с заголовком «Accounts», новый `SettingsRepositoriesGroup`, `SettingsToolsGroup`, `SettingsTelemetryGroup`, `SettingsTokenGroup`).
   - General, Tools, Telemetry и Token обёрнуты каждый в свой `ScrollViewer`; списков внутри нет.
   - **Accounts:** слева «Add account», «Remove account» и `ListBox` `AccountsList` (`DisplayName`, значок ошибки). Справа детали `AccountDetails` (`DataContext=SelectedAccount`): URL, папка, исключения, фильтр `AccountRepositoryFilterBox`, `AccountRepositoriesNoMatch`, в строке `*` — `AccountRepositoriesList`. `NoAccountsMessage` виден, когда аккаунт не выбран.
   - **Repositories:** «Add repository», фильтр `RepositoryFilterBox`, `RepositoriesNoMatch`, в строке `*` — `RepositoriesList`.
   - Таблицы — `ItemsControl` с `ScrollingItemsTemplate` и `VirtualizingPanelTemplate`, без `MaxHeight`, без внешнего `ScrollViewer`, с `ScrollToAddedItem`.
   - Прежние `AutomationId` полей сохраняются.

## 6. Тесты

**`FilteredItemsTests`** (новый): пустой фильтр, часть имени без учёта регистра, фильтр из пробелов, нет совпадений (`HasNoMatch`), очистка фильтра, добавление подходящего и неподходящего элемента, удаление, пустой источник.

**`SettingsViewModelTests`** (новые случаи): General при создании и порядок разделов; выбор аккаунта при загрузке, добавлении и удалении (соседний, `null`, невыбранный); отметка General и её снятие; Save выбирает Accounts и первый аккаунт с ошибкой; ошибка ветки репозитория аккаунта отмечает аккаунт и очищает фильтр; ошибка отдельного репозитория выбирает Repositories и очищает фильтр; ошибки в нескольких разделах — первый по порядку; отметка Token и её снятие; отметка Telemetry; ошибка схемы оставляет раздел; удаление аккаунта с ошибкой снимает отметку; Add repository при активном фильтре; конфигурация не читается — навигация работает, ошибка Tools выбирает Tools; `DisplayName` пустого URL.

Существующие тесты Save, Cancel и записи остаются без изменений: регрессия для критерия «как до изменения».

**`SettingsEditorTests`** (FlaUI): навигация и General при открытии; каждый раздел показывает свои поля; существующие тесты Accounts, Tools и Telemetry сначала выбирают раздел; новые тесты: Save с ошибкой аккаунта из General открывает Accounts и отмечает его, добавление и удаление аккаунта, фильтр без совпадений и очистка, Add repository при фильтре, введённые заголовки сохраняются при смене раздела. Колесо мыши проверяется через netwright.

## 7. Проверка

| Критерий | Как проверяем |
|---|---|
| PRD | ревью diff `docs/PRD.md` |
| Навигация, General при открытии | тесты ViewModel и FlaUI; netwright |
| Колесо мыши, нет вложенной прокрутки | ревью XAML; netwright `desktop_scroll` над таблицей |
| Accounts | тесты ViewModel и FlaUI; netwright |
| Repositories, фильтр | `FilteredItemsTests`, тесты ViewModel и FlaUI; netwright |
| Отметка ошибки, Save открывает раздел | тесты ViewModel и FlaUI; netwright |
| Save и Cancel как раньше | существующие тесты |
| README | ревью diff |
| CI | build, тесты, format, UI-тесты; CI пулреквеста |

## 8. Вне объёма

- Добавление репозитория аккаунта вручную: его добавляет обнаружение.
- Фильтр списка аккаунтов.
- Сохранение выбранного раздела между открытиями: PRD требует открывать General.
- Изменение полей, текстов проверки и порядка записи.
- Колонки с заголовками и сортировка таблиц (DataGrid).

## 9. Решения

- Пользователь разрешил пройти задачу до конца без остановки на ревью плана; изменение PRD входит в этот issue и ревьюится в пулреквесте.
- `GeneralErrors` разделы не отмечают и не переключают; их видно в любом разделе над Save.
- Фильтр отдельных репозиториев ищет по URL (владелец входит в URL).
- У каждого аккаунта свой фильтр.
- Невыбранные разделы скрыты (`Collapsed`), а не пересоздаются.

## 10. Задачи

- [x] PRD: шапка 0.43 и дата, FR-12.5, FR-12.9, строка 0.43 в §14
- [x] `FilteredItems<T>` и `FilteredItemsTests`
- [x] `SettingsSection`, `SettingsSectionItem`
- [x] `AccountSettingsViewModel`: `DisplayName`, `HasAnyErrors`, `RepositoryView`
- [x] `SettingsViewModel`: разделы, подписки на `ErrorsChanged`, `RefreshSectionErrors`, выбор раздела и аккаунта при Save, `SelectedAccount`, `RepositoryView`, очистка фильтра в `AddRepository`
- [x] Новые случаи в `SettingsViewModelTests`
- [x] `Behaviors/ScrollToAddedItem`
- [x] `SettingsView.xaml`
- [x] `AppSession.Select`; обновить `SettingsEditorTests` и добавить UI-тесты
- [x] README
- [x] Сборка, тесты, format, UI-тесты
- [x] netwright
- [x] Проверка добавленных комментариев
