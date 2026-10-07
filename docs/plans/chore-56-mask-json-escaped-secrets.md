# План: #56 Mask JSON-escaped forms of registered secrets in log files

- Issue: [#56](https://github.com/askrinnik/GitHubBackup/issues/56)
- Лейн: Feature (`type:chore`)
- Сложность: S
- Дата: 2026-10-07
- PRD: 6.10 (FR-10.6), NFR-1 — изменений PRD нет

## Цель

`SecretMasker.Register` хранит только сырой текст секрета. Serilog пишет `"`, `\` и управляющие символы в JSON-логе в экранированном виде, поэтому такой секрет остаётся открытым в `.json`-файле и в `{Properties:j}` текстового лога. `Register` должен дополнительно хранить JSON-экранированную форму, если она отличается от исходной, сохраняя порядок «длинные первыми».

## Критерии приёмки

- [x] Тест: секрет с `"` и `\` маскируется в выводе `CompactJsonFormatter`
- [x] Тест: секрет с управляющим символом маскируется в JSON-файле
- [x] Тест: JSON остаётся валидным после маскирования
- [ ] Покрыто тестами, CI зелёный
- [x] (добавлено) секрет со спецсимволами маскируется и в текстовом логе: в `{Message:lj}` (сырой вид) и в `{Properties:j}` (экранированный)
- [x] (добавлено) секрет, оканчивающийся на `\` или `"`, заменяется целиком, JSON остаётся валидным
- [x] (добавлено) секрет с не-ASCII символами маскируется в JSON-выводе
- [x] (добавлено) повторная регистрация секрета со спецсимволами ничего не дублирует

## Затрагиваемые типы

- `GitHubBackup.Core` / `Security/SecretMasker`: `Register`, новый `private static EscapeAsJsonString`, `///` у `_secrets` и `Register`.
- `GitHubBackup.Core` / `Security/ISecretMasker`: одна фраза в doc `Register`.
- Тесты: `Core.Tests/Security/SecretMaskerTests`, `Infrastructure.Tests/Logging/MaskingTextFormatterTests` (в конец, после тестов #55).

## Подход

1. Собственный экранировщик, повторяющий `JsonValueFormatter.WriteQuotedJsonString` из Serilog: `"` → `\"`, `\` → `\\`, `\n \r \t \f` → короткие формы, прочие символы `< 0x20` → `\u` + 4 hex-цифры в верхнем регистре, остальное как есть. `JsonEncodedText.Encode` не используется: с кодировщиком по умолчанию и с `UnsafeRelaxedJsonEscaping` он даёт формы, которых Serilog не пишет (`\uXXXX` для не-ASCII и `<>&'+`). Core не ссылается на Serilog (слои).
2. Правила проверяются реальным `CompactJsonFormatter` в тестах; расхождение подгоняется под фактический вывод.
3. `Register`: экранированная форма считается до `lock`; под `lock` каждая форма добавляется, если её ещё нет (`StringComparer.Ordinal`); одна сортировка по убыванию длины и одна публикация неизменяемого массива. Экранированная форма длиннее сырой, поэтому заменяется первой (секрет `ab\` → `ab\\` в JSON; иначе останется `***\"`).
4. `{Message:lj}` пишет строки литерально (совпадает сырая форма); `{Properties:j}` и поля CLEF идут через `JsonValueFormatter` (экранированная форма).

## Тесты

`SecretMaskerTests`:

- теория: экранированная форма регистрируемого секрета (`a"b`, `a\b`, LF, TAB, U+0001, U+0008) даёт `***`;
- сырая форма тоже маскируется;
- секрет `s3cret\` внутри `{"Token":"s3cret\\"}` → `{"Token":"***"}`, `JsonDocument.Parse` проходит;
- повторная регистрация не мешает.

`MaskingTextFormatterTests` (секрет регистрируется на `_masker`):

- `"` и `\`: свойство, исключение и шаблон сообщения; вывод без сырой и экранированной форм, JSON валиден, значение `***`;
- теория по управляющим символам (`\u0001`, `\u0008`, `\n`, `\t`, `\u001B`);
- секрет с хвостовым `\`;
- не-ASCII секрет;
- текстовый лог с `{Message:lj} {Properties:j}`.

## Вне рамок

- URL-кодирование, Base64 и двойное JSON-экранирование секретов.
- Формы других кодировщиков (`System.Text.Json`).
- Консольный вывод и UI.
- `GitHubToken()` и `AuthorizationHeader()` — #55.

## Решения

- Собственный экранировщик по правилам Serilog вместо `JsonEncodedText.Encode`, предложенного в issue: он совпадает с тем, что реально пишется в лог.
- Правила Serilog (нет короткой формы `\b`, hex в верхнем регистре) подтверждаются тестами через `CompactJsonFormatter`.

## Задачи

- [x] `EscapeAsJsonString` с `///`.
- [x] `Register`: обе формы, dedupe, одна сортировка и публикация.
- [x] `///` у `_secrets`, `Register`, `ISecretMasker.Register`.
- [x] Тесты `SecretMaskerTests`.
- [x] Тесты `MaskingTextFormatterTests`.
- [x] Комментарии по comment hygiene.
- [x] Сборка, тесты, `dotnet format`.
