# Указания для AI-ассистентов в этом репозитории

## Скилл `yt`

В репозитории есть [`.claude/skills/yt/SKILL.md`](.claude/skills/yt/SKILL.md) — это пользовательский руководящий документ по работе с CLI `yt`. Его автоматически подхватывает Claude Code и другие совместимые AI-инструменты.

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
