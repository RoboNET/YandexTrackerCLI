# yt — Yandex Tracker CLI

Кроссплатформенный CLI-клиент для [Яндекс Трекера](https://yandex.ru/support/tracker/ru/api-ref/about-api), собранный через NativeAOT.

- **Один бинарь ~10 МБ** — без зависимостей, без рантайма, мгновенный старт.
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
- [Ограничение очередей](#ограничение-очередей)
- [Ограничение области записи](#ограничение-области-записи)
- [Границы политик профиля](#границы-политик-профиля)
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

Размер бинаря в архиве — около 4–5 МБ (распакованный — около 10 МБ).

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
yt config set default_format table --profile work
yt config set read_only true --profile work           # сделать профиль read-only
yt config set allowed_queues DEV,QA --profile work    # ограничить доступные очереди
yt config get allowed_queues --profile work           # → "DEV,QA"
yt config set allowed_queues DEV --profile work       # сузить список — можно
yt config set allowed_queues "" --profile work        # снять ограничение — НЕЛЬЗЯ (exit 10)
yt config set allowed_write_issues DEV-42 --profile work  # писать только в эти задачи
yt config get allowed_write_issues --profile work         # → "DEV-42"
```

Доступные ключи для `config set`: `org_type`, `org_id`, `read_only`, `default_format`, `allowed_queues`, `allowed_write_issues`.

**Политики профиля через `config set` можно только ужесточить.** `read_only: true → false`, снятие или расширение `allowed_queues`/`allowed_write_issues` отклоняются с `policy_violation` (exit 10). Иначе ограничения не было бы вовсе: командную строку формирует тот же вызывающий, которого ограничивают. Ослабить политику можно только пересоздав профиль (см. [Границы политик профиля](#границы-политик-профиля)).

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
yt attachment download TECH-1 12345 --out -          # в stdout (pipe)
yt attachment delete TECH-1 12345
```

Скачивание и загрузка — потоковые (не держат файл в памяти).

С `--out -` вложение стримится в stdout: там оказываются только байты файла, сводка
`downloaded <n> bytes` уходит в stderr. `--out - --force` отвергается (exit 2).
Обрыв пайпа потребителем (`| head -c 100`) — единственный штатный случай досрочной
остановки: exit 0, но stderr помечает передачу как
`downloaded <n> bytes (truncated: consumer closed the pipe)`. Любая другая I/O-ошибка —
оборванное тело ответа, ошибка записи в stdout (например ENOSPC при `> file`) или тело
короче объявленного `Content-Length` — даёт `network_error` (exit 8): усечённые данные
никогда не выдаются за успех. Файл с именем `-` сохраняется как `--out ./-`.

При записи в файл действует то же правило, и держится оно не уборкой после сбоя, а порядком
операций: вложение пишется во временный файл `<target>.part-<random>` в каталоге цели и
переезжает под целевое имя (атомарным `rename`) только после успешной проверки
`Content-Length`. Отсюда следует, что:

- файл под целевым именем всегда означает полностью скачанное вложение — в том числе после
  `kill -9` или отключения питания, когда никакой обработчик сигналов уже не поможет;
- `--force` не разрушает прежнее содержимое файла заранее: оно заменяется ровно в момент
  успеха, а оборванная попытка оставляет старый файл нетронутым;
- оборванное скачивание (отмена — exit 11; ошибка I/O или тело короче `Content-Length` —
  exit 8) удаляет свой временный файл и пишет в stderr `removed incomplete download: <tmp>`.
  Удаляется всегда собственный временный файл, никогда не целевой.

Цена схемы — нужно право записи на каталог, а не только на сам файл: если создать временный
файл нельзя, команда падает с `network_error` (exit 8), не тронув цель. Симлинк
разыменовывается: обновляется файл по ссылке, а не подменяется сама ссылка. Специальные
приёмники (`/dev/null`, `/dev/tty`, `NUL` в Windows) не переименовываются — в них пишем
напрямую и ничего не удаляем, как и в режиме `--out -`.

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

Флаг `--read-only` при `yt auth login` записывается в создаваемый профиль (`read_only: true`) — так профиль для CI или AI-агента сразу заводится только на чтение:

```bash
yt --read-only auth login --type service-account \
  --sa-id <sa-id> --key-id <key-id> --key-file ./sa-key.pem \
  --org-type cloud --org-id <org-id> --profile ci
```

Три источника складываются по «ужесточению»: флаг вызова и env могут только включить read-only, но не выключить его, если он задан в профиле. `yt config set read_only false` при включённом `read_only` отклоняется (exit 10) — снять флаг можно только пересоздав профиль через `yt auth login`, см. [Границы политик профиля](#границы-политик-профиля).

## Ограничение очередей

Профиль может нести список очередей, за пределы которого не выходит ни на чтение, ни на запись:

```bash
yt config set allowed_queues DEV,OPS,QA --profile ci
yt auth status --profile ci
# {"profile":"ci",...,"read_only":false,"allowed_queues":["DEV","OPS","QA"]}
```

Ограничение живёт в профиле, а не в командной строке, поэтому его нельзя «забыть» при вызове. Что оно делает:

- **Адресные обращения** к задаче или очереди вне списка блокируются на уровне HTTP-конвейера, до выхода в сеть — и на чтение, и на запись (`issues/{KEY}` и всё вложенное, `queues/{KEY}` и всё вложенное, параметр `queue` в query). Ответ — явная ошибка политики с exit-кодом **10**, а не `not_found`:
  ```json
  {"error":{"code":"policy_violation","message":"queue 'OPS' is outside allowed_queues of profile 'ci' (allowed: DEV, QA)"}}
  ```
- **Поиск не ломается**: `yt issue find --yql "..."` выполняется целиком, но задачи из очередей вне списка вырезаются из вывода; `--max` считает выданные наружу задачи. Явно названная запрещённая очередь (`--queue OPS`) даёт `policy_violation`.
- **`yt queue list`** отдаёт только разрешённые очереди, **`yt suggest`** — только разрешённые задачи (в том числе без `--queue`, когда сервер ищет по всем очередям).
- **`yt link list DEV-1`** отдаёт связи только с разрешёнными очередями: сам запрос допустим, но в ответе перечислены ключи и темы связанных задач.
- **Тело запроса тоже проверяется** там, где очередь едет не в URL: `issue create --queue`, `issue move --to-queue`, `component create`, `version create` — включая форму `--json-file`/`--json-stdin`.
- **`component`/`version` по идентификатору** (`get`, `update`, `delete`) — очереди в URL нет, поэтому CLI сначала доспрашивает ресурс (`GET components/{id}`), берёт из ответа очередь-владельца и сверяет со списком. Отсутствующий ресурс остаётся `not_found` (exit 5); ответ, в котором очередь не определяется, отклоняется по политике. Дополнительный запрос делается только при действующем ограничении.
- **`yt issue batch`** при действующем ограничении требует явный список `issues` в payload; payload с полем `query` отклоняется целиком — даже если рядом стоит разрешённый `issues`, потому что набор задач по запросу выбирает сервер.
- Сравнение ключей очередей — без учёта регистра. Env-переменной для `allowed_queues` нет намеренно: окружение так же управляемо вызывающим, как и флаг.

## Ограничение области записи

Между «только чтение» и «полный доступ» есть промежуточный режим: автоматике нужно ответить комментарием ровно в ту задачу, из которой её позвали, и больше никуда. Для этого профиль может нести список задач, открытых **на запись**:

```bash
yt config set allowed_write_issues DEV-42 --profile ci

# или на один запуск — в CI задача каждый раз своя.
# Если профиль уже несёт список, переменная его только сужает (пересечение).
YT_ALLOWED_WRITE_ISSUES=DEV-42,DEV-43 yt comment add DEV-42 --text "готово"

yt auth status --profile ci
# {"profile":"ci",...,"allowed_write_issues":["DEV-42"],"allowed_write_issues_source":"profile"}
```

Как это работает:

- **Ограничивается только запись.** Чтение любых задач (в пределах `allowed_queues`) продолжает работать — этим политика отличается от read-only, при котором комментарий не прошёл бы вовсе. `POST .../_search` считается чтением и проходит.
- **Проверка стоит в HTTP-конвейере**, до выхода в сеть: мутирующий запрос (`POST`/`PUT`/`PATCH`/`DELETE`) разрешён, только если он адресован `issues/{KEY}/...` с `KEY` из списка. Ключи в URL декодируются до сравнения (обход через `%4F` не работает), сравнение — без учёта регистра.
- **Запрет по умолчанию для всего остального**: создание задачи (`POST issues`), `yt issue batch` (`POST bulkchange`), мутации очередей и автоматизаций (`queues/{KEY}/triggers`, `autoactions`, макросы) и любой другой мутирующий путь, по которому нельзя доказать попадание в разрешённую задачу.
  ```json
  {"error":{"code":"policy_violation","message":"issue 'OPS-7' is outside allowed_write_issues of profile 'ci' (allowed: DEV-42)"}}
  ```
- **Поля рассылки уведомлений запрещены.** `summonees` и `maillistSummonees` призывают людей и отправляют письма адресатам за пределами задачи, поэтому при действующем списке они блокируются. Проверяется фактическое JSON-тело запроса на любом уровне вложенности — не только разобранные флаги команды, так что через `--json-file`/`--json-stdin` их не протащить.
- **Env только сужает список профиля.** `YT_ALLOWED_WRITE_ISSUES` не может расширить область записи ни при каком сочетании значений — как и `read_only` с `allowed_queues`:

  | Профиль | Переменная | Действует |
  |---|---|---|
  | нет списка | `DEV-42,DEV-43` | `DEV-42,DEV-43` — переменная задаёт ограничение с нуля (штатный CI-сценарий: задача каждый раз своя) |
  | `DEV-42,DEV-43` | `DEV-43,OPS-7` | `DEV-43` — **пересечение**; `OPS-7` на запись не открывается |
  | `DEV-42` | `OPS-7` (не пересекается) | `config_error`, exit 9 — команда не выполняется вовсе |
  | `DEV-42` | не задана или пробелы | `DEV-42` — список профиля |

  Сравнение ключей — без учёта регистра (`dev-42` в переменной пересекается с `DEV-42` в профиле), в выводе остаётся написание из профиля.
- **Пустое пересечение — не «ограничений нет», а отказ.** Пустой список во всей модели означает отсутствие ограничения, поэтому вернуть его при непересекающихся списках значило бы открыть запись всюду. Вместо этого резолв профиля падает с `config_error` (exit 9) и называет оба списка — это же увидит и `yt auth status`.
- **Пустая переменная ≠ снятие ограничения.** Значение из одних пробелов игнорируется (действует список профиля), а значение из одних разделителей (`","`, `",,"`) — ошибка `config_error` (exit 9), а не молчаливое снятие: иначе опечатка открывала бы запись во все задачи.
- **`allowed_write_issues_source` в `yt auth status`** говорит, откуда взялся действующий список: `profile`, `env` (профиль ограничения не нёс) или `profile+env` (действует пересечение).
- Отсутствие ключа в профиле = ограничения нет. Снять уже выставленный список через `yt config set allowed_write_issues ""` нельзя — только сузить; сброс идёт через пересоздание профиля (ниже).

## Границы политик профиля

Политики (`read_only`, `allowed_queues`, `allowed_write_issues`) — это граница, которую утилита держит **против собственного вызывающего**: командную строку формирует он, поэтому проверка обязана жить внутри `yt`, а не в инструкции «не передавай такой флаг».

**Изменение политики.** `yt config set` умеет только ужесточать:

| Изменение | Результат |
|---|---|
| `read_only false → true` | разрешено |
| `read_only true → false` | `policy_violation`, exit 10 |
| `allowed_queues` не задан → `DEV,QA` | разрешено |
| `allowed_queues DEV,QA → DEV` (подмножество) | разрешено |
| `allowed_queues DEV,QA → DEV,OPS` (добавление) | `policy_violation`, exit 10 |
| `allowed_queues DEV,QA → ""` (снятие) | `policy_violation`, exit 10 |
| то же для `allowed_write_issues` | так же |

**Сброс политики — только пересоздание профиля:**

```bash
# завести профиль заново: политики берутся ТОЛЬКО из флагов этого вызова
yt auth login --profile ci --type oauth --token <token> --org-type cloud --org-id <org-id>
# → read_only=false, allowed_queues и allowed_write_issues сняты
# → с --read-only профиль сразу заводится только на чтение
```

Это работает как барьер, потому что `yt config get` маскирует `auth.token` и `auth.private_key_pem`: без самих креденшелов повторный login не сделать. `yt auth logout` очищает токен профиля (метаданные и политики при этом остаются) — связка `logout` + `login` тоже возвращает профиль в исходное состояние, потому что политики задаёт именно `login`.

`yt auth relogin` (и автоматический re-login при неудачном DPoP-refresh) — **не** путь сброса: он обновляет только токены, все политики профиля сохраняются как есть.

**Честная оговорка: политики защищают от вызывающего, который ходит через CLI.** Граница держится ровно до тех пор, пока у него нет прямой записи в файл конфига:

```
~/.config/yandex-tracker/config.json     # или $XDG_CONFIG_HOME/yandex-tracker/config.json,
                                         # или путь из $YT_CONFIG_PATH
```

Тот, кто может редактировать этот файл, снимет любую политику одной правкой JSON. `yt` создаёт файл с правами `0600` (только владелец); каталог рекомендуется держать в `0700`, а процесс с ограниченным профилем запускать под пользователем, у которого нет прав на запись в этот файл (и который не может подменить `YT_CONFIG_PATH`).

**Вторая честная оговорка: сырые креденшелы обесценивают политики целиком.** Пока сервис-аккаунтные креды доступны процессу вызывающего — например, `TRACKER_SA_SERVICE_ACCOUNT_ID`, `TRACKER_SA_KEY_ID`, `TRACKER_SA_PRIVATE_KEY`, `TRACKER_CLOUD_ORG_ID` лежат в переменных окружения CI-джобы и наследуются процессом агента, — профильные политики не являются границей вообще:

```bash
# ничего не обходя, из того, что уже есть в окружении:
YT_CONFIG_PATH=/tmp/mine.json yt auth login --type service-account \
  --sa-id "$TRACKER_SA_SERVICE_ACCOUNT_ID" --key-id "$TRACKER_SA_KEY_ID" \
  --key-pem "$TRACKER_SA_PRIVATE_KEY" --org-type cloud --org-id "$TRACKER_CLOUD_ORG_ID"
# → профиль без единой политики, с полными правами токена
```

Это не эксплойт, а штатный `auth login`: политики живут в профиле, а профиль заводится из креденшелов. **Политики профиля — второй слой защиты, а не первый.** Они предполагают, что у вызывающего нет ни записи в файл конфига, ни сырых креденшелов. Если креды всё-таки лежат в окружении процесса, границу обязана держать вызывающая сторона: не отдавать креды процессу с ограниченным профилем (передавать ему только готовый конфиг), контролировать `YT_CONFIG_PATH` и не давать подменить его, а на стороне сервиса — выдавать такому агенту отдельный сервис-аккаунт с узкими серверными правами.

**Известные пределы (что политики НЕ закрывают):**

- **Файл конфига и `YT_CONFIG_PATH`** — см. выше: прямая правка обходит всё.
- **Сырые креденшелы в окружении вызывающего** — см. выше: с ними `yt auth login` со своим `YT_CONFIG_PATH` даёт профиль без политик.
- **Boards, sprints, projects, глобальные справочники и users** — сущности вне модели очередей: `board list`, `sprint list`, `project list`, глобальные `field list`/`ref *` и `user *` по `allowed_queues` не фильтруются и показывают названия объектов чужих очередей. Исключение — **локальные** поля очереди: `field list --queue OPS` и `field get --queue OPS` адресуют `queues/OPS/localFields`, поэтому отклоняются как обычное адресное обращение (exit 10).
- **Ссылки на чужие задачи внутри разрешённой** — `issue get` (поля `parent`, `links`) и `issue changelog` печатают ответ API как есть, поэтому показывают не только *ключи* задач из очередей вне списка, но и их **темы** (`display`). Сами эти задачи прочитать нельзя: `issue get OPS-1` даёт `policy_violation` (exit 10) и запрос в сеть не уходит. Отдельная выдача связей (`link list`) отфильтрована — там связи с чужими очередями вырезаются целиком.
- **`allowed_write_issues` и создание задач** — политика запрещает всё, что не адресовано разрешённой задаче, поэтому создать задачу с таким профилем нельзя вовсе; промежуточного «можно создавать в очереди X» нет.
- **Серверные права** политики не заменяют: это ограничение клиента поверх токена. Токен с широкими правами остаётся токеном с широкими правами — если он утёк, политика профиля утекшего не защищает.

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
| `YT_ALLOWED_WRITE_ISSUES` | Список ключей задач через запятую: запись разрешена только в них. Список профиля **сужает** (действует пересечение), расширить не может. Значение без единого ключа (`","`) или список, не пересекающийся со списком профиля, — `config_error` |
| `YT_CONFIG_PATH` | Путь к конфигу (default `~/.config/yandex-tracker/config.json`) |
| `YT_API_BASE_URL` | Override базового URL Tracker API |
| `YT_TIMEOUT` | HTTP timeout в секундах (default 30). Допустимо целое от 1 до 86400; всё остальное (`0`, отрицательное, нечисловое) отвергается как `invalid_args` (exit 2) — так же, как и `--timeout` с тем же значением, а не игнорируется молча |
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
| 10 | `policy_violation` (очередь вне `allowed_queues` профиля либо запись вне `allowed_write_issues`) |
| 11 | `cancelled` — команда прервана: Ctrl-C/SIGINT, SIGTERM или истёкший HTTP-таймаут |
| 130 | Процесс убит SIGINT самой ОС — CLI сигнал не перехватил, JSON-ошибки нет (см. ниже). Тот же код возвращает `yt suggest` при выходе по `Esc` — это штатный отказ от выбора |
| 143 | Процесс убит SIGTERM самой ОС — CLI сигнал не перехватил, JSON-ошибки нет |

**Отмена в обёртках проверяется как «11, 130 или 143».** Ctrl-C даёт 11 в подавляющем
большинстве случаев, но не во всех: пока команда не дошла до первой асинхронной операции,
сигнал не перехватывается (см. ниже), и код ставит ОС. Второй Ctrl-C всегда даёт 130 —
это его смысл. Единый код здесь недостижим без того, чтобы сделать CLI неубиваемым на время
grace-периода, поэтому контракт называет все три кода.

Отмена перехватывается на верхнем уровне CLI, одинаково для всех команд: наружу уходит
та же JSON-ошибка на stderr, что и у остальных отказов, а не стектрейс .NET. Сообщение
различает причины — `timed out after <n>s …` для сработавшего таймаута (с текущим значением
и подсказкой поднять его через `--timeout <секунды>` или `YT_TIMEOUT`) и
`cancelled by SIGINT|SIGTERM …` для прерывания пользователем или супервизором. Когда
признаков не хватает, текст прямо говорит, что причину определить не удалось, вместо того
чтобы назвать наугад. Exit 11 всегда означает «результата нет»: вывод неполон по
определению, а для мутирующей команды — ещё и неизвестно, применилась ли она.

Сигнал обрабатывается только тогда, когда есть что сворачивать. Если команда не отвечает
на отмену дольше 2 секунд, ожидание прекращается и возвращается тот же код 11 с пометкой,
что команда была завершена принудительно. Если же сигнал приходит до того, как команда
дошла до первой асинхронной операции (например, она блокирующе читает stdin), CLI его
не перехватывает вовсе: процесс завершает ОС обычным образом — 130 для SIGINT, 143 для
SIGTERM, без JSON-ошибки. Подавлять сигнал в этом случае нельзя, иначе Ctrl-C переставал
бы работать.

**Второй Ctrl-C убивает немедленно.** Повторный сигнал не подавляется и уходит к ОС, не
дожидаясь ни grace-периода, ни чего-либо ещё: обработчик первого сигнала не блокирует поток
доставки сигналов, поэтому второй доходит сразу. То же верно для SIGTERM от супервизора.

## Безопасность

- **Файл конфига** `~/.config/yandex-tracker/config.json` создаётся с правами `0600` на POSIX.
- **Кэш IAM-токенов** `~/.cache/yandex-tracker/iam-tokens.json` — `0600`.
- **DPoP-ключи federated** `~/.config/yandex-tracker/federated-keys/<profile>.pem` — `0600`.
- На Windows POSIX-биты не применяются — полагайтесь на ACL пользователя.
- **Wire-log по умолчанию маскирует** токены, refresh-tokens, OAuth коды, DPoP proofs. `--log-raw` отключает — используйте только для разовой отладки.
- **`config list` / `config get`** маскирует значения `auth.token`, `auth.refresh_token`, `auth.private_key_pem` как `***`.
- **Service Account JWT** подписывается локально (PS256) — приватный ключ не покидает машину; обменивается только на IAM-токен.

## AI-ассистенты

Skill для AI-ассистентов лежит в репозитории — [`.claude/skills/yt/SKILL.md`](.claude/skills/yt/SKILL.md). Это и есть доставляемый артефакт: он содержит шпаргалку команд, exit-коды, паттерны парсинга JSON и правила безопасности (read-only, подтверждение перед mutating). После установки ассистент умеет пользоваться `yt` без подсказок пользователя.

Skill можно поставить и до установки самого `yt` — инструкция по установке бинаря есть внутри skill'а, ассистент доставит его сам при первом использовании.

### Claude Code — маркетплейс плагинов

Репозиторий одновременно является плагином Claude Code ([`.claude-plugin/plugin.json`](.claude-plugin/plugin.json)) и источником плагинов ([`.claude-plugin/marketplace.json`](.claude-plugin/marketplace.json)), поэтому его можно добавить как маркетплейс и поставить плагин по имени:

```bash
claude plugin marketplace add RoboNET/YandexTrackerCLI
claude plugin install yt@yandex-tracker-cli
```

То же самое из сессии Claude Code: `/plugin marketplace add RoboNET/YandexTrackerCLI`, затем `/plugin install yt@yandex-tracker-cli`.

Здесь `yandex-tracker-cli` — имя маркетплейса (поле `name` в `marketplace.json`), `yt` — имя плагина (поле `name` в `plugin.json`).

Обновление: `claude plugin update yt@yandex-tracker-cli` (или `/plugin update` в сессии).

### Остальные ассистенты — универсальные установщики

Единого каталога, который читали бы все агенты, нет: у каждого свой путь и свой формат. Универсальные установщики знают эту раскладку и сами кладут skill туда, куда нужно конкретному агенту; `~/.agents/skills/` по спецификации [Agent Skills](https://agentskills.io) — общая раскладка, которую читает часть агентов (Codex и те, кто следует спецификации), но не все.

```bash
npx skills add RoboNET/YandexTrackerCLI                                # интерактивный выбор агентов
npx skills add RoboNET/YandexTrackerCLI -g -a claude-code -a codex -y  # non-interactive / CI

gh skill install RoboNET/YandexTrackerCLI                              # то же самое через gh (нужен gh >= 2.90.0)
```

Команда `gh skill` появилась в GitHub CLI 2.90.0. На более старом `gh` она отваливается с `unknown command "skill"` — обновите `gh` или используйте `npx skills add`.

### Вручную

Скопировать файл туда, где его читает ваш ассистент (каталог сначала нужно создать — `curl` его не заводит):

```bash
mkdir -p ~/.agents/skills/yt
curl -sSfL https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/v0.6.0/.claude/skills/yt/SKILL.md \
  -o ~/.agents/skills/yt/SKILL.md
```

Пин на тег работает начиная с **v0.6.0**: до него в теге лежала версия предыдущего релиза, а `marketplace.json` в репозитории ещё не было. На более ранние теги пиниться не нужно.

Типовые пути: `~/.claude/skills/yt/SKILL.md` (Claude Code), `~/.agents/skills/yt/SKILL.md` (Codex и всё, что следует Agent Skills), `~/.gemini/skills/yt/SKILL.md` (Gemini CLI), `<project>/.github/instructions/` (GitHub Copilot — только project-scope).

### CI и Dockerfile

Пиньте версию на релизный тег (v0.6.0 и новее) — иначе образ пересобирается с плавающим содержимым `main`:

```dockerfile
RUN mkdir -p /root/.agents/skills/yt \
 && curl -sSfL https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/v0.6.0/.claude/skills/yt/SKILL.md \
      -o /root/.agents/skills/yt/SKILL.md
```

Для плагинной установки Claude Code пин выражается полем `ref` внутри объекта `source` в записи маркетплейса — так плагин федерируется в свой или чужой маркетплейс:

```json
{
  "name": "yt",
  "source": { "source": "url", "url": "https://github.com/RoboNET/YandexTrackerCLI.git", "ref": "v0.6.0" }
}
```

Без `ref` запись отслеживает `main`: `claude plugin marketplace add <repo>` тоже отслеживает ветку по умолчанию, отдельного флага для пина у этой команды нет. Краткая форма без пина — `{ "name": "yt", "source": { "source": "github", "repo": "RoboNET/YandexTrackerCLI" } }`.

### Версия skill'а

Сразу после frontmatter в SKILL.md стоит маркер `<!-- yt-version: X.Y.Z -->`. Он проставляется релизным workflow `prepare-release` **до** создания тега — одновременно с `version` в `plugin.json`. Поэтому релизный тег несёт согласованные версии, и любой путь доставки (маркетплейс, `npx skills add`, `gh skill install`, ручное копирование) с пином на тег даёт одинаковое содержимое.

Между релизами `main` уходит вперёд маркера: правило «изменил публичный API — обнови SKILL.md» действует постоянно, а версия проставляется только в релизном коммите. Если нужна предсказуемость — пиньте на тег.

### Миграция: команд `yt skill *` больше нет

Начиная с **0.6.0** CLI не устанавливает skill (в 0.5.0 и раньше команды ещё есть). Удалены команды `yt skill install`, `status`, `check`, `update`, `uninstall`, `show`, а вместе с ними авто-проверка актуальности при каждом запуске `yt`. Причина: доставка skill'ов — задача менеджера плагинов и универсальных установщиков, а собственный установщик внутри бинаря дублировал их и требовал отдельной инфраструктуры (маркер версии в бинаре, состояние prompt'а, разбор чужих форматов).

Что изменилось для вас:

- **Файлы, установленные прежними версиями, остались на диске и больше не обновляются.** Удалите их вручную и поставьте skill заново одним из способов выше:

  ```bash
  rm -rf ~/.claude/skills/yt/ \
         ~/.agents/skills/yt/ \
         ~/.gemini/skills/yt/ \
         ~/.cursor/rules/yt.mdc
  # project-scope Copilot — в каждом репозитории отдельно:
  rm -f <project>/.github/instructions/yt.instructions.md
  ```

  Проверьте также project-scope копии в своих репозиториях: `<project>/.claude/skills/yt/`, `<project>/.agents/skills/yt/`, `<project>/.gemini/skills/yt/`, `<project>/.cursor/rules/yt.mdc`.

- **Env-переменная `YT_SKILL_CHECK` и флаг `--no-skill-check` больше не действуют.** Флаг теперь приводит к ошибке `Unrecognized command or argument`; уберите его из скриптов и алиасов. Env-переменная просто игнорируется — отключать нечего, авто-проверки нет.
- **Файл состояния `~/.cache/yandex-tracker/skill-prompt-state.json` больше не используется** — можно удалить.

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

Релизный workflow — **только через `prepare-release`**, теги руками не ставим:

```bash
gh workflow run prepare-release --field version=0.2.0
# или: Actions → prepare-release → Run workflow
```

`prepare-release` (`.github/workflows/prepare-release.yml`) валидирует версию (строгий semver без ведущих нулей), требует зелёного прогона CI на вершине `main`, проставляет версию в `.claude-plugin/plugin.json` и в маркер `<!-- yt-version: ... -->` в `.claude/skills/yt/SKILL.md`, вливает это в `main` через PR со squash-merge и только потом вешает тег `v0.2.0` на итоговый коммит.

Тег, поставленный руками в обход `prepare-release`, оставит `plugin.json` и маркер в SKILL.md на версии предыдущего релиза — то есть пин на этот тег отдал бы skill с чужой версией в маркере (это и была [issue #20](https://github.com/RoboNET/YandexTrackerCLI/issues/20)). Есть `dry_run`, чтобы посмотреть diff без коммита и тега.

Проверить, что версия соберётся как ожидается, локально можно и без тега:

```bash
dotnet publish src/YandexTrackerCLI/YandexTrackerCLI.csproj \
  -c Release -r osx-arm64 --self-contained -o ./dist
./dist/yt --version
```

CI (`.github/workflows/build.yml`) собирает кросс-платформенные NativeAOT-бинари при пуше в `main` и при push'е тегов. Артефакты доступны во вкладке Actions.

Если коммит между тегами — MinVer автоматически генерирует pre-release версию `<next-patch>-preview.0.<height>+<sha>` (например, `0.1.1-preview.0.5+abc1234` после 5 коммитов поверх `v0.1.0`).

## Лицензия

MIT.
