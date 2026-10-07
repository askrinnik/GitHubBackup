# План: #57 Keep configuration values out of binding error messages

- Issue: [#57](https://github.com/askrinnik/GitHubBackup/issues/57)
- Лейн: Feature (`type:chore`)
- Сложность: M (затрагивает безопасность, NFR-1)
- Дата: 2026-10-07
- PRD: 7.2, 6.10 (FR-10.6), NFR-1 — изменений PRD нет, код приводится в соответствие с §7.2

## Цель

`SectionBindingExtensions.BindSection` передаёт текст `InvalidOperationException` от `ConfigurationBinder` («Failed to convert configuration value '<value>' at '<key>' to type '<type>'.») в `OptionsValidationException`, а `CliApplication` печатает его в stderr. Секрет, записанный в типизированный ключ, был бы выведен. Сообщение должно называть только ключ и тип; комментарий `ConfigurationErrorMessages` должен описывать фактическое поведение.

## Критерии приёмки

- [x] Тест: ошибка типа значения (`Backup:ShortHashLength=abc`) называет ключ и не содержит значения
- [x] Тест: ошибки проверки остальных секций по-прежнему называют только ключи
- [x] Комментарии в коде описывают фактическое поведение
- [ ] Покрыто тестами, CI зелёный
- [x] (добавлено) сообщение называет и целевой тип: `Backup:ShortHashLength cannot be converted to System.Int32.`
- [x] (добавлено) токен в типизированном ключе (`--Backup:ShortHashLength=<token>`) не попадает в stderr
- [x] (добавлено) ошибки типов в двух секциях дают две строки без значений
- [x] (добавлено) если сообщение binder'а не сопоставилось ни с одним листом секции, выводится обобщённое сообщение с именем секции и типа опций без текста исключения

## Затрагиваемые типы

- `GitHubBackup.Core` / `Configuration/SectionBindingExtensions`: `BindSection` строит сообщение сам; `<remarks>` обновляется.
- `GitHubBackup.Infrastructure` / `Hosting/ConfigurationErrorMessages`: только `<remarks>`.
- Тесты: `Core.Tests/Configuration/SectionBindingExtensionsTests`, `Cli.Tests/CliApplicationTests`.

## Подход

1. В `catch (InvalidOperationException exception)` вызывается закрытый статический `DescribeBindingFailure(IConfigurationSection section, InvalidOperationException exception)`; `exception.Message` наружу не уходит. Внутреннее исключение не используется.
2. Ключ и тип: перебрать листья секции (`section.AsEnumerable()`, полные пути, непустые значения); для каждой пары искать в сообщении маркер `'{value}' at '{path}' to type '` (ordinal); при нескольких совпадениях взять самый длинный `path`; тип — текст после маркера до `'.`. Результат: `"{path} cannot be converted to {type}."`. Маркер строится из известных пар ключ–значение, поэтому значение, имитирующее шаблон, не подменит ключ.
3. Запасной вариант без совпадения: `"The {sectionName} section cannot be bound to {typeof(TOptions).Name}."`. Текст исключения не используется.
4. Binder останавливается на первой ошибке секции: одна строка на секцию; агрегирование по секциям остаётся за `ValidateOnStart`.
5. `ConfigurationErrorMessages.<remarks>` описывает: ошибки привязки называют ключ и тип (формируются в `BindSection`), ошибки валидаторов называют ключ, ошибки загрузки файла называют файл и позицию, токен не привязывается к опциям.

## Тесты

`SectionBindingExtensionsTests`:

- теория по `Backup:ShortHashLength=abc` и `Backup:VerifyArchive=maybe`: одна строка, точный текст, значения нет;
- значение в виде токена (`ghp_…`) не попадает в сообщение;
- значение, имитирующее шаблон binder'а, не подменяет ключ;
- запасная ветка: сообщение равно `"The Backup section cannot be bound to BackupOptions."`.

`CliApplicationTests`:

- ужесточить `RunAsync_ValueOfWrongType_ReturnsCriticalNamingKey`: точная строка, значения нет;
- `RunAsync_TokenInTypedKey_NeverWritesToken`;
- `RunAsync_TypeErrorsInSeveralSections_WritesOneLinePerKeyWithoutValues`.

Существующие `RunAsync_SeveralSectionsInvalid_…`, `RunAsync_InvalidLogLevel_…` и `EnvironmentVariableOverrideTests` остаются зелёными без изменений.

## Вне рамок

- Фрагменты в сообщениях разбора JSON (`InvalidDataException`).
- Маскирование стартовых сообщений через `ISecretMasker`: значение не попадает в текст по построению.
- WPF-хост пока не использует `ConfigurationErrorMessages`.

## Решения

- Формат сообщения: полное имя типа, как отдаёт binder (`System.Int32`).
- Запасная ветка тестируется напрямую: `DescribeBindingFailure` — `internal`, если у `Core` уже есть `InternalsVisibleTo` для `Core.Tests`; иначе остаётся `private`, а ветка проверяется через `BindSection` сценарием, в котором маркер не находится (например, значение пустое).
- Шаблон сообщения binder'а считается стабильным; при несовпадении срабатывает безопасная запасная ветка.

## Задачи

- [x] `BindSection` и `DescribeBindingFailure`, `<remarks>`, XML-документация.
- [x] `ConfigurationErrorMessages`: исправить `<remarks>`.
- [x] Тесты `SectionBindingExtensionsTests`.
- [x] Тесты `CliApplicationTests`.
- [x] Комментарии по comment hygiene.
- [x] Сборка, тесты, `dotnet format`.
