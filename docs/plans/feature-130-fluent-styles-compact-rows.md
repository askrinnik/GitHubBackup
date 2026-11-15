# План: стили Fluent для полей настроек и компактные строки лога и сводки

| Поле | Значение |
|---|---|
| Issue | [#130](https://github.com/askrinnik/GitHubBackup/issues/130) — F4.12: Fluent styles for settings fields and compact log and summary rows |
| Полоса | Feature |
| Сложность | S |
| Дата | 2026-11-15 |

## Цель

Поля ввода и кнопки следуют теме Fluent в светлом и тёмном режиме. Строки ленты лога и таблицы результатов занимают одну строку текста. Требования: FR-12.3, FR-12.4, FR-12.9 (PRD 0.33).

## Критерии приёмки

1. В тёмной теме поля ввода и кнопки редактора настроек имеют оформление Fluent тёмной темы, как поле токена; в светлой теме — оформление Fluent светлой темы.
2. Поле с ошибкой валидации по-прежнему показывает красную рамку и текст ошибки под собой.
3. Стили приложения с `TargetType` элемента управления наследуют стиль Fluent этого элемента.
4. Строка ленты лога и строка таблицы результатов занимают одну строку текста с небольшими отступами; выделение строки остаётся видимым.
5. Видимые пункты проверены в запущенном приложении (netwright) в светлой и тёмной теме.
6. Покрыто тестами, CI зелёный.

## Причина

Стиль с `TargetType` элемента управления и без `BasedOn` заменяет стиль Fluent целиком. WPF берёт тогда шаблон классической темы Aero2, и элемент остаётся светлым в любой теме. Контейнеры строк Fluent (`ListBoxItem`, `ListViewItem`) имеют большие `Padding` и `MinHeight`.

## Затронутые стили

| Файл | Стиль | Изменение |
|---|---|---|
| `App.xaml` | `FormTextBoxStyle` (`TextBox`) | `BasedOn="{StaticResource {x:Type TextBox}}"` |
| `App.xaml` | новые `CompactListBoxItemStyle`, `CompactListViewItemStyle` | на основе стилей Fluent, малый `Padding`, `MinHeight="0"` |
| `SettingsView.xaml` | `GroupStyle` (`GroupBox`), `RowButtonStyle` (`Button`) | `BasedOn` |
| `HistoryView.xaml` | `FilterControlStyle` (`Control`, применён к `DatePicker` и `ComboBox`) | разделить на `FilterDatePickerStyle` и `FilterComboBoxStyle`, каждый `BasedOn` своего типа |
| `HistoryView.xaml` | `HistoryGridStyle` (`DataGrid`), стиль `DataGridRow` | `BasedOn` |
| `DiscoveryReviewWindow.xaml` | два встроенных стиля `TextBox` | `BasedOn` |
| `RepositoriesView.xaml` | стиль `TreeViewItem` | `BasedOn` |
| `RepositoriesView.xaml` | `RunLog` (`ListBox`), `RunSummaryTable` (`ListView`) | `ItemContainerStyle` — компактные стили из `App.xaml` |

Стили `Thumb` и `GridSplitter` задают свой шаблон целиком и не меняются. Стили `TextBlock`, `StackPanel`, `RowDefinition` не относятся к элементам управления.

## Подход

- Стили ресурсов создаются при первом обращении. К этому моменту `WpfThemeService` уже включил Fluent, поэтому `StaticResource {x:Type …}` находит стиль Fluent. Шаблоны Fluent берут кисти через `DynamicResource`, поэтому смена режима во время работы меняет и цвета унаследованных стилей. Это проверяют UI-тест и netwright (переключение Light → Dark).
- Компактная строка: `Padding="4,1"`, `MinHeight="0"`, `Margin="0"`. Значения уточняются по виду в приложении.
- Шаблон ошибки валидации `FormTextBoxStyle` не меняется.

## Тесты

- `GitHubBackup.App.UITests/ThemeTests`: в тёмной теме поле `BackupRootBox` тёмное (средняя яркость ниже порога тёмной темы); в светлой — светлое.
- `GitHubBackup.App.UITests/MainWindowLayoutTests`: после пробного прогона высота строки `RunLog` и строки `RunSummaryTable` не больше двух высот строки текста (сравнение с высотой текстового элемента строки, а не с числом пикселей, чтобы тест не зависел от масштаба экрана).
- Проверка ошибки валидации — существующие тесты `SettingsEditorTests`, плюс netwright.

## Вне объёма

- Собственная палитра цветов (варианты B и C отклонены в пользу нативного Fluent).
- Плотность строк дерева репозиториев и таблиц истории.

## Решения

- Вариант A: нативные стили Fluent, без своей палитры (решение пользователя).
- Ветка и worktree созданы до первого коммита по просьбе пользователя.
- При реализации: стили ресурсов `App.xaml` создаются раньше, чем `WpfThemeService` включает Fluent, и `BasedOn` находил классический стиль. Поэтому `App.xaml` задаёт `ThemeMode="System"`: тема Fluent загружается до ресурсов, а сервис темы затем переключает режим пользователя.
- При реализации: `ListView` с `GridView` берёт стиль строки по ключу `GridView.GridViewItemContainerStyleKey`; стиль по типу `ListViewItem` в теме Fluent не задан.
- Тест строк использует настоящий прогон (git и 7-Zip на локальном bare-репозитории), а не пробный: только он даёт строки в таблице результатов.
- Найдено, вне объёма: рамка ошибки валидации поля URL в строке отдельного репозитория рисуется со смещением. Дефект был и до этой задачи (проверено со старым стилем поля).

## Задачи

- [x] `BasedOn` для стилей из таблицы
- [x] Компактные стили строк и их подключение к `RunLog` и `RunSummaryTable`
- [x] UI-тесты темы полей и высоты строк
- [x] Сборка, тесты, формат
- [x] Проверка в приложении (netwright), светлая и тёмная тема
