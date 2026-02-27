# Конфигурация `config.json` (v2)

Приложение читает `config.json` из папки запуска (это `AppContext.BaseDirectory`). JSON парсится **без учета регистра ключей**, с поддержкой `//`/`/* */` комментариев и trailing commas.

## Что изменилось в v2

- `version: 2` (v1 больше не поддерживается).
- `routesByTrigger[*]` теперь использует `actionType`/`actionId` вместо `shortcut`.
- Добавлены `programs` и запуск программ из UI/роутинга.
- Добавлен `shortcuts[*].send = "messages"` (через `PostMessage`).
- Добавлен `app.autostart.enabled` (запуск при входе пользователя в Windows через HKCU Run).
- В `targets.rules` добавлены опции матчинга по классу окна (`classIs`) и заголовку окна (`title`, glob-паттерны).
- В `inputs.wmi` добавлены политики “разрешать ли триггеры при lock/remote” (`sessionPolicy`, `sessionPolicyByEvent`).
- В `routesByTrigger` поддержано “без правила” (пустой/отсутствующий `rule`) — безусловное действие.
- Добавлены `audioDevices` и `routesByTrigger.actionType = "Audio"` для управления mute микрофона/динамиков.
- Добавлены диагностика и пункт tray-меню **Run diagnostics** (логирует окна/аудио/foreground-tracker).
- `targetSelectionMode` заменен на числовой `app.targetSearchDepth`.
- Роутинг теперь работает в два прохода, сначала для глобальных shortcut.

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
- `audioDevices` — действия с аудиоустройствами (mute/unmute/toggle).
- `routesByTrigger` — что делать для каждой пары (trigger, rule).

## `app`

```jsonc
{
  "app": {
    "configPath": ".\\config.json",
    "altConfigPathHint": "%AppData%\\MiHotKey\\config.json",
    "logBufferSize": 100,
    "targetSearchDepth": 8,
    "focusPolicy": "ActivateTargetTemporarily",
    "sendTimingMs": { "modDownToKeyDown": 5, "keyDownToKeyUp": 2, "keyUpToModUp": 2 },
    "autostart": { "enabled": false },
    "diagnostics": { "sortByTabOrder": false }
  }
}
```

- `configPath` — путь к “основному” конфигу (абсолютный или относительный от папки запуска).
- `altConfigPathHint` — подсказка в логах (на поведение не влияет).
- `logBufferSize` — размер кольцевого буфера логов (10..10000).
- `targetSearchDepth` — глубина поиска целевого окна по “недавним” окнам (1..1000): сначала текущее foreground, затем предыдущие окна из истории.
  - `1` — только текущее foreground окно (трекинг истории не используется).
  - `>1` — трекинг истории включается автоматически; чем больше число, тем глубже поиск по предыдущим окнам.
  - Переключатель tray-меню **Foreground tracking** может временно отключить трекинг истории (имеет смысл только при глубине больше `1`).
- `focusPolicy`:
  - `ActivateTargetTemporarily` — временно активировать целевое окно для отправки (потом вернуть фокус).
  - `NoFocusChange` — не менять фокус (только если целевое окно уже foreground).
- `sendTimingMs` — паузы (мс) между нажатиями/отжатиями модификаторов и основной клавиши.
- `autostart.enabled` — включить автозапуск при входе пользователя в Windows.
  - Реализация: запись в `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` значения `MiHotKey` со строкой запуска текущего `.exe`.
  - Можно переключать из tray-меню **Autostart** — приложение обновит `config.json` и перезагрузит конфиг.
- `diagnostics.sortByTabOrder` — порядок вывода top-level окон в диагностике:
  - `true` — порядок Z (как вкладки Alt-Tab),
  - `false` — сортировка по `proc`/`title`.

## `tray`

Управляет видимостью пунктов меню.

```jsonc
{ "tray": { "reloadConfig": true, "showLog": true, "toggleForegroundTracking": true, "runDiagnostics": true, "exit": true } }
```

- `runDiagnostics` — показать пункт **Run diagnostics** (логирует top-level окна, аудиоустройства и состояние foreground-tracker).

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

- Модификаторы: `Ctrl`, `Alt`, `Shift`, `Win` (в любом порядке).
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
        "sessionPolicy": "Any",
        "sessionPolicyByEvent": {
          "triangle.down": "RequireUnlockedLocalSession"
        },
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
- `sessionPolicy` — политика “когда разрешено принимать события” для этого источника.
- `sessionPolicyByEvent` — переопределение политики для конкретного `mappedEvent` (ключи — это **значения** из `map`, например `triangle.down`).

`sessionPolicy` / `sessionPolicyByEvent`:

- `Any` — всегда принимать.
- `RequireUnlocked` — не принимать, пока рабочая станция залочена.
- `RequireLocalSession` — не принимать, если приложение запущено в Remote Desktop (RDP) сессии.
- `RequireUnlockedLocalSession` — комбинация двух условий.

