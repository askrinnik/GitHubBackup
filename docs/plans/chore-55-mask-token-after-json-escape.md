# План: #55 Mask GitHub tokens that follow a JSON escape sequence in logs

- Issue: [#55](https://github.com/askrinnik/GitHubBackup/issues/55)
- Лейн: Feature (`type:chore`)
- Сложность: S
- Дата: 2026-10-07
- PRD: 6.10 (FR-10.6), NFR-1 — изменений PRD нет

## Цель

`SecretMasker.GitHubToken()` не маскирует токен, если перед ним стоит JSON-экранирование (`\n`, `\t`, …) или `%XX`: левая граница `(?<![A-Za-z0-9_])` видит букву или цифру. Нужно расширить границу, не допустив ложных срабатываний внутри идентификаторов.

## Критерии приёмки

- [x] Тест: токен после `\n` в `CompactJsonFormatter` маскируется
- [x] Тест: токен после `\t` в текстовом логе с `{Properties:j}` маскируется
- [x] Тест: токен после `%3A` маскируется
- [x] Тест: идентификатор, содержащий `ghp_` внутри слова, не маскируется
- [ ] Покрыто тестами, CI зелёный
- [x] (добавлено) маскирование после `\r`, `\b`, `\f`, `\uXXXX` и `%XX` в любом регистре
- [x] (добавлено) все формы токена (`ghp_`, `gho_`, `ghu_`, `ghs_`, `ghr_`, `github_pat_`) маскируются после экранирования
- [x] (добавлено) слово, оканчивающееся на `n`/`t`/`r`/`b`/`f` без `\` перед ним, не маскируется

## Подход

Левая граница: «нет символа слова перед токеном» ИЛИ «перед токеном escape-последовательность»:

```
(?:(?<![A-Za-z0-9_])|(?<=\\[nrtbf]|\\u[0-9A-Fa-f]{4}|%[0-9A-Fa-f]{2}))(?:gh[pousr]_[A-Za-z0-9_]{36,}|github_pat_[A-Za-z0-9_]{22,})
```

Шаблон — raw string literal, как у `AuthorizationHeader()`. Меняется только `GitHubToken()` в `src/GitHubBackup.Core/Security/SecretMasker.cs` и его `///` (`<remarks>` с причиной, без ссылки на issue). `Register`, `Mask`, `_secrets` не трогаются.

## Тесты

`GitHubBackup.Core.Tests` / `SecretMaskerTests`:

- `Mask_GitHubTokenAfterJsonEscape_ReplacesToken` — теория: `\n`, `\r`, `\t`, `\b`, `\f`, `\u001B`.
- `Mask_GitHubTokenAfterUrlEncodedCharacter_ReplacesToken` — теория: `%3A`, `%3a`, `%2F`.
- `Mask_GitHubTokenFormsAfterJsonEscape_ReplacesToken` — теория по шести формам токена после `\n`.
- `Mask_UnregisteredGitHubToken_ReplacesIt` — добавить `ghu_` и `ghr_`.
- `Mask_GitHubTokenPrefixInsideWord_ReturnsTextUnchanged` — теория: `myghp_…`, `contentghp_…`, `xgithub_pat_…`, `A3Aghp_…`.

`GitHubBackup.Infrastructure.Tests` / `MaskingTextFormatterTests` (используется `LogEvents.Create`, токен не регистрируется):

- `Format_GitHubTokenAfterNewLineInProperty_MasksJsonOutputAndKeepsItValid` — `CompactJsonFormatter`, JSON валиден, значение `"line one\n***"`.
- `Format_GitHubTokenAfterTabInProperty_MasksTextOutput` — `TextFormatter()` с `{Properties:j}`, вывод содержит `a\t***`.

## Вне рамок

- JSON-экранированные формы зарегистрированных секретов — #56.
- Двойное URL-кодирование (`%253A`).
- Проверка чётности `\` перед `n`/`t`.
- Правая граница токена и форматы без документированных префиксов.

## Решения

- `\uXXXX` входит в левую границу: Serilog пишет прочие управляющие символы (например ESC из цветного вывода git) как `\u00XX`.
- Избыточное маскирование для `\\n` + `ghp_…` принимается: это не утечка, а чётность `\` усложнила бы шаблон.

## Задачи

- [x] Изменить шаблон `SecretMasker.GitHubToken()` и дополнить `///`.
- [x] Добавить `ghu_` и `ghr_` в `Mask_UnregisteredGitHubToken_ReplacesIt`.
- [x] Добавить теории в `SecretMaskerTests`.
- [x] Добавить два факта в `MaskingTextFormatterTests`.
- [x] Проверить комментарии по comment hygiene.
- [x] Сборка, тесты и `dotnet format`.
