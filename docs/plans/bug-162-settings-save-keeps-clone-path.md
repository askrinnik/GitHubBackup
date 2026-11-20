# План: Settings editor Save drops the clonePath of repository entries

- Ишью: [#162](https://github.com/askrinnik/GitHubBackup/issues/162)
- Лейн: Bug
- Сложность: S
- Дата: 2026-11-20

## Источник требований

PRD FR-3.10, §7.3. Изменение PRD не требуется: требование уже описано, код его нарушает.

## Воспроизведение

Тест `SettingsViewModelTests.Save_OtherFieldEdited_KeepsClonePathOfEveryKindOfEntry` загружает конфиг, где у репозитория аккаунта и у отдельного репозитория задан `ClonePath`, меняет `BackupRoot` и сохраняет. До исправления тест падает: `ClonePath` в сохранённом конфиге равен `null`.

## Корневая причина

`SettingsViewModel.BuildConfig` пересобирает записи через `AccountRepositorySettingsViewModel.ToConfig` и `RepositorySettingsViewModel.ToConfig`. Эти методы создают новые объекты конфигурации и копируют только `Name`/`Url`, `Id`, `Status`, `Paused`, `Branch` и `Path`. Поле `ClonePath` редактор не показывает и не хранит, поэтому оно теряется.

## Изменения

- `AccountRepositorySettingsViewModel`: поле `_clonePath = source.ClonePath`, запись в `ToConfig` (`ClonePath = _clonePath`), как у `_id`.
- `RepositorySettingsViewModel`: то же самое.
- Редактор не показывает `clonePath`: поле переносится без изменений, как `Id`. UI не меняется.

## Тесты

- `Save_OtherFieldEdited_KeepsClonePathOfEveryKindOfEntry` (уже добавлен в рабочем дереве): оба вида записей; запись аккаунта без `clonePath` остаётся `null`.

## Риск регрессии

Минимальный: значение переносится как есть. Новые записи (`RepositoryConfig` без `ClonePath`) получают `null`, как раньше.

## Вне объёма

- Редактирование `clonePath` в редакторе настроек (FR-3.10 не требует его в UI).

## Задачи

- [x] Воспроизвести падение теста.
- [x] Переносить `ClonePath` в `AccountRepositorySettingsViewModel.ToConfig`.
- [x] Переносить `ClonePath` в `RepositorySettingsViewModel.ToConfig`.
- [x] Сборка, тесты и проверка формата зелёные.

## Решения

- Пользователь разрешил переходить к реализации без отдельного ревью плана, если вопросов нет. Вопросов нет.
