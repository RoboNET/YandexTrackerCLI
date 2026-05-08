# yt — Yandex Tracker CLI

Кроссплатформенный CLI-клиент для [Яндекс Трекера](https://yandex.ru/support/tracker/ru/api-ref/about-api), собранный через NativeAOT.

- **Один бинарь ~7.5 МБ** — без зависимостей, без рантайма, мгновенный старт.
- **Все четыре способа входа** — OAuth (Yandex 360), IAM-static, Service Account, Federated (browser PKCE с DPoP).
- **JSON-first для AI-агентов** и read-friendly table для человека (auto-detect TTY).
- **Покрывает почти весь публичный API** — задачи, комментарии, ворклоги, вложения, чек-листы, доски, спринты, проекты, компоненты, версии, поля, справочники.
- **READ_ONLY режим** — гарантия для безопасных скриптов и AI.
- **Wire-log** для отладки HTTP, с маскированием секретов.

## Содержание

- [Установка](#установка)
- [Быстрый старт](#быстрый-старт)
- [Авторизация](#авторизация)
  - [OAuth (Yandex 360)](#oauth-yandex-360)
  - [IAM-static (Yandex Cloud)](#iam-static-yandex-cloud)
  - [Service Account](#service-account)
  - [Federated (браузер)](#federated-браузер)
- [Профили и конфиг](#профили-и-конфиг)
- [Команды](#команды)
- [Формат вывода](#формат-вывода)
- [Read-only режим](#read-only-режим)
- [Переменные окружения](#переменные-окружения)
- [Wire-log (отладка HTTP)](#wire-log-отладка-http)
- [Exit-коды](#exit-коды)
- [Безопасность](#безопасность)
- [AI-ассистенты](#ai-ассистенты)
- [Сборка из исходников](#сборка-из-исходников)
- [Версионирование](#версионирование)
- [Лицензия](#лицензия)

## Установка

### Homebrew (macOS Apple Silicon, Linux)

```bash
brew install RoboNET/yt/yt
```

После выпуска новой версии:

```bash
brew upgrade yt
```

### Scoop (Windows)

```powershell
scoop bucket add yt https://github.com/RoboNET/scoop-yt
scoop install yt
```

Обновление:

```powershell
scoop update yt
```

### PowerShell-инсталлер (Windows, без Scoop)

```powershell
irm https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/main/install.ps1 | iex
```

Установит `yt.exe` в `%LOCALAPPDATA%\Programs\yt` и добавит каталог в user PATH. Поддерживаются параметры:

```powershell
# Конкретная версия
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/main/install.ps1))) -Version 0.1.2

# Свой каталог установки, без правки PATH
iwr https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/main/install.ps1 -OutFile install.ps1
.\install.ps1 -InstallDir C:\tools\yt -NoPath
```

Скрипт скачивает `yt-win-x64.zip`, проверяет SHA256 из `SHA256SUMS` и распаковывает `yt.exe`. На ARM64-Windows ставится x64-сборка (работает через эмуляцию).

### Готовый бинарь

Скачайте архив для своей платформы со страницы [Releases](https://github.com/RoboNET/YandexTrackerCLI/releases/latest):

| Платформа | Архив |
|---|---|
| Linux x86_64 | `yt-linux-x64.tar.gz` |
| Linux ARM64 | `yt-linux-arm64.tar.gz` |
| macOS Apple Silicon | `yt-osx-arm64.tar.gz` |
| Windows x86_64 | `yt-win-x64.zip` |

Также прилагается файл `SHA256SUMS` — для проверки целостности.

```bash
# macOS Apple Silicon (пример)
RID=osx-arm64
VERSION=v0.1.1
curl -L -o yt.tar.gz \
  "https://github.com/RoboNET/YandexTrackerCLI/releases/download/${VERSION}/yt-${RID}.tar.gz"
tar -xzf yt.tar.gz
sudo mv yt /usr/local/bin/yt
yt --version
```

На macOS перед первым запуском возможно потребуется снять `quarantine`:

```bash
xattr -d com.apple.quarantine /usr/local/bin/yt
```

На Windows распакуйте `yt.exe` из zip и положите в любую папку из `PATH` (или используйте Scoop / `install.ps1` выше).

Размер бинаря в архиве — около 3–4 МБ (распакованный — 7–8 МБ).

### Из исходников

Если хочется собрать самому или нужна последняя dev-версия:

```bash
git clone https://github.com/RoboNET/YandexTrackerCLI.git
cd YandexTrackerCLI
dotnet publish src/YandexTrackerCLI/YandexTrackerCLI.csproj \
  -c Release -r osx-arm64 --self-contained -o ./dist
./dist/yt --help
```

Поддерживаемые RID: `osx-arm64`, `linux-x64`, `linux-arm64`, `win-x64`. Требуется .NET 10 SDK 10.0.201+.

## Быстрый старт

```bash
# 1. Залогиниться (один из четырёх способов, см. ниже)
yt auth login --type federated --federation-id <fed-id> \
  --org-type cloud --org-id <org-id> --profile work

# 2. Сделать профиль активным по умолчанию
yt config profile work

# 3. Использовать
yt user me                          # информация о себе
yt queue list --max 10              # доступные очереди
yt issue find --queue TECH --max 5  # задачи в очереди
yt issue get TECH-146               # детали задачи (с markdown в TTY)
yt comment list TECH-146            # комментарии задачи
```

## Авторизация

CLI поддерживает четыре режима. Какой выбрать:

| Режим | Когда использовать |
|---|---|
| **OAuth** | Yandex 360 / Яндекс ID, есть токен `y0_…` |
| **IAM-static** | Yandex Cloud, разовая задача с готовым IAM-токеном |
| **Service Account** | CI / автоматизация, есть SA-ключ |
| **Federated** | Yandex Cloud, обычный пользователь (как `yc init`) |

### OAuth (Yandex 360)

Если токен уже есть:

```bash
yt auth login --type oauth --token y0_XXX \
  --org-type yandex360 --org-id 123456 --profile work
```

Если токена нет — без `--token` в TTY CLI откроет страницу OAuth и попросит ввести токен:

```bash
yt auth login --type oauth --org-type yandex360 --org-id 123456 --profile work
```

Где взять токен: [oauth.yandex.ru](https://oauth.yandex.ru/) → создать приложение с правами на Tracker.

Где взять `org-id`: [admin.yandex.ru](https://admin.yandex.ru/) → организация → URL содержит `org/<id>`.

### IAM-static (Yandex Cloud)

```bash
TOKEN=$(yc iam create-token)
yt auth login --type iam-static --token "$TOKEN" \
  --org-type cloud --org-id <org-id> --profile yc
```

IAM-токен живёт 12 часов. Для долгой работы используйте Service Account или Federated.

### Service Account

```bash
yt auth login --type service-account \
  --sa-id ajeXXX --key-id ajkXXX --key-file ./sa-key.pem \
  --org-type cloud --org-id <org-id> --profile ci
```

CLI собирает JWT (PS256), обменивает на IAM-токен через `https://iam.api.cloud.yandex.net/iam/v1/tokens` и кэширует его в `~/.cache/yandex-tracker/iam-tokens.json` (chmod 600). Обновление автоматическое.

Альтернатива — передать ключ через env (удобно для CI):

```bash
export YT_SERVICE_ACCOUNT_ID=ajeXXX
export YT_KEY_ID=ajkXXX
export YT_KEY_FILE=./sa-key.pem
export YT_ORG_TYPE=cloud
export YT_ORG_ID=<org-id>
yt user me
```

### Federated (браузер)

Самый удобный режим для разработчика — аналог `yc init --federation-id`:

```bash
yt auth login --type federated --federation-id <fed-id> \
  --org-type cloud --org-id <org-id> --profile work
```

Что произойдёт:

1. CLI запустит локальный listener на `127.0.0.1:<random-port>`.
2. Откроет в браузере `https://auth.yandex.cloud/oauth/authorize?…&yc_federation_hint=<fed-id>`.
3. Залогинитесь в IdP вашей организации.
4. Браузер вернётся на localhost, CLI обменяет `code` на access + refresh токены.
5. Сгенерирует ECDSA P-256 keypair (DPoP), сохранит в `~/.config/yandex-tracker/federated-keys/<profile>.pem`.

Дальше CLI **сам** обновляет access по истечении (≈12 ч) через DPoP refresh — браузер больше не нужен. Если ваша организация не выдаёт refresh-токены — CLI сообщит `mode: federated_static` и попросит перелогиниться через 12 ч.

**Где взять `federation-id`:**

```bash
yc organization-manager federation saml list --organization-id <org-id>
```

## Профили и конфиг

Все настройки лежат в `~/.config/yandex-tracker/config.json`.

### Несколько профилей

```bash
yt auth login --type oauth --token y0_X --org-type yandex360 --org-id 1 --profile personal
yt auth login --type federated --federation-id F --org-type cloud --org-id O --profile work

yt config list                       # показать все профили (секреты маскируются)
yt config profile work               # выбрать default
yt --profile personal queue list     # одноразовый override
YT_PROFILE=personal yt queue list    # тоже одноразовый, через env
```

### Управление настройками профиля

```bash
yt config get auth.org_id --profile work
yt config set default_format=table --profile work
yt config set read_only=true --profile work     # сделать профиль read-only
```

Доступные ключи для `config set`: `org_type`, `org_id`, `read_only`, `default_format`.

### Приоритет источников

Для каждого параметра — первое непустое значение:

1. CLI-флаг (`--profile`, `--org-id`, `--read-only`, `--format`, `--timeout`, `--no-color`, `--no-pager`)
2. Env-переменная (`YT_PROFILE`, `YT_ORG_ID`, `YT_READ_ONLY`, `YT_FORMAT`, `YT_TIMEOUT`, …)
3. Поле профиля в `config.json`
4. Default

## Команды

| Группа | Команды |
|---|---|
| `auth` | `login`, `logout`, `status` |
| `config` | `list`, `get`, `set`, `profile` |
| `user` | `me`, `get`, `search`, `list` |
| `queue` | `list` |
| `issue` | `get`, `find`, `create`, `update`, `transition`, `move`, `delete`, `batch` |
| `comment` | `list`, `add`, `update`, `delete` |
| `worklog` | `list`, `add`, `update`, `delete` |
| `attachment` | `list`, `upload`, `download`, `delete` |
| `checklist` | `get`, `add-item`, `toggle`, `update`, `remove` |
| `link` | `list`, `add`, `remove` |
| `board` | `list`, `get` |
| `sprint` | `list`, `get` |
| `project` | `list`, `get`, `create`, `update`, `delete` |
| `component` | `list`, `get`, `create`, `update`, `delete` |
| `version` | `list`, `get`, `create`, `update`, `delete` |
| `field` | `list`, `get` |
| `ref` | `statuses`, `priorities`, `issue-types`, `resolutions` |

`yt --help` и `yt <group> --help` дают полную справку.

### Поиск задач (issue find)

Поддерживает оба способа фильтрации одновременно:

```bash
# Простые фильтры
yt issue find --queue TECH --status open --assignee korolev --max 20

# YQL-запрос
yt issue find --query 'Queue: TECH AND Status: !Closed AND Assignee: me()'

# Комбо
yt issue find --query 'Updated: today()' --queue TECH --max 50

# Стрим NDJSON для больших выборок
yt issue find --queue TECH --stream | jq -r '.key'
```

Простые фильтры: `--queue`, `--status`, `--assignee`, `--type`, `--priority`, `--tag`, `--component`, `--sprint`, `--query`, `--order`. Все опциональны и комбинируются с `AND`.

### Создание и обновление

```bash
yt issue create --queue TECH --summary "Bug in login" \
  --description "Шаги..." --assignee korolev --priority high

yt issue update TECH-1 --summary "Updated" --priority normal

yt issue transition TECH-1 --list                # доступные переходы
yt issue transition TECH-1 --to in_progress      # выполнить переход
```

### Комментарии и ворклоги

```bash
yt comment add TECH-1 --text "Закоммитил исправление"
yt worklog add TECH-1 --duration PT2H30M --comment "Реализация" \
  --start 2026-04-25T10:00:00+03:00
```

Длительность — ISO 8601: `PT2H` (2 часа), `PT30M` (30 минут), `PT2H30M` (2 ч 30 мин).

### Вложения

```bash
yt attachment list TECH-1
yt attachment upload TECH-1 ./screenshot.png
yt attachment download TECH-1 12345 --out ./out.png
yt attachment download TECH-1 12345 --force          # перезаписать
yt attachment delete TECH-1 12345
```

Скачивание и загрузка — потоковые (не держат файл в памяти).

### Чек-листы

```bash
yt checklist get TECH-1
yt checklist add-item TECH-1 --text "Покрыть тестами"
yt checklist toggle TECH-1 <item-id>            # авто-инверсия checked
yt checklist toggle TECH-1 <item-id> --checked  # явно
yt checklist update TECH-1 <item-id> --text "Новый текст"
yt checklist remove TECH-1 <item-id>
```

### Связи

```bash
yt link list TECH-1
yt link add TECH-1 --to TECH-2 --type relates
yt link remove TECH-1 <link-id>
```

Типы: `relates`, `is dependent by`, `depends on`, `is parent task for`, `is subtask for`, `duplicates`, `is duplicated by`, `is epic of`, `has epic`.

## Формат вывода

Глобальная опция `--format <auto|json|minimal|table>`. По умолчанию `auto` — формат выбирается каскадом:

1. CLI-флаг `--format` (если не `auto`)
2. Env `YT_FORMAT`
3. Поле `default_format` в активном профиле
4. Auto-detect: stdout в pipe/file → `json`; TTY → `table`

| Значение | Когда использовать |
|---|---|
| `json` | Скрипты, AI-агенты, jq-pipelines |
| `minimal` | Одно идентифицирующее поле на строку (`key`/`id`/`login`) |
| `table` | Чтение в терминале — key-value (одиночка) или многоколоночная (массив) |
| `auto` | Default, см. каскад |

```bash
yt user me                       # auto: TTY → table
yt user me | cat                 # auto: pipe → json
yt --format minimal queue list   # компактно для скриптов
yt config set default_format=table   # сохранить предпочтение
YT_FORMAT=json yt user me        # разовый override
```

### Detail view

При `--format=table` (TTY) команды `yt issue get` и `yt comment list` рендерятся как **rich detail view**: header (`KEY · Type · Status · Priority`), bold summary, key-value метаданные, описание с разметкой markdown (заголовки, списки, чек-боксы, code blocks, blockquotes, inline `code`, **bold**, *italic*, ссылки, изображения, авто-линкуемые URL и issue keys).

Поддерживается также Tracker-специфичная разметка: `~~зачёркнутое~~`, `{red}(красный)`, `{yellow}(жёлтый)`, `\(escaped parens\)` и др.

### Pager

Detail view и `comment list` в TTY автоматически прокачиваются через pager (по умолчанию `less -R -F -X`):

- `--no-pager` — отключить разово
- `YT_PAGER=cat` — отключить через env
- `YT_PAGER="less -R"` или `PAGER=more` — кастом

Pager автоматически отключается при pipe/file — там он не имеет смысла.

### Кликабельные ссылки (OSC 8)

Современные терминалы (iTerm2, Ghostty, kitty, WezTerm, VS Code, GNOME Terminal, Apple Terminal) поддерживают OSC 8 — ссылки в выводе становятся cmd/⌘-кликабельными. CLI авто-определяет поддержку по `TERM_PROGRAM`/`COLORTERM`. Force через `YT_HYPERLINKS=1` или отключение через `YT_HYPERLINKS=0`.

Кликабельными становятся: markdown-ссылки, bare URLs, Tracker issue keys (`TECH-1234` → `https://tracker.yandex.ru/TECH-1234`), markdown-images.

## Read-only режим

Гарантия что CLI не выполнит ни один POST/PUT/PATCH/DELETE — полезно для безопасных скриптов и AI-агентов:

- Флаг `--read-only`
- Env `YT_READ_ONLY=1`
- Поле `"read_only": true` в профиле

Любая mutating-команда возвращает `{"error":{"code":"read_only_mode",...}}` на stderr и exit-код **3**, не доходя до сети. POST на `/_search`-эндпоинты Tracker (поиск задач) исключён из блокировки.

## Переменные окружения

| Env | Назначение |
|---|---|
| `YT_PROFILE` | Имя профиля (аналог `--profile`) |
| `YT_OAUTH_TOKEN` | OAuth-токен (перекрывает файл) |
| `YT_IAM_TOKEN` | Готовый IAM-токен |
| `YT_SERVICE_ACCOUNT_ID` / `YT_KEY_ID` / `YT_KEY_FILE` / `YT_KEY_PEM` | Параметры сервис-аккаунта |
| `YT_ORG_TYPE` | `yandex360` или `cloud` |
| `YT_ORG_ID` | ID организации |
| `YT_READ_ONLY` | `1`/`true` — принудительно read-only |
| `YT_CONFIG_PATH` | Путь к конфигу (default `~/.config/yandex-tracker/config.json`) |
| `YT_API_BASE_URL` | Override базового URL Tracker API |
| `YT_TIMEOUT` | HTTP timeout в секундах (default 30) |
| `YT_LOG_FILE` | Путь к файлу wire-log |
| `YT_LOG_RAW` | `1`/`true` — отключить маскирование секретов в wire-log |
| `YT_FORMAT` | Формат вывода (`auto`/`json`/`minimal`/`table`) |
| `YT_PAGER` | Команда pager (default `less -R -F -X`); `cat` или пустая — отключить |
| `YT_HYPERLINKS` | Force-on (`1`) или force-off (`0`) для OSC 8 |
| `YT_TERMINAL_WIDTH` | Override ширины терминала (clamp [40, 200]) |
| `NO_COLOR` | Любое непустое значение отключает ANSI-цвета ([no-color.org](https://no-color.org)) |
| `PAGER` | Системная команда pager (fallback если `YT_PAGER` не задан) |

## Wire-log (отладка HTTP)

Глобальная опция `--log-file <path>` (или env `YT_LOG_FILE`) включает запись всего HTTP-обмена в файл: запрос (метод, URL, заголовки, тело) и ответ (статус, заголовки, тело, время в мс) для каждого вызова Tracker API, IAM-exchange, federated refresh и token-endpoint.

```bash
yt --log-file ~/yt.log user me
YT_LOG_FILE=~/yt.log yt issue get TECH-1
```

Каждая пара запрос/ответ нумеруется (`req-N` / `resp-N`) для сопоставления при параллельных запросах. Файл создаётся с правами `0600` (на POSIX); путь поддерживает `~/`.

**Маскирование (по умолчанию):** `Authorization`, `DPoP`, `Cookie`, `Set-Cookie`, `Proxy-Authorization` → `***`. JSON / form-encoded поля `token`, `refresh_token`, `access_token`, `id_token`, `private_key`, `password`, `code_verifier`, `client_secret`, `code` → `"***"`. Multipart-тела показывают только `Content-Disposition`. Тела > 64 KB обрезаются.

### Raw mode (без маскирования)

Для глубокой отладки (например, диагностика DPoP-mismatch при federated):

```bash
yt --log-raw --log-file ~/yt-raw.log auth login --type federated ...
```

В raw-режиме в файл попадают живые токены, OAuth-коды, DPoP proofs (с автоматическим декодированием header/payload). **Используйте только для отладки и удаляйте файл сразу после.**

## Exit-коды

Ошибки идут на stderr в формате:
```json
{"error":{"code":"...","message":"...","http_status":...,"trace_id":"..."}}
```

| Код | Смысл |
|---|---|
| 0 | Успех |
| 1 | Непойманная/общая ошибка |
| 2 | Некорректные аргументы |
| 3 | `read_only_mode` |
| 4 | `auth_failed` / `forbidden` |
| 5 | `not_found` |
| 6 | `rate_limited` |
| 7 | `server_error` |
| 8 | `network_error` |
| 9 | `config_error` |

## Безопасность

- **Файл конфига** `~/.config/yandex-tracker/config.json` создаётся с правами `0600` на POSIX.
- **Кэш IAM-токенов** `~/.cache/yandex-tracker/iam-tokens.json` — `0600`.
- **DPoP-ключи federated** `~/.config/yandex-tracker/federated-keys/<profile>.pem` — `0600`.
- На Windows POSIX-биты не применяются — полагайтесь на ACL пользователя.
- **Wire-log по умолчанию маскирует** токены, refresh-tokens, OAuth коды, DPoP proofs. `--log-raw` отключает — используйте только для разовой отладки.
- **`config list` / `config get`** маскирует значения `auth.token`, `auth.refresh_token`, `auth.private_key_pem` как `***`.
- **Service Account JWT** подписывается локально (PS256) — приватный ключ не покидает машину; обменивается только на IAM-токен.

## AI-ассистенты

`yt` ставит skill в пять разных AI-ассистентов одной командой — [Claude Code](https://docs.anthropic.com/en/docs/claude-code/skills), [OpenAI Codex](https://developers.openai.com/codex/skills), Gemini CLI, Cursor IDE и GitHub Copilot:

```bash
yt skill install                        # TTY → интерактивный prompt (выбор ассистентов + scope + подтверждение)
yt skill install --no-prompt            # CI/script: ставит во все 5 (Claude+Codex+Gemini+Cursor; Copilot global → skipped)
yt skill install --target claude        # только Claude (явный target — prompt не запускается)
yt skill install --target codex         # только Codex
yt skill install --target gemini        # только Gemini
yt skill install --target cursor        # только Cursor
yt skill install --target copilot --scope project --project-dir .  # Copilot — только project-scope
yt skill install --scope project        # все 5 target'ов в текущий проект
yt skill install --scope project --project-dir /path/to/repo
yt skill status                         # что установлено + какая версия + up_to_date
yt skill update                         # перезаписать установленные локации актуальной версией CLI
yt skill check                          # ручная проверка устаревших skill (TTY → prompt; pipe → JSON warning)
yt skill check --no-prompt              # только статус, без интерактива
yt skill check --reset-prompt-state     # сбросить «больше не спрашивать»
yt skill uninstall                      # удалить
yt skill show --target claude           # напечатать что было бы записано (claude/codex/gemini/cursor/copilot)
```

**Интерактивный режим `yt skill install`.** Если вы запускаете `yt skill install` в обычном терминале без флагов, CLI спросит куда устанавливать skill: предложит чек-лист ассистентов (помечает [✓] те, у кого обнаружен базовый каталог `~/.claude/`, `~/.gemini/`, …), затем спросит scope (global / project) и подтвердит перезапись существующих файлов.

В non-TTY (pipe/CI) или при передаче `--no-prompt` / явных `--target` / `--scope` интерактив отключается и используется тот же default-flow что и раньше (`--target all --scope global`).

| Target  | Scope   | Путь                                                          | Формат файла |
| ------- | ------- | ------------------------------------------------------------- | ------------ |
| Claude  | Global  | `~/.claude/skills/yt/SKILL.md`                                | SKILL.md as-is |
| Claude  | Project | `<projectDir>/.claude/skills/yt/SKILL.md`                     | SKILL.md as-is |
| Codex   | Global  | `~/.agents/skills/yt/SKILL.md`                                | SKILL.md as-is |
| Codex   | Project | `<projectDir>/.agents/skills/yt/SKILL.md`                     | SKILL.md as-is |
| Gemini  | Global  | `~/.gemini/skills/yt/SKILL.md`                                | SKILL.md as-is |
| Gemini  | Project | `<projectDir>/.gemini/skills/yt/SKILL.md`                     | SKILL.md as-is |
| Cursor  | Global  | `~/.cursor/rules/yt.mdc`                                      | translated `.mdc` (`description` / `globs` / `alwaysApply`) |
| Cursor  | Project | `<projectDir>/.cursor/rules/yt.mdc`                           | translated `.mdc` |
| Copilot | Global  | **не поддерживается** — `--target all --scope global` тихо пропускает | — |
| Copilot | Project | `<projectDir>/.github/instructions/yt.instructions.md`        | translated `.instructions.md` (`applyTo: "**"`) |

Claude / Codex / Gemini получают полный SKILL.md (с YAML frontmatter `name: yt`). Cursor и Copilot получают переписанный frontmatter под их форматы — body skill'а одинаковое, версия одинаковая. Маркер `<!-- yt-version: X.Y.Z -->` сохраняется во всех вариантах для определения актуальности.

Skill содержит шпаргалку команд, exit-коды, паттерны парсинга JSON, правила безопасности (read-only, подтверждение перед mutating). После установки AI-ассистент знает как пользоваться `yt` без подсказок пользователя.

После `brew upgrade yt` рекомендовано запустить `yt skill update` — перезапишет установленные локации актуальной версией. Можно положиться на встроенный auto-check: при первом запуске CLI после апдейта в TTY появится prompt с предложением обновить (выбор `Y`/`n`/`never` запоминается); в pipe выводится один раз JSON-warning в stderr. Отключить: `--no-skill-check` или `YT_SKILL_CHECK=0`.

## Сборка из исходников

```bash
git clone https://github.com/RoboNET/YandexTrackerCLI.git
cd YandexTrackerCLI

# Полный прогон (build + tests)
dotnet restore YandexTrackerCLI.slnx
dotnet build YandexTrackerCLI.slnx --configuration Release
dotnet test --project tests/YandexTrackerCLI.Core.Tests/YandexTrackerCLI.Core.Tests.csproj
dotnet test --project tests/YandexTrackerCLI.Tests/YandexTrackerCLI.Tests.csproj

# NativeAOT-бинарь
dotnet publish src/YandexTrackerCLI/YandexTrackerCLI.csproj \
  -c Release -r osx-arm64 --self-contained -o ./dist
```

Требования:
- .NET 10 SDK 10.0.201+
- Тесты на [TUnit](https://github.com/thomhurst/TUnit)
- `Directory.Build.props` включает `TreatWarningsAsErrors`, `IsAotCompatible`

## Версионирование

Версия выводится автоматически из git-тега через [MinVer](https://github.com/adamralph/minver). Формат тега — `vMAJOR.MINOR.PATCH` (например, `v0.1.0`, `v1.0.0`).

```bash
yt --version
# 0.1.0-preview.0+<commit-sha>     ← между тегами (pre-release)
# 0.1.0                             ← если HEAD на теге v0.1.0
```

Релизный workflow:

```bash
# 1. Зафиксировать изменения
git commit -am "feat: новая фича"

# 2. Поставить тег на нужный коммит
git tag v0.2.0

# 3. Собрать с этой версией
dotnet publish src/YandexTrackerCLI/YandexTrackerCLI.csproj \
  -c Release -r osx-arm64 --self-contained -o ./dist
./dist/yt --version   # 0.2.0

# 4. Запушить тег
git push origin v0.2.0
```

CI (`.github/workflows/build.yml`) собирает кросс-платформенные NativeAOT-бинари при пуше в `main` и при push'е тегов. Артефакты доступны во вкладке Actions.

Если коммит между тегами — MinVer автоматически генерирует pre-release версию `<next-patch>-preview.0.<height>+<sha>` (например, `0.1.1-preview.0.5+abc1234` после 5 коммитов поверх `v0.1.0`).

## Лицензия

MIT.
