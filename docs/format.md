# The script format

Every file is JSON, checked against the schemas in `schema/`. Point `$schema` at them and VS Code completes and checks
what you type. `autotest validate <suite>` reports every mistake in a suite at once: a misspelt step, an unknown
element or macro, a property a step doesn't take.

The vocabulary is small on purpose. When a case needs more than it offers, write a macro, or a `powershell` step;
the scripts never turn into a programming language that a person can't read as a test plan.

## suite.json

```json
{
  "name": "NetDeploy manual tests",
  "app": { "exe": "${exe}", "window": "NetDeploy", "runAsAdmin": true, "status": "statusText" },
  "variables": {
    "exe": { "firstExisting": ["NetDeploy.exe", "../../../artifacts/portable/NetDeploy.exe"] },
    "ordersDir": "C:\\Apps\\Contoso.Orders"
  },
  "helpers": { "logFile": { "regex": "Log: (.+)$" } },
  "requires": ["${exe}"],
  "reset": { "powershell": "tools/Reset.ps1", "leftovers": "tools/Leftovers.ps1" }
}
```

| Property | What it is |
| --- | --- |
| `app.exe`, `app.arguments` | The program to start, relative to the suite folder |
| `app.window` | Its main window's title |
| `app.startWindow` | The title of the window it opens first, when that isn't its main window, such as a sign-in window. `startApp` waits for it; a `useWindow` step moves on to the main window. |
| `app.runAsAdmin` | autotest restarts itself as administrator; unattended, it exits with 4 instead |
| `app.status` | An element whose text says what the app is doing. Its text before every click is kept, so `waitText` with `new` doesn't take the old text. |
| `variables` | Text, or `{ "firstExisting": [paths] }`: the first that exists, else the last. `autotest run --var name=value` overrides one. Built in: `suiteDir`, `resultsDir`, `temp`. |
| `helpers` | `${name(value)}` gives the regex's first group in value, or the whole match when it has no group |
| `requires` | Paths that must exist; if one doesn't, autotest exits with 2 |
| `reset.leftovers` | A script that prints one line for each thing an earlier run left. Unattended, or when a watched run starts from the first case, anything it prints is cleaned up first. |
| `reset.powershell` | The script that cleans up |
| `databases` | The databases `query` steps and `dbValue` checks use, by a name of your own: see [Databases and queues](#databases-and-queues) |
| `queues` | The message queues `send`, `receive` and `purge` steps and `queue` checks use, by a name of your own |
| `builder.watch` | Controls whose text the builder watches while recording: `{ "element": "savedText", "call": "waitForSaved", "check": true }`. When it changes after a step, the builder adds a wait for its new words, through `call` (given `startsWith` when the macro takes it) or a `waitText`; with `check`, it keeps the words and checks them at the checkpoint. The app's `status`, and any element with a macro named `waitFor` and its name, such as `waitForSavedText`, are watched anyway. |
| `builder.afterClick` | Steps the builder adds after a control is clicked, instead of a wait for the status text: `{ "element": "deploy", "call": "waitForDeploy", "with": { "within": "300" }, "delay": 0.5 }` adds a `wait` of `delay` seconds, then the call. |

## Cases

`cases/01-open-package.json`: the number orders the cases, the rest is the case's id.

| Property | What it is |
| --- | --- |
| `name` | What the case does |
| `needs` | The ids of the cases it carries on from |
| `needsNote` | What it needs, in words, when that isn't another case ("A clean computer") |
| `covers` | What it tries |
| `tags` | Words to pick it by, such as `["smoke"]`. The launcher filters by them; `autotest run --tags smoke` runs only those cases. |
| `review` | Why it needs looking at again, such as a change to a case it carries on from. The builder writes it; the launcher shows a warning until the case is changed, or the warning is clicked away. |
| `steps` | The steps |

## Steps

Each step does one thing, named by its key. Any step can also have:

| Property | What it is |
| --- | --- |
| `say` | Your words for the step, shown when pointing at its row. The row itself shows what the step does, such as Click [Add order], with the control on a badge. Alone, `say` is a line of its own, for what isn't a step. |
| `as` | Keeps what the step found in a variable: `${name}` is a control's text, `${name.value}`, `${name.helpText}`, `${name.id}`, `${name.selection}` its other parts. After `run`, `powershell` and `script`: `${name.code}`, `${name.output}`, `${name.first}`. Any step's `${name.seconds}` is how long it took, for an `atMost` check. |
| `within` | Seconds to wait before it counts as stuck. Unattended runs triple it. |
| `what` | The control's name on the step's badge and in messages, such as `Ship 1003` for a button found by its row. Without it, the badge shows the words on the control, else its name in elements.json. |
| `note` | For whoever maintains the script |
| `disabled` | Skips the step |

| Step | Takes | What it does |
| --- | --- | --- |
| `startApp` | | Closes any copy of the app, starts it and puts it beside the panel |
| `closeApp` | | Closes it |
| `click` | target, `how`, `whileEnabled` | Invokes, toggles or selects a control. `how: "mouse"` clicks it with the mouse, `"right"` right-clicks it and `"double"` double-clicks it. With `whileEnabled`, clicks it again and again while it is there and can be used, such as Up until the top folder. |
| `drag` | target, `to` | Drags a control with the mouse and drops it on another |
| `menu` | a list of items, `on` | Picks from a menu, each item in turn: `{ "menu": ["File", "Export…"] }`. With `on`, right-clicks that control first and picks from its menu. |
| `type` | target, `text`, `secret` | Types a key at a time, then Tab. A secret stays out of the log. |
| `choose` | target, `option` | Picks from a drop-down |
| `tick` | target, `checked` | Ticks or clears a tick box |
| `setValue` | target, `to` | Sets a slider's or a number box's value, or a box's text in one go, such as a date picker's |
| `expand` | a tree item's path, `in` | Opens each tree item on the path and selects the last: `{ "expand": ["Contoso", "Orders"] }`. `in` is the tree, when there is more than one. |
| `hover` | target | Points at a control, such as a row whose buttons only show on hover |
| `tab` | a tab's header | Opens a tab |
| `key` | `"Tab"`, `"Ctrl+A"`, `"PageDown"`, `"Shift+F10"` | Presses keys: a letter or digit, Enter, Esc, Tab, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up, Down, Left, Right, F1 to F24 or Win, with Ctrl, Alt or Shift |
| `find` | target | Waits for a control |
| `waitGone` | target | Waits for a control to go |
| `waitEnabled` | target | Waits for a control to be there and usable, not greyed out |
| `useWindow` | a window's title | Waits for another window of the app's and drives that one from now on, as after a sign-in window |
| `waitText` | target, `startsWith`, `contains`, `equals`, `matches`, `changes`, `new` | Waits for a control's text: what is typed in a text box or editor, or the name of anything else |
| `replaceText` | target, `regex`, `replacement` | Replaces the first match in a text box, in one go as a paste would. The case fails if nothing matches. |
| `answerDialog` | a message box's title, `button`, `expect` | Presses Yes, No, OK or Cancel. The case fails if the message doesn't have `expect` in it. |
| `pickFile`, `pickFolder` | the picker's title, `path` | Types a path into a Windows file or folder picker |
| `call` | a macro, `with` | Runs a macro |
| `repeat` | a list, or `{ "rows": file }`, `steps`, `item` | Runs steps for each item, in `${item}`, or each row of a data file: see below |
| `whileShows` | target, `steps`, `untilText` with `startsWith`, `contains`, `equals`, `matches`, `new` | Runs steps each time a control shows, until `untilText`'s text says it is done: see below |
| `if` | a check, `steps`, `else` | Runs `steps` when the check passes, `else` when it doesn't |
| `set` | `{ name: value }` | Sets variables |
| `run` | a program, `args`, `env` | Runs a program |
| `powershell` | a script, or a list of lines, `args` | Runs PowerShell with `args` as `$args`. `${...}` in the script is PowerShell's. |
| `script` | a `.ps1` file in the suite, `args` | Runs it with Windows PowerShell, from the suite folder, with `args` as `$args`. It fails when it exits with a code other than 0. |
| `wait` | seconds | Waits, for an app that shows nothing to wait for. A wait for what the app shows is sturdier. |
| `copy` | a file or folder, `to` | Copies a file, into a folder or onto a file, or a folder and everything in it, replacing what is there |
| `delete` | a file or folder | Deletes it, and everything in a folder. Nothing being there is fine. |
| `writeFile` | a file, `text`, `append` | Writes a text, or a list of lines, making the folder if need be; with `append`, adds to the end |
| `unzip` | a .zip file, `to` | Unzips it into a folder, replacing the folder when it is there |
| `startService`, `stopService` | a service's name | Starts or stops a Windows service, and waits until it has |
| `setJsonValue` | a JSON file, `path`, `to` | Sets a setting such as `Orders:Region`, making the objects on the way. A number or true and false stays one. Comments in the file are dropped. |
| `setAppSetting` | a .config file, `key`, `to` | Sets an appSettings value, adding it when it isn't there |
| `query` | a database, `sql`, `params` | Runs SQL on a database in suite.json. With `as`, keeps the first value it reads; `${name.rows}` is how many rows it read or changed, and `${name.output}` all it read, a line to a row. |
| `send` | a queue, `message` or `file` | Puts a message on a queue in suite.json |
| `receive` | a queue, `equals`, `contains`, `matches` | Takes the next message off the queue, waiting `within` seconds (10 when left out); nothing coming is stuck. A message that isn't the one expected fails the step. With `as`, keeps its text. |
| `purge` | a queue | Takes every message off the queue |
| `checkpoint` | a title, `checks`, `look`, `tools` | Ends a case: see below |

## Targets

A target is the name of an element in `elements.json`, a selector, or a list of either to try in order.

| Selector property | What it matches |
| --- | --- |
| `id` | The AutomationId. WPF takes it from `x:Name` when none is set; inside a template it repeats on every row. |
| `name` | The text: a button's content, a box's label |
| `type` | The UI Automation control type: Button, Edit, Text, ComboBox, ListItem, TabItem, CheckBox, RadioButton, List, Window… |
| `helpText` | The tooltip |
| `className` | The class name |
| `match` | How `name` and `helpText` are compared: `exact` (the default), `startsWith` or `contains` |
| `within` | Only inside another control; with `type: Window`, another window of the app's, such as a dialog |
| `sameRowAs` | Only on the same row as another control, such as the buttons beside a file's name |
| `scrollToFind` | Scrolls the window's long lists until it is built, for lists that only build the rows in view |
| `cell` | A grid's cell: `{ "cell": { "row": "ORD-1001", "column": "Quantity" } }` is the Quantity of the row one of whose cells says ORD-1001. With `within`, only in that grid. |
| `button`, `text`, `edit`, `tabItem`, `comboBox`, `checkBox`, `radioButton`, `listItem`, `list`, `window`, `treeItem`, `menuItem`, `dataItem`, `hyperlink` | Shorthand: `{ "button": "Add" }` is `{ "type": "Button", "name": "Add" }` |

An element can have `${parameters}`, given where it is used:

```json
"folderRow": { "type": "Button", "helpText": "${path}" }
{ "click": { "element": "folderRow", "with": { "path": "${ordersDir}\\main" } } }
```

When a control under `within` or `sameRowAs` can't be found, the runner points at that row again and looks once more,
for buttons that only show while the mouse is over their row.

## Going round and choosing

The scripts are straight lists of steps, apart from three things.

`repeat` runs steps once for each item of a list, or each row of a data file in the suite's folder: a CSV file whose
first line names its columns, or a JSON list of objects. Each row is `${item}` (its first column), and each column a
part of it:

```json
{ "repeat": { "rows": "data/orders.csv" }, "item": "order",
  "steps": [ { "call": "addOrder", "with": { "customer": "${order.Customer}", "quantity": "${order.Quantity}" } } ] }
```

 `if` runs steps when a check passes, and its `else` when it
doesn't; its check is any of the checkpoint checks below, so there is no expression language to learn:

```json
{ "if": { "equals": "${stepNumber(counter)}", "to": "${stepCount(counter)}" },
  "steps": [ { "click": { "button": "Run this step" } } ],
  "else": [ { "click": { "button": "Skip this step" } } ] }
```

`whileShows` is for an app that asks about one thing at a time in a window of its own, such as each step of a removal.
Each time the window shows, its steps run; once `untilText`'s text says it is done, the run carries on. Another window
showing up instead, nothing happening for `within` seconds (5 minutes when left out), or the window still showing
after 100 rounds, is stuck. With `as`, it keeps
the text that said it was done.

```json
{ "whileShows": { "window": "Remove install" },
  "untilText": "statusText", "matches": "^(Removed |Removal stopped)", "new": true,
  "steps": [ { "call": "answerRemovalStep", "with": { "choice": "run" } } ] }
```

## Databases and queues

Each database and queue has a name in suite.json, so the cases never repeat a connection string. `${variables}` work in
every part, so a password can come from `autotest run --var dbPassword=…`; wherever a connection shows, its password
shows as dots.

```json
"databases": {
  "orders": { "kind": "sqlServer", "connection": "Server=.\\SQLEXPRESS;Database=Orders;Integrated Security=true;TrustServerCertificate=true" },
  "stock": { "kind": "postgres", "connection": "Host=localhost;Database=stock;Username=qa;Password=${dbPassword}" }
},
"queues": {
  "newOrders": { "kind": "rabbitMq", "connection": "amqp://guest:guest@localhost:5672/", "queue": "orders.new" },
  "invoices": { "kind": "ibmMq", "connection": "localhost(1414)", "channel": "DEV.APP.SVRCONN", "queueManager": "QM1", "queue": "DEV.QUEUE.1",
                "user": "app", "password": "${mqPassword}" },
  "legacy": { "kind": "msmq", "queue": ".\\private$\\orders" }
}
```

| Kind | What it is | Driver |
| --- | --- | --- |
| `sqlServer` | SQL Server, Express and LocalDB | Microsoft.Data.SqlClient |
| `postgres` | PostgreSQL | Npgsql |
| `oracle` | Oracle Database | Oracle.ManagedDataAccess.Core |
| `mySql` | MySQL and MariaDB | MySqlConnector |
| `sqlite` | SQLite, a file | Microsoft.Data.Sqlite |
| `rabbitMq` | RabbitMQ: `connection` is its amqp:// address | RabbitMQ.Client |
| `ibmMq` | IBM MQ: `connection` is host(port), with `channel`, `queueManager`, and `user` and `password` when the channel asks | IBMMQDotnetClient |
| `msmq` | MSMQ: `queue` is its path. `format` is `xml`, as .NET's XmlMessageFormatter writes text (the default), or `text`. Either is read. | Windows PowerShell's System.Messaging |

SQL is written in the database's own dialect, and values go in `params`, never pasted into the SQL: `@id` names one,
or `:id` on Oracle. Values are read as text: numbers and dates the same on every machine, a database null as `<null>`.

```json
{ "query": "orders", "sql": "delete from Orders where Customer = @customer", "params": { "customer": "Ada" } },
{ "send": "newOrders", "file": "data/order-1001.json" },
{ "receive": "invoices", "contains": "INV-1001", "within": 30, "as": "invoice" },
{ "checkpoint": "Order 1001 is billed",
  "checks": [ { "dbValue": "orders", "sql": "select Status from Orders where Id = @id", "params": { "id": "1001" }, "equals": "Billed" },
              { "queue": "invoices", "count": 0 } ] }
```

## Macros

`macros/waitForStatusBar.json`:

```json
{
  "params": { "startsWith": {}, "within": { "default": "180" } },
  "steps": [ { "waitText": "statusText", "startsWith": "${startsWith}", "new": true, "within": "${within}", "as": "status" } ],
  "returns": "status"
}
```

Called as `{ "call": "waitForStatusBar", "with": { "startsWith": "Deploy finished." }, "as": "status" }`. A macro sees the
case's variables; its parameters and what it keeps stay its own, except what `returns` hands back.

## Checkpoints

```json
{ "checkpoint": "main is installed and running",
  "checks": [ { "service": "Contoso.Orders-main", "state": "Running" } ],
  "look": [ "The folder list shows main, v1.0.0 and a green dot." ],
  "tools": [ { "openFolder": "${ordersDir}\\main", "label": "Open main" } ] }
```

Each check says what should be true and, when it isn't, what was found instead. `label` replaces its wording;
`within` gives it seconds to come true.

| Check | Takes |
| --- | --- |
| `fileExists`, `fileMissing` | a path |
| `service` | a name, `state` (Running, Stopped; absent when left out) |
| `scheduledTask` | a path such as `\Contoso\Orders Export-main`, `exists` |
| `jsonValue` | a file, `path` such as `Orders:Region`, `equals` (or `<absent>`) |
| `appSetting` | a .config file, `key`, `equals` |
| `connectionString` | a .config file, `name` |
| `fileContains`, `logged` | a file, `text` |
| `fileCount` | a folder, `pattern`, `equals`, `folders`, `recursive` |
| `fileVersion` | an exe, `startsWith` |
| `output` | a program, `args`, `contains` |
| `lastLine` | a file being written, `contains`; waits 30 seconds for it |
| `equals`, `matches`, `contains`, `lacks` | a value, usually `${kept}`, and `to`, `regex` or `text` |
| `shows` | a target that should be in the app now |
| `enabled`, `disabled` | a target that should be usable, or greyed out |
| `ticked`, `notTicked` | a tick box |
| `selected` | an item, tab or option that should be the one picked |
| `focused` | a target that should have the keyboard |
| `says` | a target, and `equals`, `startsWith`, `contains` or `matches`: its text, or what is typed in it |
| `rows` | a list, grid or tree, `equals`: how many rows or items it has |
| `nothingLeft` | the suite's leftovers script finds nothing |
| `fileMatches` | a file, `regex`; `^` and `$` match at each line |
| `registry` | a key such as `HKLM\SOFTWARE\Contoso`, `value`, `equals` (or `<absent>`) |
| `process` | a program's name, `running` (`false`: it shouldn't be) |
| `eventLog` | a log such as `Application`, `source`, `contains`; entries since the case started count |
| `web` | an address, `status` (200 when left out), `contains` |
| `clipboard` | text the clipboard should have |
| `atMost`, `atLeast` | a number, usually `${kept}` such as `${deploy.seconds}`, and `to` |
| `dbValue` | a database, `sql`, `params`, and `equals`, `contains`, `matches` or `rows`: the first value it reads, or how many rows. With none of them, that it reads a row. |
| `queue` | a queue, `count` or `has`: how many messages are waiting, or that one has the text. It looks without taking them. |

| Tool | Opens |
| --- | --- |
| `openFolder` | a folder in File Explorer |
| `openFile` | a file in Notepad |
| `console` | a PowerShell script in a window that stays open, with `args` |
| `app` | a program, such as `taskschd.msc` |

A watched run shows the checkpoint for a few seconds and carries on (Space holds it there), or waits for you with
`--mode pause-at-checkpoints`. Unattended, it records the checks and carries on.
