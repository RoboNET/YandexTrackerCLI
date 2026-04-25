---
name: yt
description: Use when interacting with Yandex Tracker (Яндекс Трекер) — searching/reading issues, comments, worklogs, attachments, checklists, links, boards, sprints, projects. Triggers on words like "Яндекс Трекер", "Tracker", "ишью", "задача в трекере", issue keys like "TECH-1234", or URLs like tracker.yandex.ru/MAN-123. Use yt CLI for both reading and mutating operations; pass --read-only or YT_READ_ONLY=1 for safe browsing.
---

# yt — Yandex Tracker CLI

`yt` — это NativeAOT CLI для Яндекс Трекера. Один бинарь, JSON-вывод по умолчанию (auto-detect: TTY → table, pipe → json), стабильные exit-коды. Используй его для любых задач связанных с Tracker'ом.

## Установка и проверка

Проверь что установлен:

```bash
yt --version
```

Если нет — установи из [GitHub Releases](https://github.com/RoboNET/YandexTrackerCLI/releases/latest), либо из исходников `dotnet publish`. Подробнее в README репозитория.

## Базовые правила

- **JSON-вывод по умолчанию для скриптов** — auto-detect: при pipe всегда compact JSON. Не пытайся парсить таблицы — всегда работай с `yt ... | jq` или `python -c`.
- **Read-only режим** — для безопасного чтения добавляй `--read-only` или `YT_READ_ONLY=1`. Это блокирует все mutating-операции (POST/PUT/PATCH/DELETE) до выхода в сеть. Возвращает exit 3 при попытке.
- **Exit-коды** — стабильные (см. ниже): 0 = успех, 2 = плохие аргументы, 3 = read-only заблокирован, 4 = auth_failed/forbidden, 5 = not_found, 6 = rate_limited, 7 = server_error, 8 = network_error, 9 = config_error.
- **Профиль** — выбирается через `--profile <name>` или `YT_PROFILE`. Если в конфиге ровно один профиль — он используется автоматически. Если несколько — нужно либо указать, либо предварительно `yt config profile <name>`.
- **Перед мутирующими действиями** — спроси пользователя подтверждение (создание issue, удаление, изменение статуса, добавление комментария от его имени).

## Часто используемые команды

### Поиск задач

Простые фильтры (рекомендую вместо YQL для типовых случаев):

```bash
yt --read-only issue find --queue TECH --status open --max 20
yt --read-only issue find --assignee korolev --max 50
yt --read-only issue find --queue TECH --tag urgent --priority high
```

YQL для сложных запросов:

```bash
yt --read-only issue find --query 'Queue: TECH AND Updated: today() AND Assignee: me()'
```

NDJSON-стрим для больших выборок:

```bash
yt --read-only issue find --queue TECH --stream | jq -r '.key'
```

### Получить задачу

```bash
yt --read-only issue get TECH-146
yt --read-only issue get TECH-146 | jq '{key,summary,status:.status.display,assignee:.assignee.display}'
```

В ответе ключевые поля: `key`, `summary`, `description` (markdown), `status.display`, `type.display`, `priority.display`, `assignee.display`, `createdBy.display`, `createdAt`, `updatedAt`, `tags`, `queue.display`, `boards`.

### Комментарии и ворклоги

```bash
yt --read-only comment list TECH-146
yt --read-only worklog list TECH-146
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
yt --read-only attachment list TECH-146
yt --read-only attachment download TECH-146 12345 --out ./screenshot.png
```

Загрузка/удаление — mutating:

```bash
yt attachment upload TECH-146 ./file.png
yt attachment delete TECH-146 12345
```

### Чек-листы и связи

```bash
yt --read-only checklist get TECH-146
yt --read-only link list TECH-146
```

### Создание/изменение задач

```bash
yt issue create --queue TECH --summary "Bug in login" \
  --description "Шаги воспроизведения..." --priority high

yt issue update TECH-1 --summary "Updated" --priority normal
yt issue transition TECH-1 --list                 # доступные переходы
yt issue transition TECH-1 --to in_progress       # выполнить
```

### Справочники и метаданные

```bash
yt --read-only ref statuses             # все статусы
yt --read-only ref priorities           # приоритеты
yt --read-only ref issue-types          # типы задач
yt --read-only queue list --max 50      # очереди
yt --read-only board list               # доски
yt --read-only field list --queue TECH  # поля очереди
```

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

Federated требует TTY — нельзя из non-interactive окружения. Service-account работает везде.

## Парсинг JSON ответов

Объект (для одиночных команд):

```bash
yt --read-only issue get TECH-146 | jq -r '.summary'
yt --read-only user me | jq '.email'
```

Массив (для list-команд):

```bash
yt --read-only queue list --max 100 | jq -r '.[].key'
yt --read-only issue find --queue TECH --max 50 | jq -r '.[] | "\(.key) \(.summary)"'
```

## Обработка ошибок

Все ошибки — JSON на stderr:

```json
{"error":{"code":"not_found","message":"...","http_status":404,"trace_id":"..."}}
```

В bash скриптах:

```bash
if ! result=$(yt --read-only issue get TECH-9999 2>&1); then
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
| Чек-лист | `yt checklist get KEY-N` |
| Связанные задачи | `yt link list KEY-N` |
| Доступные переходы | `yt issue transition KEY-N --list` |
