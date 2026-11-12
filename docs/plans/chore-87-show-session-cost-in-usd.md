# План: #87 — Show the session cost in USD in the workflow profile summary

- Issue: [#87](https://github.com/askrinnik/GitHubBackup/issues/87)
- Линия: Feature (`type:chore`, `area:ai`)
- Сложность: S
- Дата: 2026-11-12
- Источник: `docs/ai/README.md` (раздел «Профиль реальных сессий»), `docs/ai/implement-issue.md`

## 1. Цель

Итог профиля сессии на шаге 12 `/implement-issue` показывает оценочную сумму в USD по ценам API. Документы харнесса называют эту сумму.

## 2. Критерии приёмки

Из issue, дословно:
1. Шаг 12 `implement-issue` называет сумму в USD в составе итога профиля, с пометкой, что это оценка по ценам API, а не списание по подписке
2. `docs/ai/implement-issue.md` и `docs/ai/README.md` упоминают сумму в USD
3. На первой сессии после изменения итог профиля показывает сумму в USD

## 3. Подход

- `.ai/prompts/implement-issue.md`, шаг 12, пункт *Show the result*: добавить «the estimated cost in USD (an estimate at API list prices, not a subscription charge)».
- `docs/ai/implement-issue.md`, дерево шагов: строка итога — «вызовы, единицы стоимости, сумма в USD, активности, лимит».
- `docs/ai/README.md`, раздел «Профиль реальных сессий»: в перечне измерений — оценка суммы в USD по ценам API.
- Код и тесты не меняются.

## 4. Чек-лист

- [x] Шаг 12 `.ai/prompts/implement-issue.md`
- [x] `docs/ai/implement-issue.md`
- [x] `docs/ai/README.md`

## Вне рамок

- `.ai/prompts/implement-issues.md` и `docs/ai/implement-issues.md`: своего состава итога у них нет.
- `profile-session.config.json`: таблица цен живёт в настройках плагина.

## Решения

- Открытых вопросов нет; пользователь разрешил начать реализацию без отдельного обзора плана.