Примечания:

- “Locked” определяется по событиям `SessionLock`/`SessionUnlock` текущей пользовательской сессии.
- “Remote” определяется по `SystemInformation.TerminalServerSession` (т.е. RDP-сессия).

## `targets.rules`

Правила поиска целевого окна. При срабатывании триггера приложение перебирает кандидатов (см. `targetSearchDepth`) и выбирает первое окно, которое матчится под правило.

```jsonc
{
  "targets": {
    "rules": [
      {
        "id": "teams",
        "prio": 95,
        "proc": [ "msedgewebview2", "ms-teams" ],
        "classIs": [ "TeamsWebView" ],
        "title": [ "Microsoft Teams", "* | Microsoft Teams" ]
      }
    ]
  }
}
```

- `id` — идентификатор правила (используется в `routesByTrigger`).
- `prio` — приоритет (больше = важнее).
- `proc` — имена процессов без `.exe` (например `chrome`).
- `classIs` — список допустимых window class name (из `GetClassNameW`).
- `title` — список паттернов для заголовка окна (OR: достаточно одного совпадения).
  - Поддерживается glob: `*` = любые символы, `?` = один символ, `\*`/`\?` = литеральные.
  - Если в паттерне **нет** `*`/`?`, он трактуется как “подстрока” (аналог `*text*`).
  - Для точного совпадения можно использовать префикс `=`: например `=Meeting`.

Матчинг:

- сначала `proc` (обязательно),
- затем (если задано) `classIs`,
- затем (если задано) `title`.

## `shortcuts`

Словарь “сочетание клавиш” → как отправлять.

```jsonc
{
  "shortcuts": {
    "teams.mute": { "keys": "Win+Alt+K", "send": "global" }
  }
}
```

`send`:

- `scan` — `SendInput` по scan-code (обычно **не зависит от раскладки**; требует, чтобы клавиша была в маппинге scan-кодов; сейчас маппинг есть для A..Z).
- `vk` — `SendInput` по virtual key (**зависит от раскладки/языка** активного ввода).
- `messages` — отправка `WM_KEYDOWN/WM_KEYUP` через `PostMessage` в целевое окно **без смены фокуса** (работает не во всех приложениях, но часто помогает при конфликте модификаторов с глобальным хоткеем).
- `global` — `SendInput` без фокуса в окно (как `scan`), но выполняется только если целевое окно по правилам существует среди top-level окон (подходит для приложений, которые сами регистрируют глобальные хоткеи).

Формат `keys` такой же, как в `inputs.hotkeys` (модификаторы: `Ctrl`, `Alt`, `Shift`, `Win`).

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

## `audioDevices`

Действия над аудиоустройствами через Core Audio (Windows).

```jsonc
{
  "audioDevices": {
    "mic.toggle": {
      "flow": "Capture",
      "role": "Communications",
      "deviceId": "",
      "action": "ToggleMute"
    }
  }
}
```

Поля:

- `flow` — тип потока:
  - `Capture` — микрофоны (input).
  - `Render` — динамики/наушники (output).
- `role` — роль девайса:
  - `Console` — “обычное” устройство по умолчанию.
  - `Multimedia` — мультимедийное.
  - `Communications` — коммуникационное (часто используют Teams/Zoom).
- `deviceId` — конкретный ID устройства (строка Windows). Если пусто — берется default по `flow`+`role`.
- `action`:
  - `ToggleMute` — переключить mute,
  - `Mute` — включить mute,
  - `Unmute` — отключить mute.

Примечания:

- Если `deviceId` задан, `role` игнорируется.
- Мут применяется на уровне устройства, это влияет на все приложения, использующие этот девайс.

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
      { "actionType": "Program", "actionId": "notepad" }
    ]
  }
}
```

- `rule` — `targets.rules[].id`. Если поле отсутствует или пустое (`""`) — действие выполняется **безусловно**, без матчинга окна.
- `actionType`:
  - `Shortcut` — отправить `shortcuts[actionId]`.
  - `Program` — запустить `programs[actionId]`.
  - `Audio` — выполнить действие из `audioDevices[actionId]`.
- `actionId` — id действия (ключ в `shortcuts`, `programs` или `audioDevices`).

Важно:

- `triggerId` должен существовать в `inputs.hotkeys[].id` **или** в `bindings` (как ключ).
  - например, для `openNotepad` выше нужно добавить `inputs.hotkeys` с `id: "openNotepad"` или сделать привязку через `bindings`.
- Роутинг выполняется в два прохода:
  1. Сначала ищутся только `Shortcut` с `send: "global"`: сначала по недавним окнам (`targetSearchDepth`), потом по всем top-level окнам.
  2. Затем ищутся все неглобальные действия: только по недавним окнам (`targetSearchDepth`).
- В каждом проходе для первого найденного подходящего окна выполняется действие и обработка завершается.
