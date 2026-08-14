---
name: yt
description: Use when interacting with Yandex Tracker (Яндекс Трекер) — searching/reading issues, comments, worklogs, attachments, checklists, links, boards, sprints, projects. Triggers on words like "Яндекс Трекер", "Tracker", "ишью", "задача в трекере", issue keys like "TECH-1234", or URLs like tracker.yandex.ru/MAN-123. Use yt CLI for both reading and mutating operations; pass --read-only or YT_READ_ONLY=1 for safe browsing.
---

<!-- yt-version: 0.6.2 -->

# yt — Yandex Tracker CLI

`yt` — это NativeAOT CLI для Яндекс Трекера. Один бинарь, JSON-вывод по умолчанию (auto-detect: TTY → table, pipe → json), стабильные exit-коды. Используй его для любых задач связанных с Tracker'ом.

## Установка и проверка

Проверь что установлен:

```bash
yt --version
```

Если нет — установи одним из способов (выбери по ОС):

```bash
# macOS (Apple Silicon) и Linux (x64/arm64) — Homebrew
brew install RoboNET/yt/yt

# Windows — Scoop
scoop bucket add yt https://github.com/RoboNET/scoop-yt
scoop install yt
```

```powershell
# Windows без Scoop — PowerShell-инсталлер (yt.exe → %LOCALAPPDATA%\Programs\yt + user PATH)
irm https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/main/install.ps1 | iex
```

