# Указания для AI-ассистентов в этом репозитории

## Скилл `yt`

В репозитории есть [`.claude/skills/yt/SKILL.md`](.claude/skills/yt/SKILL.md) — это пользовательский руководящий документ по работе с CLI `yt`. Его автоматически подхватывает Claude Code и другие совместимые AI-инструменты.

Этот же файл встроен в бинарь как embedded resource (LogicalName `yt.skill.md`) и распространяется через команду `yt skill install`. Skill устанавливается в 5 AI-ассистентов:

- **Claude** (`~/.claude/skills/yt/SKILL.md`) — полный SKILL.md as-is
- **Codex** (`~/.agents/skills/yt/SKILL.md`) — полный SKILL.md as-is
- **Gemini** (`~/.gemini/skills/yt/SKILL.md`) — полный SKILL.md as-is
- **Cursor** (`~/.cursor/rules/yt.mdc`) — translated `.mdc` (frontmatter: `description` / `globs` / `alwaysApply`)
- **Copilot** (`<projectDir>/.github/instructions/yt.instructions.md`) — translated `.instructions.md` (`applyTo: "**"`); **только project-scope**, global-scope для Copilot тихо пропускается

### Шпаргалка `yt skill`

| Команда | Назначение |
|---|---|
| `yt skill install` (TTY, без флагов) | Интерактивный prompt: выбор ассистентов + scope + подтверждение перезаписи. По умолчанию предлагает «обнаруженные» (видит `~/.claude`, `~/.gemini`, …). |
| `yt skill install --no-prompt` | Скрипт-режим: ставит во все 5 ассистентов (`--target all --scope global` — Claude+Codex+Gemini+Cursor; Copilot global → skipped). Используется в CI и при перенаправлении stdin/stdout. |
| `yt skill install --target claude --scope project` | В `./.claude/skills/yt/SKILL.md` (явный target → prompt не запускается) |
| `yt skill install --target gemini` | Только Gemini (`~/.gemini/skills/yt/SKILL.md`) |
| `yt skill install --target cursor` | Только Cursor (`~/.cursor/rules/yt.mdc`) |
| `yt skill install --target copilot --scope project` | Только Copilot (`<projectDir>/.github/instructions/yt.instructions.md`) |
| `yt skill status` | Что установлено + version + `up_to_date` per location + `any_outdated` |
| `yt skill update` | Перезаписать все уже установленные локации текущей версией CLI |
| `yt skill check` | Ручная проверка устаревших skill (TTY → prompt; pipe → JSON warning) |
| `yt skill check --no-prompt` | Только статус |
| `yt skill check --reset-prompt-state` | Сбросить «больше не спрашивать» |
| `yt skill uninstall` | Удалить |
| `yt skill show --target claude\|codex\|gemini\|cursor\|copilot` | Напечатать содержимое (как было бы записано) |

Интерактивный режим `yt skill install` активируется когда:

- stdout/stdin не перенаправлены (TTY),
- НЕ передан `--target` или `--scope`,
- НЕ передан `--no-prompt`.

Иначе используется default flow (`--target all --scope global`, или то что передано в флагах).

Auto-check срабатывает при каждом вызове `yt <команда>` (skip для `skill *`, `--version`, `--help`, `--no-skill-check`, `YT_SKILL_CHECK=0`). State хранится в `~/.cache/yandex-tracker/skill-prompt-state.json`.

В SKILL.md обязательно должен быть маркер `<!-- yt-version: {VERSION} -->` сразу после YAML frontmatter — `EmbeddedSkill.ReadAll()` подменяет `{VERSION}` на актуальную сборочную версию из `AssemblyInformationalVersionAttribute` (MinVer). По этому маркеру `status`/`update`/auto-check определяют, актуален ли установленный файл.

### Правило обновления скилла

**При любом изменении публичного API CLI обязательно обновить SKILL.md.** Публичный API — это всё что видит конечный пользователь:

- Имена команд и подкоманд (`yt <group> <command>`)
- Аргументы и опции команд (`--profile`, `--queue`, …)
- Поведение по умолчанию (auto-detect формата, default-profile resolution)
- Формат вывода (поля JSON, exit-коды)
- Переменные окружения (`YT_*`, `NO_COLOR`, `PAGER`)
- Поведение при ошибках (коды, сообщения)

Что считать изменением:

- **Добавление**: новые команды, флаги, env-переменные → добавить в шпаргалку и соответствующие разделы скилла.
- **Изменение поведения**: меняется default, меняется формат вывода, меняется exit-код → исправить примеры и описания.
- **Удаление/переименование**: удалить устаревшие упоминания, добавить миграционную пометку если ломаем совместимость.

Сценарий PR:

1. Изменил код в `src/YandexTrackerCLI/` (или `Core/`) затрагивающий пользовательский API.
2. Обновил тесты.
3. **Обновил `.claude/skills/yt/SKILL.md`** — синхронизировал примеры, шпаргалку, exit-коды.
4. (опционально) Обновил `README.md` если изменение видно в основном описании.

Нарушение этого правила приводит к расхождению между документацией для AI и реальным поведением CLI — AI начинает выдумывать несуществующие флаги или использовать устаревший синтаксис.

## Архитектура

- `src/YandexTrackerCLI.Core/` — переиспользуемое ядро (HTTP-клиент, auth-провайдеры, конфиг, JSON-сериализация). AOT-friendly, без UI-зависимостей.
- `src/YandexTrackerCLI/` — CLI: команды, парсинг аргументов через System.CommandLine 2.0.7, рендеры вывода (JSON/Minimal/Table/Detail).
- `tests/YandexTrackerCLI.Core.Tests/` — unit-тесты ядра.
- `tests/YandexTrackerCLI.Tests/` — e2e и component тесты CLI.

## Технические инварианты

- **NativeAOT-совместимость**: никакого reflection, только source-gen JSON (`TrackerJsonContext`), `JsonElement` для динамических данных. `IsAotCompatible=true` обязателен.
- **TreatWarningsAsErrors=true** — все warning'и должны быть исправлены до коммита.
- **Тесты на TUnit** — не xUnit. CLI-тесты с глобальным состоянием (Console, env) помечать `[NotInParallel("yt-cli-global-state")]`.
- **Версионирование через MinVer** — версия выводится из git-тега (`v0.1.0`). Не редактировать `Version` свойства вручную.

## Релизный workflow

1. Внести изменения, пройти тесты локально (`dotnet test`).
2. Если затронут API — **обновить SKILL.md** (см. выше).
3. Коммит, push в main → CI запускает только тесты.
4. Когда готов релиз: `git tag vX.Y.Z` + `git push origin vX.Y.Z` → CI собирает 4 платформенных архива и публикует [GitHub Release](https://github.com/RoboNET/YandexTrackerCLI/releases) с SHA256SUMS.

## Команды для разработки

```bash
# Тесты
dotnet test --project tests/YandexTrackerCLI.Core.Tests/YandexTrackerCLI.Core.Tests.csproj
dotnet test --project tests/YandexTrackerCLI.Tests/YandexTrackerCLI.Tests.csproj

# Локальный AOT-бинарь
dotnet publish src/YandexTrackerCLI/YandexTrackerCLI.csproj \
  -c Release -r osx-arm64 --self-contained -o ./dist
./dist/yt --version

# Smoke-test с read-only safety
YT_READ_ONLY=1 ./dist/yt user me
```
