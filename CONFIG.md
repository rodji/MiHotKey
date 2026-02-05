# Конфигурация `config.json` (v2)

Приложение читает `config.json` из папки запуска (это `AppContext.BaseDirectory`). JSON парсится **без учета регистра ключей**, с поддержкой `//`/`/* */` комментариев и trailing commas.

## Что изменилось в v2

- `version: 2` (v1 больше не поддерживается).
- `routesByTrigger[*]` теперь использует `actionType`/`actionId` вместо `shortcut`.
- Добавлены `programs` и запуск программ из UI/роутинга.
- Добавлен `shortcuts[*].send = "messages"` (через `PostMessage`).

## Как выбирается файл конфига

Алгоритм загрузки:

1. Всегда сначала читается `config.json` рядом с `.exe` (чтобы узнать `app.configPath`).
2. Если `app.configPath` указывает на другой путь, загружается уже тот файл и применяется как итоговый конфиг.

Это позволяет хранить “основной” конфиг, например, в `%AppData%`, оставив рядом с `.exe` маленький bootstrap-конфиг.

## Структура верхнего уровня

- `version` — обязательно `2`.
- `app` — поведение приложения (фокус, выбор окна, тайминги отправки).
- `tray` — видимость пунктов меню в трее.
- `logging` — уровни логирования.
- `inputs` — источники событий (hotkeys, WMI).
- `bindings` — привязка `triggerId -> [events...]` для WMI-событий.
- `targets` — правила поиска целевого окна.
- `shortcuts` — словарь сочетаний клавиш для отправки.
- `programs` — словарь программ, которые можно запускать.
- `routesByTrigger` — что делать для каждой пары (trigger, rule).

## `app`

```jsonc
{
  "app": {
    "configPath": ".\\config.json",
    "altConfigPathHint": "%AppData%\\MiHotKey\\config.json",
    "logBufferSize": 100,
    "foregroundTrackingEnabled": true,
    "foregroundHistorySize": 10,
    "targetSelectionMode": "ForegroundThenPrevious",
    "focusPolicy": "ActivateTargetTemporarily",
    "sendTimingMs": { "modDownToKeyDown": 5, "keyDownToKeyUp": 2, "keyUpToModUp": 2 }
  }
}
```

- `configPath` — путь к “основному” конфигу (абсолютный или относительный от папки запуска).
- `altConfigPathHint` — подсказка в логах (на поведение не влияет).
- `logBufferSize` — размер кольцевого буфера логов (10..10000).
- `foregroundTrackingEnabled` — включить трекинг активного/предыдущего окна.
- `foregroundHistorySize` — сколько окон хранить в истории (0..1000).
- `targetSelectionMode`:
  - `ForegroundThenPrevious` — сначала текущее foreground окно, затем предыдущее.
  - `ForegroundOnly` — только foreground.
  - `AlwaysPrevious` — только предыдущее.
- `focusPolicy`:
  - `ActivateTargetTemporarily` — временно активировать целевое окно для отправки (потом вернуть фокус).
  - `NoFocusChange` — не менять фокус (только если целевое окно уже foreground).
- `sendTimingMs` — паузы (мс) между нажатиями/отжатиями модификаторов и основной клавиши.

## `tray`

Управляет видимостью пунктов меню.

```jsonc
{ "tray": { "reloadConfig": true, "showLog": true, "toggleForegroundTracking": true, "exit": true } }
```

## `logging`

```jsonc
{
  "logging": {
    "level": "Information",
    "overrides": { "Target": "Debug", "Match": "Debug", "Send": "Debug", "Exec": "Information" },
    "includeScopes": false,
    "maxMessageLength": 300,
    "showConfigPathsInLog": true
  }
}
```

- `level` — базовый уровень.
- `overrides` — переопределения по категориям.
- `maxMessageLength` — обрезка сообщений в UI-логе.
- `showConfigPathsInLog` — логировать пути конфига при reload.

## `inputs.hotkeys`

Глобальные хоткеи (через `RegisterHotKey`). Регистрируются с `MOD_NOREPEAT` (длинное удержание не спамит повтором).

```jsonc
{ "inputs": { "hotkeys": [ { "id": "mute", "keys": "Ctrl+Alt+M" } ] } }
```

Формат `keys`:

- Модификаторы: `Ctrl`, `Alt`, `Shift` (в любом порядке).
- Основная клавиша: буква/цифра (`M`, `1`) или имя из `System.Windows.Forms.Keys` (например `F12`, `Escape`).

## `inputs.wmi` + `bindings`

WMI-источник генерирует “события” (строки), которые дальше мапятся в `triggerId` через `bindings`.

