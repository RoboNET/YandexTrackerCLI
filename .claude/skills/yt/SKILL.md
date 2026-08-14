---
name: yt
description: Use when interacting with Yandex Tracker (Яндекс Трекер) — searching/reading issues, comments, worklogs, attachments, checklists, links, boards, sprints, projects. Triggers on words like "Яндекс Трекер", "Tracker", "ишью", "задача в трекере", issue keys like "TECH-1234", or URLs like tracker.yandex.ru/MAN-123. Use yt CLI for both reading and mutating operations; pass --read-only or YT_READ_ONLY=1 for safe browsing.
---

<!-- yt-version: 0.6.0 -->

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

Без пакетных менеджеров — готовый бинарь из [GitHub Releases](https://github.com/RoboNET/YandexTrackerCLI/releases/latest) (архивы: `yt-linux-x64.tar.gz`, `yt-linux-arm64.tar.gz`, `yt-osx-arm64.tar.gz`, `yt-win-x64.zip`):

```bash
RID=linux-x64   # подставь платформу
curl -fsSL -o yt.tar.gz "https://github.com/RoboNET/YandexTrackerCLI/releases/latest/download/yt-${RID}.tar.gz"
tar -xzf yt.tar.gz && sudo mv yt /usr/local/bin/yt
# macOS: если бинарь в карантине — xattr -d com.apple.quarantine /usr/local/bin/yt
```

Из исходников: `dotnet publish` (см. README репозитория).

## Базовые правила

- **JSON-вывод по умолчанию для скриптов** — auto-detect: при pipe всегда compact JSON. Не пытайся парсить таблицы — всегда работай с `yt ... | jq` или `python -c`.
- **Exit-коды** — стабильные (см. ниже): 0 = успех, 2 = плохие аргументы, 3 = read-only заблокирован, 4 = auth_failed/forbidden, 5 = not_found, 6 = rate_limited, 7 = server_error, 8 = network_error, 9 = config_error, 10 = policy_violation (очередь вне `allowed_queues` профиля либо запись вне `allowed_write_issues`).
- **Профиль** — выбирается через `--profile <name>` или `YT_PROFILE`. Если в конфиге ровно один профиль — он используется автоматически. Если несколько — нужно либо указать, либо предварительно `yt config profile <name>`.
- **Перед мутирующими действиями** — спроси пользователя подтверждение (создание issue, удаление, изменение статуса, добавление комментария от его имени).
- **Страховка `--read-only`** — необязательный пояс безопасности для AI-агентов или массовых операций. Блокирует POST/PUT/PATCH/DELETE до выхода в сеть, возвращает exit 3. Пропускает POST на `_search`-эндпоинты (поиск через `issue find`). Для обычных read-команд (`issue get`, `comment list`, `attachment list`, …) флаг не нужен — они и так GET-запросы.
- **Политики профиля (`read_only`, `allowed_queues`, `allowed_write_issues`)** — то же ограничение, но как свойство самих креденшелов, а не флаг вызова: их нельзя «забыть» в командной строке. `allowed_write_issues` ограничивает **только запись** (чтение остаётся), поэтому годится для автоматики, которой надо ответить ровно в одну задачу. См. раздел «Политики профиля» ниже.

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

Повторный `auth login` в существующий профиль **пересоздаёт** его: политики (`read_only`, `allowed_queues`, `allowed_write_issues`) берутся только из флагов этого вызова, то есть по умолчанию снимаются, а `--read-only` ставит `read_only=true`. Переносится только `default_format`. Это и есть штатный путь сброса политики — он требует самих креденшелов (`yt config get` их маскирует). `yt auth relogin`, наоборот, обновляет только токены и все политики сохраняет.

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

# проверить, что окружение настроено как задумано
yt auth status --profile ci
# {"profile":"ci",...,"read_only":true,"allowed_queues":["DEV","OPS","QA"],
#  "allowed_write_issues":["DEV-42"],"allowed_write_issues_source":"profile"}
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

### Изменение и сброс политик

`yt config set` умеет только **ужесточать** политику:

| Изменение | Результат |
|---|---|
| `read_only false → true` | ок |
| `read_only true → false` | `policy_violation`, exit 10 |
| ограничения нет → любой список | ок |
| список → его подмножество | ок |
| список → расширение или `""` | `policy_violation`, exit 10 |

Сброс — только пересоздание профиля:

```bash
yt auth login --profile ci --type oauth --token <token> --org-type cloud --org-id <org-id>
# политики берутся только из флагов этого вызова: read_only=false, списки сняты
```

Барьер держится на том, что `yt config get` маскирует `auth.token` и `auth.private_key_pem`: без креденшелов повторный login не сделать. `yt auth logout` чистит токен, но политики оставляет; `yt auth relogin` (и авто-relogin при неудачном DPoP-refresh) политики сохраняет.

### Что политики НЕ закрывают (известные пределы)

Условий, при которых политики вообще что-то значат, **два** — и оба про вызывающего:

- **Файл конфига.** Граница держится, пока у вызывающего нет прямой записи в `~/.config/yandex-tracker/config.json` (или `$XDG_CONFIG_HOME/yandex-tracker/config.json`, либо путь из `YT_CONFIG_PATH`). Кто может править этот файл — снимет любую политику. Файл создаётся с правами `0600`; каталог держите `0700` и запускайте ограниченный процесс под пользователем без прав на запись туда и без возможности подменить `YT_CONFIG_PATH`.
- **Сырые креденшелы.** Пока сервис-аккаунтные креды доступны процессу вызывающего (типовой случай: `TRACKER_SA_SERVICE_ACCOUNT_ID`, `TRACKER_SA_KEY_ID`, `TRACKER_SA_PRIVATE_KEY`, `TRACKER_CLOUD_ORG_ID` лежат в переменных CI-джобы и наследуются процессом агента), политики не являются границей вообще: ничего не обходя, вызывающий делает `YT_CONFIG_PATH=/tmp/mine.json yt auth login --type service-account …` из того, что у него и так есть, и получает профиль **без единой политики**. Политики профиля — второй слой защиты, а не первый; они предполагают, что у вызывающего нет ни записи в конфиг, ни сырых кредов. Если креды всё-таки в окружении — границу держит вызывающая сторона: не отдавать креды ограниченному процессу (только готовый конфиг), контролировать `YT_CONFIG_PATH`, выдавать отдельный сервис-аккаунт с узкими серверными правами.

Дальше — пределы уже внутри самой модели политик:

- **Boards, sprints, projects, глобальные справочники полей и пользователи** по `allowed_queues` не фильтруются. **Локальные** поля очереди — исключение: `field list --queue OPS` бьёт в `queues/OPS/localFields` и отклоняется как адресное обращение (exit 10).
- **Ссылки на чужие задачи внутри разрешённой** (`issue get` — поля `parent`/`links`, `issue changelog`): ответ API печатается как есть, поэтому видны и ключи, и **темы** (`display`) задач из очередей вне списка. Сами эти задачи остаются нечитаемыми: `issue get OPS-1` даёт `policy_violation` (exit 10), запрос в сеть не уходит. Отдельная выдача связей (`link list`) отфильтрована — там чужие связи вырезаются целиком.
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
| 10 | policy_violation | очередь вне `allowed_queues` профиля, запись вне `allowed_write_issues`, `summonees`/`maillistSummonees` при действующем ограничении области записи, либо попытка ослабить политику через `yt config set` — сменить профиль (`--profile`) или, если есть креденшелы, пересоздать его через `yt auth login` |

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
| Сбросить политики профиля | `yt auth login --profile ci --type oauth --token <token> --org-type cloud --org-id <id>` |
| Сузить область записи на один запуск | `YT_ALLOWED_WRITE_ISSUES=DEV-42 yt comment add DEV-42 --text "..."` (со списком профиля — пересечение) |
| Проверить политики профиля | `yt auth status --profile ci` |