Без пакетных менеджеров — готовый бинарь из [GitHub Releases](https://github.com/RoboNET/YandexTrackerCLI/releases/latest) (архивы: `yt-linux-x64.tar.gz`, `yt-linux-arm64.tar.gz`, `yt-linux-musl-x64.tar.gz`, `yt-linux-musl-arm64.tar.gz`, `yt-osx-arm64.tar.gz`, `yt-win-x64.zip`):

```bash
RID=linux-x64   # подставь платформу; на Alpine и других musl-образах — linux-musl-x64 / linux-musl-arm64
curl -fsSL -o yt.tar.gz "https://github.com/RoboNET/YandexTrackerCLI/releases/latest/download/yt-${RID}.tar.gz"
tar -xzf yt.tar.gz && sudo mv yt /usr/local/bin/yt
# macOS: если бинарь в карантине — xattr -d com.apple.quarantine /usr/local/bin/yt
```

Из исходников: `dotnet publish` (см. README репозитория).

## Базовые правила

- **JSON-вывод по умолчанию для скриптов** — auto-detect: при pipe всегда compact JSON. Не пытайся парсить таблицы — всегда работай с `yt ... | jq` или `python -c`.
- **Exit-коды** — стабильные (см. ниже): 0 = успех, 2 = плохие аргументы, 3 = read-only заблокирован, 4 = auth_failed/forbidden, 5 = not_found, 6 = rate_limited, 7 = server_error, 8 = network_error, 9 = config_error, 10 = policy_violation (очередь вне `allowed_queues` профиля, запись вне `allowed_write_issues` либо внешний эффект при `external_effects: false`), 11 = cancelled (Ctrl-C или сработавший таймаут), 130/143 = процесс убит SIGINT/SIGTERM самой ОС. Отмену проверяй как «11, 130 или 143».
- **Exit 11 = результата нет.** Команда не доработала до конца: пришёл Ctrl-C/SIGTERM либо истёк HTTP-таймаут. Всё, что успело напечататься в stdout, — обрывок, а не ответ; повторяй команду, а не разбирай её вывод. Сообщение в stderr называет причину, а при таймауте — ещё и текущее значение и способ его поднять (`--timeout <секунды>`, env `YT_TIMEOUT`). Для мутирующих команд exit 11 означает «неизвестно, применилось ли» — перед повтором проверь состояние задачи. Значение таймаута — целое от 1 до 86400; `--timeout 0` или `YT_TIMEOUT=-5` отвергаются как `invalid_args` (exit 2), а не игнорируются молча.
- **Профиль** — выбирается через `--profile <name>` или `YT_PROFILE`. Если в конфиге ровно один профиль — он используется автоматически. Если несколько — нужно либо указать, либо предварительно `yt config profile <name>`.
- **Перед мутирующими действиями** — спроси пользователя подтверждение (создание issue, удаление, изменение статуса, добавление комментария от его имени).
- **Страховка `--read-only`** — необязательный пояс безопасности для AI-агентов или массовых операций. Блокирует POST/PUT/PATCH/DELETE до выхода в сеть, возвращает exit 3. Пропускает POST на `_search`-эндпоинты (поиск через `issue find`). Для обычных read-команд (`issue get`, `comment list`, `attachment list`, …) флаг не нужен — они и так GET-запросы.
- **Политики профиля (`read_only`, `allowed_queues`, `allowed_write_issues`, `external_effects`)** — то же ограничение, но как свойство самих креденшелов, а не флаг вызова: их нельзя «забыть» в командной строке. `allowed_write_issues` ограничивает **только запись** (чтение остаётся), поэтому годится для автоматики, которой надо ответить ровно в одну задачу. `external_effects: false` запрещает **инициировать рассылку и интеграции явно** — призыв (`summonees`/`maillistSummonees`) и мутации автоматизаций (`triggers`/`autoactions`/`macros`), — оставляя обычную работу с задачами. См. раздел «Политики профиля» ниже.

## Переменные окружения

Полный список того, что читает CLI. Ничего, кроме перечисленного, на поведение команд не влияет.

### Чем ограничить работу

| Переменная | Значения | Поведение |
|---|---|---|
| `YT_READ_ONLY` | включают `1`/`true`/`yes`/`on`, выключают `0`/`false`/`no`/`off` — регистр не важен, пробелы по краям обрезаются (`on`, `YES`, `TrUe`, `" 1"` включают). Пусто или одни пробелы = переменная не задана. **Любое другое значение — `config_error` (exit 9)**, а не тихое «выключено» | Блокирует POST/PUT/PATCH/DELETE до выхода в сеть (exit 3). Складывается по ИЛИ с `--read-only` и с `read_only` профиля: **только ужесточает**, снять политику профиля через `YT_READ_ONLY=0` нельзя |
| `YT_ALLOWED_WRITE_ISSUES` | список ключей через запятую (`DEV-42,DEV-43`), сравнение без учёта регистра | Ограничивает область записи. **Только сужает:** профиль без списка + env = список из env; профиль со списком + env = **пересечение**; непересекающиеся списки = `config_error` (exit 9); значение без единого ключа (`","`, `",,"`) = `config_error` (exit 9); значение из одних пробелов игнорируется. Подробности и таблица случаев — в «Политики профиля» |
| `YT_EXTERNAL_EFFECTS` | те же написания, что у `YT_READ_ONLY`; разбор такой же строгий (мусор — `config_error`, exit 9) | `0`/`false`/`no`/`off` запрещает инициировать рассылку и интеграции явно: `summonees`/`maillistSummonees` в теле и мутации `triggers`/`autoactions`/`macros` (exit 10). **Только ужесточает:** `1`/`true` не снимает `external_effects: false` профиля |
| `YT_ALLOWED_QUEUES` | **не существует** | Ограничение очередей задаётся **только** в профиле (`yt config set --profile ci allowed_queues DEV,QA`). Env-переменной нет намеренно: окружение так же управляемо вызывающим, как и командная строка, поэтому ограничением быть не может. Не пытайся её задать — она будет проигнорирована |

**Коды отказа при мусорном значении разные — скрипту нужно знать оба.** Мусор в окружении (`YT_READ_ONLY=мусор`) — это `config_error` (**exit 9**), а мусор в аргументе команды (`yt config set read_only мусор`) — `invalid_args` (**exit 2**). Асимметрия намеренная: в первом случае неверно задано окружение процесса, во втором — аргумент вызова. Проверяй оба кода, если скрипт разбирает причину отказа. То же самое и у `YT_LOG_RAW`.

**Граница проходит не здесь.** Эти переменные ограничивают того, кто **запускает** процесс. Если окружение вызова формирует сам вызывающий (а AI-агент обычно формирует), то `YT_READ_ONLY` и `YT_ALLOWED_WRITE_ISSUES` — его собственная страховка от случайной мутации, а не барьер: снимаются они так же легко, как ставятся. Настоящей границей остаётся то, что вызывающий изменить не может, — политики профиля в конфиге, к которому у него нет записи, и серверные права токена (см. «Что политики НЕ закрывают»). Отдельно: **`YT_CONFIG_PATH` ослабляет** — им указывают на другой конфиг и обходят политики профиля целиком.

### Профиль, организация, креденшелы

| Переменная | Поведение |
|---|---|
| `YT_PROFILE` | Имя профиля. Приоритет: `--profile` → `YT_PROFILE` → `default_profile` из конфига → единственный профиль, если он один (несколько без выбора = `config_error`, exit 9) |
| `YT_ORG_TYPE` | Ровно `yandex360` или `cloud` (регистрозависимо), иначе `config_error` (exit 9). Перекрывает профиль |
| `YT_ORG_ID` | ID организации; перекрывает профиль. Нет ни в env, ни в профиле — `config_error` (exit 9) |
| `YT_SERVICE_ACCOUNT_ID`, `YT_SERVICE_ACCOUNT_KEY_ID`, `YT_SERVICE_ACCOUNT_KEY_FILE` / `YT_SERVICE_ACCOUNT_KEY_PEM` | Сервис-аккаунт целиком из env. Задана хоть одна — нужны `_ID` + `_KEY_ID` + (`_KEY_FILE` либо `_KEY_PEM`), иначе `config_error` (exit 9) |
| `YT_IAM_TOKEN` | Готовый IAM-токен |
| `YT_OAUTH_TOKEN` | OAuth-токен |
| `YT_OAUTH_CLIENT_ID` | client_id для браузерного `yt auth login --type oauth` без `--token`; `--client-id` важнее |

Приоритет авторизации: сервис-аккаунт из env → `YT_IAM_TOKEN` → `YT_OAUTH_TOKEN` → auth профиля. Креденшелы из env **заменяют** профильные целиком, но политики (`read_only`, `allowed_queues`, `allowed_write_issues`) при этом берутся из выбранного профиля.

### Вывод, терминал, сеть, пути

| Переменная | Поведение |
|---|---|
| `YT_FORMAT` | `json`/`minimal`/`table`/`auto` (регистронезависимо). Приоритет: `--format` → `YT_FORMAT` → `default_format` профиля → auto (TTY = table, pipe = json). Мусорное значение — `invalid_args` (exit 2), не игнорируется |
| `YT_TIMEOUT` | HTTP-таймаут в секундах, целое 1…86400 (default 30). `0`, отрицательное, нечисловое — `invalid_args` (exit 2). `--timeout` важнее |
| `YT_API_BASE_URL` | Base URL Tracker API. Невалидный URL не даёт JSON-ошибки: необработанное исключение и exit 1 |
| `NO_COLOR` | Любое непустое значение выключает ANSI-цвета ([no-color.org](https://no-color.org)). `TERM=dumb` и перенаправленный stdout выключают их тоже |
| ~~`YT_NO_COLOR`~~ | **Не существует.** Цвет выключают `NO_COLOR`, `--no-color`, `TERM=dumb` и перенаправленный stdout. Раньше ключ значился среди читаемых переменных, но не применялся нигде — удалён |
| `YT_HYPERLINKS` | Force для OSC 8. Выключают `0`/`false`/`no`/`off` (регистронезависимо, с обрезкой пробелов), **любое другое непустое значение включает**. При перенаправленном stdout и `TERM=dumb` игнорируется (ссылок нет) |
| `YT_TERMINAL_WIDTH` | Ширина в колонках, clamp [40, 200]. Нечисловое или ≤ 0 — молча игнорируется (ширина консоли, иначе 100) |
| `YT_PAGER` | Команда pager. Пустая строка или `cat` — выключить pager. Не задана — берётся `PAGER`, иначе `less -R -F -X`. `--no-pager` и перенаправленный stdout выключают pager в любом случае |
| `PAGER` | Системный fallback для `YT_PAGER` |
| `YT_LOG_FILE` | Путь к wire-log; работает для **любой** команды. `--log-file` важнее: если задан флаг, файл из env не создаётся вовсе |
| `YT_LOG_RAW` | Отключает маскирование секретов в wire-log (работает для любой команды). Разбор **строгий, как у `YT_READ_ONLY`**: снимают маскирование только `1`/`true`/`yes`/`on`, оставляют — `0`/`false`/`no`/`off` и незаданная переменная (регистр не важен, пробелы по краям обрезаются). **Любое другое значение — `config_error` (exit 9)**: мусор вроде `YT_LOG_RAW=disabled` не должен молча вылить в файл живые `Authorization`, `DPoP` и `refresh_token`. При включении в файл попадают живые токены — удаляй его сразу |
| `YT_CONFIG_PATH` | Путь к конфигу. **Ослабляет:** другой конфиг = другие (или никакие) политики профиля, поэтому в защищённом сценарии вызывающий не должен иметь возможности её подменить |
| `XDG_CONFIG_HOME` | База для `<...>/yandex-tracker/config.json` и DPoP-ключей (default `~/.config`); `YT_CONFIG_PATH` важнее |
| `XDG_CACHE_HOME` | База для кэша IAM-токенов `<...>/yandex-tracker/iam-tokens.json` (default `~/.cache`) |
| `HOME` / `USERPROFILE` | Домашний каталог, от которого раскрывается `~` в путях (конфиг, DPoP-ключи, кэш, `--log-file ~/...`). Порядок: `HOME` → `USERPROFILE` → системный запрос профиля пользователя |

## Часто используемые команды

### Поиск задач

Простые фильтры (рекомендую вместо YQL для типовых случаев):

```bash
yt issue find --queue TECH --status open --max 20
yt issue find --assignee korolev --max 50
yt issue find --queue TECH --tag urgent --priority high
```

YQL для сложных запросов:

```bash
yt issue find --query 'Queue: TECH AND Updated: today() AND Assignee: me()'
```

NDJSON-стрим для больших выборок:

```bash
yt issue find --queue TECH --stream | jq -r '.key'
```

### Получить задачу

```bash
yt issue get TECH-146
yt issue get TECH-146 | jq '{key,summary,status:.status.display,assignee:.assignee.display}'
```

В ответе ключевые поля: `key`, `summary`, `description` (markdown), `status.display`, `type.display`, `priority.display`, `assignee.display`, `createdBy.display`, `createdAt`, `updatedAt`, `tags`, `queue.display`, `boards`.

### Интерактивный поиск (suggest)

```bash
yt suggest                          # пустой ввод, начать набирать
yt suggest "fix"                    # с pre-filled буфером
yt suggest --queue TECH "login"     # ограничить очередью
yt suggest --limit 5                # показать максимум 5 результатов
yt issue get $(yt suggest)          # композиция: pick → get
```

Интерактивный TTY-only fuzzy-поиск через `GET /v3/issues/_suggest`. Стрелки `↑↓` навигация, `Enter` — печатает выбранный `key` в stdout, `Esc` — выход с кодом 130. Если stdout/stdin перенаправлены — exit 2 (`invalid_args`); для скриптов используйте `yt issue find`.

### Комментарии и ворклоги

```bash
yt comment list TECH-146
yt worklog list TECH-146
```

Создание (это mutating!):

```bash
yt comment add TECH-146 --text "Покрыл тестами"
yt worklog add TECH-146 --duration PT2H30M --comment "Реализация" \
  --start 2026-04-25T10:00:00+03:00
```

Длительность в формате ISO 8601: `PT2H` = 2 часа, `PT30M` = 30 минут, `PT2H30M` = 2 ч 30 мин.

### Вложения

```bash
yt attachment list TECH-146
yt attachment download TECH-146 12345 --out ./screenshot.png
```

`--out -` (ровно один дефис) стримит вложение в stdout вместо записи на диск:

```bash
yt attachment download TECH-146 12345 --out - | head -c 100 | xxd
yt attachment download TECH-146 12345 --out - > ./screenshot.png
```

- stdout содержит **только байты вложения** — JSON-сводка `{"downloaded":…,"bytes":…}` в этом режиме не печатается;
- краткая диагностика уходит в stderr строкой `downloaded <n> bytes`;
- `--out - --force` — ошибка `invalid_args` (exit 2): stdout нечего перезаписывать;
- обрыв пайпа потребителем (`| head -c 100`) — штатный исход: exit 0, в stderr `downloaded <n> bytes (truncated: consumer closed the pipe)`;
- любая другая I/O-ошибка — `network_error` (exit 8), а не тихий exit 0: оборванное тело HTTP-ответа, ошибка записи в stdout (например ENOSPC при `> file`), тело короче объявленного `Content-Length`. Усечённые данные никогда не выдаются за успех — по exit 0 без пометки `truncated` поток гарантированно полный;
- файл, который реально называется `-`, сохраняется как `--out ./-` (одиночный дефис всегда означает stdout).

При записи **в файл** действует тот же запрет на выдачу огрызка за целое, только держится он не уборкой после сбоя, а порядком операций: вложение пишется во временный файл `<target>.part-<random>` рядом с целью и переезжает под целевое имя (атомарный `rename`) **только целиком**, после проверки `Content-Length`. Поэтому:

- файл под целевым именем всегда означает полностью скачанное вложение — проверять размер после `yt attachment download` не нужно. Это верно и после `kill -9`, и после отключения питания;
- `--force` не уничтожает прежнее содержимое заранее: оборванная попытка (отмена — exit 11; ошибка I/O или короткое тело — exit 8) оставляет старый файл нетронутым, а в stderr пишет `removed incomplete download: <tmp>` — удаляется всегда собственный временный файл;
- нужно право записи **на каталог**, а не только на сам файл; иначе `network_error` (exit 8) и цель не тронута;
- симлинк разыменовывается (обновляется файл по ссылке); `/dev/null`, `/dev/tty`, `NUL` и прочие устройства пишутся напрямую, без временного файла.

Загрузка/удаление — mutating:

```bash
yt attachment upload TECH-146 ./file.png
yt attachment delete TECH-146 12345
```

### Чек-листы и связи

```bash
yt checklist get TECH-146
yt link list TECH-146
```

### Создание/изменение задач

```bash
yt issue create --queue TECH --summary "Bug in login" \
  --description "Шаги воспроизведения..." --priority high

yt issue update TECH-1 --summary "Updated" --priority normal
yt issue transition TECH-1 --list                 # доступные переходы
yt issue transition TECH-1 --to in_progress       # выполнить

yt issue changelog TECH-1                          # история изменений (JSON-массив)
yt issue changelog TECH-1 --stream | jq -r '.id'  # NDJSON, по записи на строку
yt issue changelog TECH-1 --per-page 200 --max 500 # размер страницы / лимит записей
```

`yt issue changelog KEY-N` — GET `/v3/issues/{key}/changelog`, read-only, удобно для
анализа cycle time (время по статусам). Опции: `--per-page` (default 100, размер
страницы), `--max` (default 10000, лимит записей), `--stream` (NDJSON вместо
JSON-массива). Пагинация курсорная (заголовок `Link`, `rel="next"`): выгружается полная
история независимо от `--per-page`, ограничение только по `--max`.

### Справочники и метаданные

```bash
yt ref statuses             # все статусы
yt ref priorities           # приоритеты
yt ref issue-types          # типы задач
yt queue list --max 50      # очереди
yt board list               # доски
yt field list --queue TECH  # поля очереди
```

### Автоматизации

`yt automation <kind> <op>` — работа с триггерами, автодействиями и макросами очереди (per-queue CRUD + activate/deactivate, кроме макросов).

```bash
yt automation trigger    list   --queue TECH
yt automation trigger    get    <id> --queue TECH
yt automation trigger    create --queue TECH --json-file trg.json [--name "..."] [--active|--inactive]
yt automation trigger    update <id> --queue TECH --json-file trg.json [--name "..."] [--active|--inactive]
yt automation trigger    delete <id> --queue TECH
yt automation trigger    activate   <id> --queue TECH
yt automation trigger    deactivate <id> --queue TECH

yt automation autoaction <list|get|create|update|delete|activate|deactivate>   # те же опции
yt automation macro      <list|get|create|update|delete>                       # без activate/deactivate
```

Inline-флаги (`--name`, `--active`, `--inactive`) **сливаются** поверх содержимого `--json-file`/`--json-stdin` на верхнем уровне body. Удобно для шаблонов: один JSON-файл — разные `--name` / `--active` per call.

То же merge-поведение применяется во всех командах с `--json-file`/`--json-stdin` (`issue create`, `issue update`, `comment add`, `worklog add`, `component create`, `version create`, …): typed-флаги больше не взаимоисключающиеся с raw-JSON, а перекрывают поля верхнего уровня. Для вложенных полей (например, `lead.id`) inline-флаги игнорируются — нужен raw-JSON.

## Авторизация

Если `yt user me` возвращает auth_failed (exit 4) — нужен логин:

```bash
# Yandex Cloud federated (browser flow) — самый удобный
yt auth login --type federated --federation-id <fed-id> \
  --org-type cloud --org-id <org-id> --profile work

# Yandex Cloud service-account (для CI)
yt auth login --type service-account \
  --sa-id <sa-id> --key-id <key-id> --key-file ./sa-key.pem \
  --org-type cloud --org-id <org-id> --profile ci

# Yandex 360 OAuth
yt auth login --type oauth --token y0_XXX \
  --org-type yandex360 --org-id <org-id> --profile work
```

Federated `auth login` требует TTY — нельзя из non-interactive окружения. Service-account работает везде.

Флаг `--read-only` при `auth login` записывает `read_only=true` в создаваемый профиль (работает для всех типов: `oauth`, `iam-static`, `service-account`, `federated`). Это глобальный флаг `yt`, поэтому ставится перед подкомандой или после неё — оба варианта равнозначны:

```bash
yt --read-only auth login --type service-account \
  --sa-id <sa-id> --key-id <key-id> --key-file ./sa-key.pem \
  --org-type cloud --org-id <org-id> --profile ci
```

Повторный `auth login` в существующий профиль **пересоздаёт** его: политики (`read_only`, `allowed_queues`, `allowed_write_issues`, `external_effects`) берутся только из флагов этого вызова, то есть по умолчанию снимаются, а `--read-only` ставит `read_only=true`. Переносится только `default_format`. Это и есть штатный путь сброса политики — он требует самих креденшелов (`yt config get` их маскирует). `yt auth relogin`, наоборот, обновляет только токены и все политики сохраняет.

## Политики профиля

Профиль может нести ограничения, которые действуют независимо от того, как его позвали — полезно для профилей, которыми пользуется автоматика (CI, AI-агенты).

```bash
# только чтение: любой POST/PUT/PATCH/DELETE блокируется (exit 3)
yt config set --profile ci read_only true

# доступ только к перечисленным очередям (exit 10 за их пределами)
yt config set --profile ci allowed_queues DEV,OPS,QA

# посмотреть текущее значение (список через запятую)
yt config get --profile ci allowed_queues        # → "DEV,OPS,QA"

# сузить список — можно
yt config set --profile ci allowed_queues DEV

# снять ограничение — НЕЛЬЗЯ: policy_violation, exit 10
yt config set --profile ci allowed_queues ""

# запись только в перечисленные задачи (чтение не ограничено)
yt config set --profile ci allowed_write_issues DEV-42
yt config get --profile ci allowed_write_issues  # → "DEV-42"

# то же самое на один запуск (для CI, где задача каждый раз своя).
# Список профиля переменная только СУЖАЕТ: действует пересечение, чужую задачу не добавить
YT_ALLOWED_WRITE_ISSUES=DEV-42,DEV-43 yt comment add DEV-42 --text "..."

# не инициировать рассылку и интеграции явно
yt config set --profile ci external_effects false
yt config get --profile ci external_effects       # → "false"

# то же самое на один запуск
YT_EXTERNAL_EFFECTS=0 yt comment add DEV-42 --text "..."

# проверить, что окружение настроено как задумано
yt auth status --profile ci
# {"profile":"ci",...,"read_only":true,"allowed_queues":["DEV","OPS","QA"],
#  "allowed_write_issues":["DEV-42"],"allowed_write_issues_source":"profile",
#  "external_effects":false}
```

Как работает `allowed_queues`:

- **Адресные обращения** к задаче или очереди вне списка блокируются до выхода в сеть — и на чтение, и на запись: `issue get OPS-1`, `comment add OPS-1`, `component list --queue OPS`, автоматизации `queues/OPS/...`. Ответ — явная ошибка политики, а не `not_found`:
  ```json
  {"error":{"code":"policy_violation","message":"queue 'OPS' is outside allowed_queues of profile 'ci' (allowed: DEV, QA)"}}
  ```
- **Поиск продолжает работать**: `yt issue find --yql "..."` выполняется целиком, но задачи из очередей вне списка **вырезаются из вывода**. `--max` считает выданные наружу задачи, поэтому лимит набирается корректно. Явно названная запрещённая очередь (`--queue OPS`) — ошибка `policy_violation`, а не пустая выдача.
- **`yt queue list`** отдаёт только разрешённые очереди; **`yt suggest`** — только разрешённые задачи, в том числе без `--queue`.
- **`yt link list DEV-1`** отдаёт только связи с разрешёнными очередями (сам запрос допустим, но в ответе — ключи и темы связанных задач).
- **Тело запроса проверяется** там, где очередь едет не в URL: `issue create --queue`, `issue move --to-queue`, `component create`, `version create` — включая `--json-file`/`--json-stdin`.
- **`component`/`version` по id** (`get`/`update`/`delete`): CLI сначала доспрашивает ресурс, берёт из ответа очередь-владельца и сверяет со списком. Нет ресурса → обычный `not_found` (exit 5); очередь не определяется → `policy_violation`.
- **`yt issue batch`** при действующем ограничении требует явный список `issues` в payload; payload с полем `query` отклоняется целиком — даже вместе с разрешённым `issues`, потому что задачи по запросу выбирает сервер.
- Сравнение ключей очередей — **без учёта регистра**; список хранится в профиле в том написании, которое задал пользователь.
- Env-переменной для `allowed_queues` нет намеренно: окружение так же управляемо вызывающим, как и флаг командной строки.

Как работает `allowed_write_issues` (ограничение **области записи**):

- **Ограничивается только запись.** Чтение любых задач (в пределах `allowed_queues`) продолжает работать — этим политика отличается от `read_only`. `POST .../_search` считается чтением и проходит.
- **Мутирующий запрос разрешён, только если он адресован задаче из списка**: `issues/{KEY}/...` с `KEY` из списка (`comment add DEV-42`, `issue update DEV-42`, `checklist`, `worklog`, `links`, `transitions`, `attachments`). Сравнение ключей — **без учёта регистра**; percent-encoding в URL декодируется до сравнения, обход через `%4F` не работает.
- **Запрет по умолчанию для всего остального.** При действующем списке блокируются целиком: создание задачи (`yt issue create`), `yt issue batch` (`POST bulkchange`), мутации очередей и автоматизаций (`queues/{KEY}/triggers`, `autoactions`, макросы), а также любой мутирующий путь, по которому нельзя доказать попадание в разрешённую задачу:
  ```json
  {"error":{"code":"policy_violation","message":"issue 'OPS-7' is outside allowed_write_issues of profile 'ci' (allowed: DEV-42)"}}
  ```
- **Рассылка уведомлений запрещена.** Поля тела запроса `summonees` и `maillistSummonees` призывают людей и шлют письма адресатам за пределами задачи, поэтому при действующем списке они блокируются — на любом уровне вложенности и независимо от того, пришли они из `--text`-флагов или из raw-тела (`--json-file`/`--json-stdin`). Проверяется фактическое тело запроса, а не только разобранные флаги.
- **Env только сужает, расширить не может.** `YT_ALLOWED_WRITE_ISSUES` не даёт расширения ни при каком сочетании значений — как `read_only` и `allowed_queues`:

  | Профиль | Переменная | Действует |
  |---|---|---|
  | нет списка | `DEV-42,DEV-43` | `DEV-42,DEV-43` (штатный CI-сценарий: задача каждый раз своя) |
  | `DEV-42,DEV-43` | `DEV-43,OPS-7` | `DEV-43` — **пересечение**; `OPS-7` на запись не открывается |
  | `DEV-42` | `OPS-7` (не пересекается) | `config_error`, exit 9 — команда не выполняется |
  | `DEV-42` | не задана или пробелы | `DEV-42` |

  Сравнение — без учёта регистра (`dev-42` пересекается с `DEV-42`), в выводе остаётся написание из профиля.
- **Пустое пересечение = отказ, а не «ограничений нет».** Пустой список означает отсутствие ограничения, поэтому непересекающиеся списки дают `config_error` (exit 9) с обоими списками в сообщении — иначе переменная с чужой задачей открывала бы запись всюду.
- **Пустая переменная ≠ снятие.** Значение из одних пробелов игнорируется (действует список профиля); значение из одних разделителей (`","`, `",,"`) — `config_error` (exit 9), а не молчаливое снятие.
- **Отсутствие ключа в профиле = ограничения нет**, но снять уже выставленный список через `yt config set allowed_write_issues ""` нельзя — только сузить (см. ниже).
- `yt auth status` печатает действующий список в `allowed_write_issues` и его источник в `allowed_write_issues_source`: `profile`, `env` (профиль ограничения не нёс) или `profile+env` (действует пересечение).

Как работает `external_effects` (запрет **инициировать рассылку и интеграции явно**):

- **Призыв запрещён.** Любое мутирующее обращение, в JSON-теле которого на любом уровне вложенности есть `summonees` или `maillistSummonees` (они шлют письма людям и в списки рассылки), — включая вложенный комментарий в теле перехода и payload `yt issue batch`. Проверяется фактическое тело запроса, а не разобранные флаги, поэтому `--json-file`/`--json-stdin` ничего не меняют:
  ```json
  {"error":{"code":"policy_violation","message":"request body field 'summonees' summons recipients by mail and is blocked by external_effects of profile 'ci'"}}
  ```
- **Мутации автоматизаций запрещены** — вся группа `yt automation`. `POST`/`PUT`/`PATCH`/`DELETE` на `queues/{KEY}/triggers`, `queues/{KEY}/autoactions/{id}`, `queues/{KEY}/macros/{id}`: среди действий триггера есть HTTP-запрос наружу и отправка письма, а созданные триггер, автодействие и макрос переживают сессию. **Чтение (`GET`) остаётся разрешённым.**
- Сегмент ресурса сверяется позиционно (`queues/{KEY}/{triggers|autoactions|macros}`), поэтому очередь с ключом `TRIGGERS` или `MACROS` под запрет не попадает. Сегменты пути декодируются до сравнения (`%74riggers` не обходит), сравнение — без учёта регистра; `POST .../_search` считается чтением и проходит. Тело, объявленное как JSON, но неразбираемое, и тело сверх 4 МБ отклоняются: доказать, что призыва в них нет, нельзя.
- **Больше ничего не запрещается**: сам по себе `bulkchange`, `notify`-параметры и прочее — вне объёма политики.
- **Отсутствие ключа `external_effects` = разрешено.** `YT_EXTERNAL_EFFECTS` только ужесточает: `0`/`false`/`no`/`off` запрещает поверх любого профиля, `1`/`true` запрет профиля не снимает, мусор — `config_error` (exit 9).
- **Это не гарантия, что наружу ничего не уйдёт.** Любая правка задачи может поднять триггер, уже настроенный на стороне очереди, и он отправит письмо или HTTP-запрос — CLI об этом не знает и повлиять не может. Гарантия молчания есть только у `read_only`.

### Изменение и сброс политик

`yt config set` умеет только **ужесточать** политику:

| Изменение | Результат |
|---|---|
| `read_only false → true` | ок |
| `read_only true → false` | `policy_violation`, exit 10 |
| `external_effects` не задан/`true` → `false` | ок |
| `external_effects false → true` | `policy_violation`, exit 10 |
| ограничения нет → любой список | ок |
| список → его подмножество | ок |
| список → расширение или `""` | `policy_violation`, exit 10 |

Сброс — только пересоздание профиля:

```bash
yt auth login --profile ci --type oauth --token <token> --org-type cloud --org-id <org-id>
# политики берутся только из флагов этого вызова: read_only=false, списки и
# запрет внешних эффектов сняты
```

Барьер держится на том, что `yt config get` маскирует `auth.token` и `auth.private_key_pem`: без креденшелов повторный login не сделать. `yt auth logout` чистит токен, но политики оставляет; `yt auth relogin` (и авто-relogin при неудачном DPoP-refresh) политики сохраняет.

### Что политики НЕ закрывают (известные пределы)

Условий, при которых политики вообще что-то значат, **два** — и оба про вызывающего:

- **Файл конфига.** Граница держится, пока у вызывающего нет прямой записи в `~/.config/yandex-tracker/config.json` (или `$XDG_CONFIG_HOME/yandex-tracker/config.json`, либо путь из `YT_CONFIG_PATH`). Кто может править этот файл — снимет любую политику. Файл создаётся с правами `0600`; каталог держите `0700` и запускайте ограниченный процесс под пользователем без прав на запись туда и без возможности подменить `YT_CONFIG_PATH`.
- **Сырые креденшелы.** Пока сервис-аккаунтные креды доступны процессу вызывающего (типовой случай: `TRACKER_SA_SERVICE_ACCOUNT_ID`, `TRACKER_SA_KEY_ID`, `TRACKER_SA_PRIVATE_KEY`, `TRACKER_CLOUD_ORG_ID` лежат в переменных CI-джобы и наследуются процессом агента), политики не являются границей вообще: ничего не обходя, вызывающий делает `YT_CONFIG_PATH=/tmp/mine.json yt auth login --type service-account …` из того, что у него и так есть, и получает профиль **без единой политики**. Политики профиля — второй слой защиты, а не первый; они предполагают, что у вызывающего нет ни записи в конфиг, ни сырых кредов. Если креды всё-таки в окружении — границу держит вызывающая сторона: не отдавать креды ограниченному процессу (только готовый конфиг), контролировать `YT_CONFIG_PATH`, выдавать отдельный сервис-аккаунт с узкими серверными правами.

Дальше — пределы уже внутри самой модели политик:

- **Boards, sprints, projects, глобальные справочники полей и пользователи** по `allowed_queues` не фильтруются. **Локальные** поля очереди — исключение: `field list --queue OPS` бьёт в `queues/OPS/localFields` и отклоняется как адресное обращение (exit 10).
- **Ссылки на чужие задачи внутри разрешённой** (`issue get` — поля `parent`/`links`, `issue changelog`): ответ API печатается как есть, поэтому видны и ключи, и **темы** (`display`) задач из очередей вне списка. Сами эти задачи остаются нечитаемыми: `issue get OPS-1` даёт `policy_violation` (exit 10), запрос в сеть не уходит. Отдельная выдача связей (`link list`) отфильтрована — там чужие связи вырезаются целиком.
- **`external_effects` не закрывает уже настроенные триггеры**: правка задачи может поднять триггер очереди, который сам отправит письмо или HTTP-запрос. Политика запрещает только то, что инициирует сам вызывающий; «наружу ничего не уйдёт» гарантирует лишь `read_only`.
- **Серверные права политики не заменяют**: это ограничение клиента поверх токена.

### Повторный вход (federated DPoP)

Federated-токены DPoP-привязаны и протухают. Когда обновление токена не удаётся:

- **В TTY**: `yt <команда>` сама откроет браузер, выполнит повторный вход и продолжит — ничего делать не нужно.
- **В non-interactive (агенты, CI, пайпы)**: команда НЕ открывает браузер сама. Возвращает `auth_failed` (exit 4) с полем `relogin_command` — точную команду, которую надо выполнить, чтобы открылся браузер:

```json
{"error":{"code":"auth_failed","message":"...","relogin_command":"yt auth relogin --profile work"}}
```

`yt auth relogin` переиспользует сохранённые `federation_id` и DPoP-ключ профиля — НЕ нужно заново указывать `--federation-id`/`--org-*`:

```bash
yt auth relogin --profile work          # откроет браузер, переавторизует, сохранит токены
yt auth relogin                          # для default-профиля
yt auth relogin --profile work --timeout-auth 180
```

`auth relogin` stdin не читает — её можно вызывать и из non-TTY (браузер всё равно откроется на машине пользователя). Если агент получил `relogin_command` — выполни эту команду (или подскажи пользователю), затем повтори исходную команду.

## Парсинг JSON ответов

Объект (для одиночных команд):

```bash
yt issue get TECH-146 | jq -r '.summary'
yt user me | jq '.email'
```

Массив (для list-команд):

```bash
yt queue list --max 100 | jq -r '.[].key'
yt issue find --queue TECH --max 50 | jq -r '.[] | "\(.key) \(.summary)"'
```

## Обработка ошибок

Все ошибки — JSON на stderr:

```json
{"error":{"code":"not_found","message":"...","http_status":404,"trace_id":"..."}}
```

В bash скриптах:

```bash
if ! result=$(yt issue get TECH-9999 2>&1); then
  exit_code=$?
  if [ "$exit_code" = "5" ]; then
    echo "Issue not found"
  fi
fi
```

## Что НЕ делать

- **Не парси `--format table`** — это для людей, неустойчиво, нет гарантии формата.
- **Не выдумывай команды/флаги** — проверяй через `yt --help` или `yt <group> --help` если не уверен.
- **Не делай мутирующие операции без подтверждения пользователя** — `issue create`, `issue update`, `issue delete`, `comment add`, `comment delete`, `worklog add`, `attachment upload/delete`, `link add/remove`, `checklist *`, `transition --to`, `move`, `batch`, `project/component/version create/update/delete`. Если в режиме «просто посмотреть» — добавляй `--read-only`.
- **Не сохраняй токены в логи/файлы**. Если нужен wire-log для отладки — он по умолчанию маскирует секреты; `--log-raw` отключает маскирование, но требует немедленного удаления файла.

## Краткая справка по exit-кодам

| Code | Значение | Действие |
|---|---|---|
| 0 | OK | продолжать |
| 2 | invalid_args | проверить синтаксис команды |
| 3 | read_only_mode | команда мутирующая, снять `--read-only` если намеренно |
| 4 | auth_failed/forbidden | перелогиниться или проверить права |
| 5 | not_found | проверить ключ задачи / ID |
| 6 | rate_limited | подождать (Retry handler уже делает retry с backoff) |
| 7 | server_error | retry или сообщить о сбое Tracker |
| 8 | network_error | проверить сеть, прокси, timeout |
| 9 | config_error | проверить профиль (`yt config list`) или `auth login` |
| 10 | policy_violation | очередь вне `allowed_queues` профиля, запись вне `allowed_write_issues`, `summonees`/`maillistSummonees` при действующем ограничении области записи или при `external_effects: false`, мутация `triggers`/`autoactions`/`macros` при `external_effects: false`, либо попытка ослабить политику через `yt config set` — сменить профиль (`--profile`) или, если есть креденшелы, пересоздать его через `yt auth login` |
| 11 | cancelled | команда прервана: Ctrl-C/SIGINT, SIGTERM или истёкший HTTP-таймаут. Вывод неполон — повторить команду; при таймауте поднять его через `--timeout <секунды>` или `YT_TIMEOUT` |
| 130 | — | процесс убит SIGINT самой ОС (сигнал пришёл до первой async-операции либо это второй Ctrl-C); JSON-ошибки нет. Тот же код у `yt suggest` при выходе по `Esc` — штатный отказ от выбора |
| 143 | — | процесс убит SIGTERM самой ОС; JSON-ошибки нет |

Отмена ловится на верхнем уровне CLI и одинакова для всех команд: на stderr уходит обычная JSON-ошибка `{"error":{"code":"cancelled","message":…}}`, стектрейс .NET наружу не попадает. Сообщение различает случаи: `timed out after <n>s …` — сработал таймаут (и назван способ его поднять); `cancelled by SIGINT|SIGTERM …` — прервал пользователь или супервизор. Если признаков не хватило, текст честно говорит, что причину определить не удалось, и всё равно подсказывает про `--timeout`/`YT_TIMEOUT`.

Команда, не отвечающая на отмену дольше 2 секунд, всё равно даёт exit 11 — с пометкой, что была завершена принудительно. Исключение одно: сигнал, пришедший до первой асинхронной операции команды (например, когда она блокирующе читает stdin), CLI не перехватывает — процесс завершает ОС с кодом 130 (SIGINT) или 143 (SIGTERM) и без JSON-ошибки. Второй Ctrl-C тоже уходит к ОС и убивает процесс немедленно, не дожидаясь grace-периода.

**В обёртках проверяй отмену как «11, 130 или 143», а не только 11.** Единый код здесь недостижим: чтобы всегда отдавать 11, пришлось бы подавлять сигнал и в момент, когда команда ещё ничего не делает асинхронно, — тогда Ctrl-C переставал бы работать.

## Шпаргалка

| Задача | Команда |
|---|---|
| Кто я | `yt user me` |
| Список очередей | `yt queue list --max 50` |
| Мои задачи | `yt issue find --assignee me() --max 50` (через YQL) или `--assignee <login>` |
| Открытые баги | `yt issue find --queue X --status open --type bug` |
| Детали задачи | `yt issue get KEY-N` |
| Комменты задачи | `yt comment list KEY-N` |
| Вложения | `yt attachment list KEY-N` |
| Скачать вложение | `yt attachment download KEY-N <id> --out ./file` |
| Вложение в pipe | `yt attachment download KEY-N <id> --out -` (stdout = только байты) |
| Чек-лист | `yt checklist get KEY-N` |
| Связанные задачи | `yt link list KEY-N` |
| Доступные переходы | `yt issue transition KEY-N --list` |
| История изменений | `yt issue changelog KEY-N` (GET, для cycle time) |
| Повторный вход (federated) | `yt auth relogin --profile <name>` |
| Профиль только на чтение | `yt config set --profile ci read_only true` |
| Ограничить очереди профиля | `yt config set --profile ci allowed_queues DEV,QA` |
| Разрешить запись только в задачу | `yt config set --profile ci allowed_write_issues DEV-42` |
| Запретить призыв и правку автоматизаций | `yt config set --profile ci external_effects false` |
| Сбросить политики профиля | `yt auth login --profile ci --type oauth --token <token> --org-type cloud --org-id <id>` |
| Сузить область записи на один запуск | `YT_ALLOWED_WRITE_ISSUES=DEV-42 yt comment add DEV-42 --text "..."` (со списком профиля — пересечение) |
| Запретить внешние эффекты на один запуск | `YT_EXTERNAL_EFFECTS=0 yt comment add DEV-42 --text "..."` |
| Проверить политики профиля | `yt auth status --profile ci` |