Пример:

```jsonc
{
  "inputs": {
    "wmi": [
      {
        "id": "tma",
        "namespace": "root\\wmi",
        "query": "SELECT * FROM TMA_WMIEvent",
        "where": { "InstanceName": "ACPI\\PNP0C14\\0x2_0" },
        "extract": { "prop": "EventDetail", "index": 2 },
        "map": { "1": "triangle.down", "6": "triangle.up" },
        "repeatHandling": "firstDownOnlyUntilUp",
        "debounceMs": 40
      }
    ]
  },
  "bindings": {
    "mute": [ "triangle.down" ]
  }
}
```

- `map` — строковый код → имя события.
- `bindings` — `triggerId` → список событий, которые должны вызвать этот trigger.

## `targets.rules`

Правила поиска целевого окна. При срабатывании триггера приложение перебирает кандидатов (см. `targetSelectionMode`) и выбирает первое окно, которое матчится под правило.

```jsonc
{
  "targets": {
    "rules": [
      { "id": "teams", "prio": 95, "proc": [ "teams", "msteams" ], "titleHas": [ "Teams" ] }
    ]
  }
}
```

- `id` — идентификатор правила (используется в `routesByTrigger`).
- `prio` — приоритет (больше = важнее).
- `proc` — имена процессов без `.exe` (например `chrome`).
- `titleHas` — подстроки, которые должны встречаться в заголовке окна.
  - если `titleHas` пустой, совпадение только по `proc`.

## `shortcuts`

Словарь “сочетание клавиш” → как отправлять.

```jsonc
{
  "shortcuts": {
    "teams.mute": { "keys": "Ctrl+Shift+M", "send": "messages" }
  }
}
```

`send`:

- `scan` — `SendInput` по scan-code (требует, чтобы клавиша была в маппинге scan-кодов; сейчас маппинг есть для A..Z).
- `vk` — `SendInput` по virtual key.
- `messages` — отправка `WM_KEYDOWN/WM_KEYUP` через `PostMessage` в целевое окно **без смены фокуса** (работает не во всех приложениях, но часто помогает при конфликте модификаторов с глобальным хоткеем).

## `programs`

Словарь программ, которые можно запускать (из tray-меню `Run` или через роутинг).

```jsonc
{
  "programs": {
    "notepad": {
      "title": "Notepad",
      "file": "notepad.exe",
      "args": "",
      "workdir": "",
      "useShellExecute": true,
      "hidden": false,
      "captureOutput": false,
      "env": {}
    }
  }
}
```

Поля:

- `title` — имя в UI (если пусто — показывается `id`).
- `file` — путь/команда. Поддерживаются переменные окружения (`%ComSpec%`, `%AppData%`).
  - если путь относительный (содержит `.` или `\\`/`/`), он резолвится относительно папки запуска;
  - иначе передается как есть (например `notepad.exe` ищется через PATH).
- `args` — аргументы командной строки (с `%VAR%`).
- `workdir` — рабочая папка (аналогично `file`).
- `useShellExecute`:
  - `true` — запуск через shell (как в Explorer).
  - `false` — прямой запуск процесса (доступны `env`, захват stdout/stderr).
- `hidden` — попытаться запустить скрыто (актуально для консольных при `useShellExecute=false`).
- `captureOutput` — логировать stdout/stderr после завершения (только при `useShellExecute=false`).
- `env` — переменные окружения (только при `useShellExecute=false`).

В лог пишется: старт, завершение, `exit code`, stdout/stderr (обрезаются до разумного размера).

## `routesByTrigger`

Главная таблица маршрутизации: для каждого `triggerId` задается список правил, и для каждого правила — действие.

```jsonc
{
  "routesByTrigger": {
    "mute": [
      { "rule": "teams", "actionType": "Shortcut", "actionId": "teams.mute" },
      { "rule": "meet",  "actionType": "Shortcut", "actionId": "meet.mute" }
    ],
    "openNotepad": [
      { "rule": "teams", "actionType": "Program", "actionId": "notepad" }
    ]
  }
}
```

- `rule` — `targets.rules[].id`.
- `actionType`:
  - `Shortcut` — отправить `shortcuts[actionId]`.
  - `Program` — запустить `programs[actionId]`.
- `actionId` — id действия (ключ в `shortcuts` или `programs`).

Важно:

- `triggerId` должен существовать в `inputs.hotkeys[].id` **или** в `bindings` (как ключ).
  - например, для `openNotepad` выше нужно добавить `inputs.hotkeys` с `id: "openNotepad"` или сделать привязку через `bindings`.
- Для первого найденного подходящего окна выполняется действие и обработка завершается.
