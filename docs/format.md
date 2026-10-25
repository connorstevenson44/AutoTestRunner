# The script format

Every file is JSON, checked against the schemas in `schema/`. Point `$schema` at them and VS Code completes and checks
what you type. `walk check <suite>` reports every mistake in a suite at once: a misspelt step, an unknown element or
macro, a property a step doesn't take.

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
| `app.runAsAdmin` | walk restarts itself as administrator; unattended, it exits with 4 instead |
| `app.status` | An element whose text says what the app is doing. Its text before every click is kept, so `waitText` with `new` doesn't take the old text. |
| `variables` | Text, or `{ "firstExisting": [paths] }`: the first that exists, else the last. `walk run --var name=value` overrides one. Built in: `suiteDir`, `resultsDir`, `temp`. |
| `helpers` | `${name(value)}` gives the regex's first group in value, or the whole match when it has no group |
| `requires` | Paths that must exist; if one doesn't, walk exits with 2 |
| `reset.leftovers` | A script that prints one line for each thing an earlier run left. Unattended, or when a watched run starts from the first case, anything it prints is cleaned up first. |
| `reset.powershell` | The script that cleans up |

## Cases

`cases/01-open-package.json`: the number orders the cases, the rest is the case's id.

| Property | What it is |
| --- | --- |
| `name` | What the case does |
| `needs` | The ids of the cases it carries on from |
| `needsNote` | What it needs, in words, when that isn't another case ("A clean computer") |
| `covers` | What it tries |
| `steps` | The steps |

## Steps

Each step does one thing, named by its key. Any step can also have:

| Property | What it is |
| --- | --- |
| `say` | What the panel says. On an action it is shown before the action; on `find`, `waitText` and `waitGone` after it, with time to read it. Alone, it is narration. |
| `as` | Keeps what the step found in a variable: `${name}` is a control's text, `${name.value}`, `${name.helpText}`, `${name.id}`, `${name.selection}` its other parts. After `run` and `powershell`: `${name.code}`, `${name.output}`, `${name.first}`. |
| `within` | Seconds to wait before it counts as stuck. Unattended runs triple it. |
| `what` | What to call the control in a message: "I couldn't find *the start screen*." |
| `note` | For whoever maintains the script |
| `disabled` | Skips the step |

| Step | Takes | What it does |
| --- | --- | --- |
| `startApp` | | Closes any copy of the app, starts it and puts it beside the panel |
| `closeApp` | | Closes it |
| `click` | target, `how: "mouse"`, `whileEnabled` | Invokes, toggles or selects a control; with `how`, clicks it with the mouse. With `whileEnabled`, clicks it again and again while it is there and can be used, such as Up until the top folder. |
| `type` | target, `text`, `secret` | Types a key at a time, then Tab. A secret stays out of the log. |
| `choose` | target, `option` | Picks from a drop-down |
| `tick` | target, `checked` | Ticks or clears a tick box |
| `hover` | target | Points at a control, such as a row whose buttons only show on hover |
| `tab` | a tab's header | Opens a tab |
| `key` | `"Tab"`, `"Ctrl+A"` | Presses keys |
| `find` | target | Waits for a control |
| `waitGone` | target | Waits for a control to go |
| `waitText` | target, `startsWith`, `contains`, `equals`, `matches`, `changes`, `new` | Waits for a control's text: what is typed in a text box or editor, or the name of anything else |
| `replaceText` | target, `regex`, `replacement` | Replaces the first match in a text box, in one go as a paste would. The case fails if nothing matches. |
| `answerDialog` | a message box's title, `button`, `expect` | Presses Yes, No, OK or Cancel. The case fails if the message doesn't have `expect` in it. |
| `pickFile`, `pickFolder` | the picker's title, `path` | Types a path into a Windows file or folder picker |
| `call` | a macro, `with` | Runs a macro |
| `repeat` | a list, `steps`, `item` | Runs steps for each item, in `${item}` |
| `whileShows` | target, `steps`, `untilText` with `startsWith`, `contains`, `equals`, `matches`, `new` | Runs steps each time a control shows, until `untilText`'s text says it is done: see below |
| `if` | a check, `steps`, `else` | Runs `steps` when the check passes, `else` when it doesn't |
| `set` | `{ name: value }` | Sets variables |
| `run` | a program, `args`, `env` | Runs a program |
| `powershell` | a script, or a list of lines, `args` | Runs PowerShell with `args` as `$args`. `${...}` in the script is PowerShell's. |
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
| `button`, `text`, `edit`, `tabItem`, `comboBox`, `checkBox`, `radioButton`, `listItem`, `list`, `window` | Shorthand: `{ "button": "Add" }` is `{ "type": "Button", "name": "Add" }` |

An element can have `${parameters}`, given where it is used:

```json
"folderRow": { "type": "Button", "helpText": "${path}" }
{ "click": { "element": "folderRow", "with": { "path": "${ordersDir}\\main" } } }
```

When a control under `within` or `sameRowAs` can't be found, the runner points at that row again and looks once more,
for buttons that only show while the mouse is over their row.

## Going round and choosing

The scripts are straight lists of steps, apart from three things.

`repeat` runs steps once for each item of a list. `if` runs steps when a check passes, and its `else` when it
doesn't; its check is any of the checkpoint checks below, so there is no expression language to learn:

```json
{ "if": { "equals": "${stepNumber(counter)}", "to": "${stepCount(counter)}" },
  "steps": [ { "click": { "button": "Run this step" } } ],
  "else": [ { "click": { "button": "Skip this step" } } ] }
```

`whileShows` is for an app that asks about one thing at a time in a window of its own, such as each step of a removal.
Each time the window shows, its steps run; once `untilText`'s text says it is done, the run carries on. Another window
showing up instead, or nothing happening for `within` seconds (5 minutes when left out), is stuck. With `as`, it keeps
the text that said it was done.

```json
{ "whileShows": { "window": "Remove install" },
  "untilText": "statusText", "matches": "^(Removed |Removal stopped)", "new": true,
  "steps": [ { "call": "removalStep", "with": { "choice": "run" } } ] }
```

## Macros

`macros/waitStatus.json`:

```json
{
  "params": { "startsWith": {}, "within": { "default": "180" } },
  "steps": [ { "waitText": "statusText", "startsWith": "${startsWith}", "new": true, "within": "${within}", "as": "status" } ],
  "returns": "status"
}
```

Called as `{ "call": "waitStatus", "with": { "startsWith": "Deploy finished." }, "as": "status" }`. A macro sees the
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
| `nothingLeft` | the suite's leftovers script finds nothing |

| Tool | Opens |
| --- | --- |
| `openFolder` | a folder in File Explorer |
| `openFile` | a file in Notepad |
| `console` | a PowerShell script in a window that stays open, with `args` |
| `app` | a program, such as `taskschd.msc` |

A watched run shows the checkpoint for a few seconds and carries on (Space holds it there), or waits for you with
`--mode checks`. Unattended, it records the checks and carries on.
