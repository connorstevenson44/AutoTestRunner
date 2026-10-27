# AutoTestRunner

A manual test runner for WPF apps. It runs a test on your computer while you watch: it moves the mouse to each button
or box, outlines it in orange, and shows what it is doing in a panel beside the app, with every command it runs and
what that command prints as it prints it. Each case ends at a checkpoint:
what it checked by itself (files, settings, services, scheduled tasks, what a program prints), then what to look at
yourself, with buttons that open it. When it can't find something you can put the app right by hand and try again,
skip the case, or stop.

The same scripts run unattended in CI, so a renamed button breaks the build before it breaks a tester. Unattended, the
first thing it can't find ends the run with a screenshot and a dump of the UI Automation tree.

It drives the app through UI Automation with [FlaUI](https://github.com/FlaUI/FlaUI), so it works with any WPF app,
and the cases are JSON files checked against a schema, so an editor completes and checks them.

## Running a suite

```powershell
dotnet publish src/AutoTestRunner.Cli -c Release -o artifacts/autotest

autotest                            # opens the launcher on every suite near autotest, and in the folder it started in
autotest launch <suite folder>      # opens the launcher on one suite, with the suites beside it listed too
autotest record <suite folder> --name "Take an order"     # builds a new case by using the app; see Building a case
autotest run <suite folder>         # runs while you watch, from the first case
autotest run <suite folder> --cases 1-5,8 --mode step-by-step --speed Slow
autotest run <suite folder> --tags smoke,orders --unattended    # only the cases tagged smoke or orders, as CI runs it
autotest validate <suite folder>    # reads every script and reports what's wrong, without running anything
autotest list <suite folder>        # lists the cases in order
autotest preview-ui                 # plays a made-up run, to see the launcher and panel without an app
autotest run --help                 # every option a command takes
```

`--unattended` runs with no panel and no pauses. `--tags` picks the cases tagged with any of the tags given, after
`--cases` picks by number; a case's tags are in its `tags` list.

| Key | What it does |
| --- | --- |
| **Space** | Pause, or carry on by itself |
| **Enter** | Do the next step, then wait |
| **Esc** | Stop the test (it asks first) |

The panel shows the case and a bar of every case, coloured as each ends; the controls as icons, with their keys under
them; what it is doing now, the command a step is about to run and what it is waiting for; and below, everything it
has done. Each step is a row of what it does and what to, such as Click [Add order] or Type `Ada` in [customer]: a
control is on a badge, and pointing at it shows its type, AutomationId, name in `elements.json` and how it was found.
A wait shows what it saw and how long it took. Each command shows with its output, streaming while it runs, then its
exit code. A checkpoint is a table of its checks, each with what it expected beside what it found, passed or not. A
step that fails while the case carries on, such as a message box that says something else, turns red with what it
expected and found; the next checkpoint is red too, and lists it.

`--mode auto` (the default) shows each checkpoint for a few seconds and carries on; `pause-at-checkpoints` waits at
each checkpoint; `step-by-step` waits for Enter before every step. `--speed` is `Slow`, `Modest` (the default) or `Fast`, or
`Manual`: see [Running by hand](#running-by-hand). The keys belong to the test unless you clicked into a text box
yourself. Results go in `<suite>\results\<date and time>`: `run.log`, `summary.txt`, `junit.xml`, and a screenshot and
UI Automation dump of each point it got stuck. Something that goes wrong in autotest itself, rather than in a test, is
written to `errors.log` in one logs folder, `%LOCALAPPDATA%\AutoTestRunner\logs`, with where it happened, for a bug
report: a crash or mistake in the launcher, a run, the builder or a clean up, and the run.log to look at with it. The
small folder button at the bottom left of the launcher opens it.

Exit codes: 0 passed, 1 a case failed, 2 the app or something in `requires` is missing, 3 a script error, 4 the suite
needs administrator and autotest isn't.

The launcher lists every suite it finds near autotest: beside it in a ready-to-run kit, and up to three folders down
from the folder it is in or was started from, such as `samples/` in a clone. Pick one on the left to see its cases,
whether its app is there, and how its last run went; a suite with mistakes in its scripts shows them instead. A shield
on Start means the suite needs administrator, which autotest asks for when it starts.

A case that runs after others shows their numbers in a small pill with an arrow, such as ← 1, 2: amber when one of
them isn't ticked. Click the pill to tick the case with every case it needs. **Clean up** runs the suite's reset
script and shows its log, which folds away, or hides with ✕.

A chip for each tag picks the cases tagged with it. The **⋯** on a case copies, renames, moves or deletes it: moving
keeps every case after the cases it carries on from, and deleting one that others carry on from has them carry on
from what it did, marked as needing a look. The icons beside the suite's name open:

- **Named controls**: each name in `elements.json`, how it finds its control and where it is used. The eye points it
  out in the app, the rename icon changes it everywhere it is used, and **Used nowhere** shows the ones nothing uses.
- **Without ids**: the controls on the app's screen a test would use that have no AutomationId, red with nothing to
  find them by and amber with only their words, each with the XAML that gives it one, to copy.
- **New suite**: pick an app's `.exe`, start it to read its window's title, and point at its status bar. It writes
  `suite.json`, `elements.json` and the `waitForStatusBar` macro, ready for New case.

When a watched run is stuck on a step that uses a control, **Pick it** lets you click the control the step should
use. It is saved in `elements.json` when the step names an element, else in the step's own file, and the step is
tried again.

### Running by hand

With the speed set to **Manual**, in the launcher, the panel or with `--speed Manual`, you do every click, key and
drag yourself, and autotest never moves the mouse or presses a key. It still does everything else: starts and closes
the app, runs commands and scripts, copies files, waits for the app and makes the checks. For a step in the app, the
panel says **Your turn** with what to do, such as Type `Ada` in [customer], and the control is outlined in amber, with the mouse going straight through the outline; it
carries on by itself once the step is done:

- Typing, ticking, picking from a drop-down and setting a value are done once the control holds what the step says,
  however it got there: pasted, typed with mistakes put right, or picked with the keyboard. A password box can't be
  read, so it is done once you leave it or press Enter in it.
- A click, right-click or double-click is done once you click the control while it can be used; opening a tab, once the
  tab is open; pointing at one, once the mouse is over it. A key step is done once you press those keys, in any window. A menu outlines each item in turn, and is
  done once you click the last. Expanding a tree is done once the last item is picked, and a drag once you drop the
  control on the other one.
- A message box is done once it closes, and a file picker once it closes with the file named in it. Closed another way,
  such as with Cancel, the step fails and the case carries on, as there is no going back to it.

Anything else you do meanwhile is yours: a wrong click or a detour doesn't fail the step, it just isn't the step, so the
run goes on waiting. Space, Enter and Esc belong to the app, unless the panel itself has the focus; the panel's
buttons still pause, step and stop. Changing the speed from Manual while a step waits has autotest do that step itself.

## Building a case

**New case** in the launcher opens the builder: a panel beside the app, which gets the app to where the case starts
(a fresh start, or where another case leaves off), then records.

- **Record** (on at the start), or **F9** in the app or the panel: use the app. Each click, pick from a drop-down, tick
  and tab becomes a step, what you type becomes one when you leave the box, and keys such as Enter or Ctrl+S become key
  steps. A click that changes the status text gets a wait for its new words, so the case waits for the app, not a
  time. Message boxes and file pickers become `answerDialog`, `pickFile` and `pickFolder`. A right-click and the menu
  items picked after it become a `menu` step, and a double-click a click with `how: double`. F9 was chosen as Windows
  and WPF's own controls give it no meaning; while the builder is open it is kept from the app.
- The amber **pick tools** under the toolbar do something with a control: choose the tool, then click the control in
  the app, and the click doesn't reach the app. As the mouse goes, each control is outlined with how a step would find
  it, or in grey with why the tool can't be used on it, such as a wait for the text of a control that shows none.
  **Click**, **Point at**, **Wait for it** › (to show, to go, its text, until it can be used), **Keep its text**,
  **Check it** › (it shows, its text, can be used, greyed out, ticked, selected, has the keyboard, says, rows),
  **Name it**, and **More** › (**Set its value…**, **Watch it**, **After clicking it…**). Esc puts a tool down. Once one
  has been used, Ctrl+click while recording uses it again.
- Each step shows how it finds its control, with a dot: green for a name in `elements.json` or an AutomationId, amber
  for what it says, red when more than one control matches. **Name it** adds a red or amber one to `elements.json`
  and has the steps use the name. Over a step, ▶ tries it in the app, the arrows move it, and the bin takes it out;
  click its words to change what the panel says.
- The toolbar adds steps that aren't about one control: **Press keys**, **Pick from a menu**, **Wait** a number of
  seconds and **Show a message**, each a button; **Script** › **Run a script…** picks a `.ps1` or `.cs` in the suite
  and runs it, and **New PowerShell script…** or **New C# script…** writes one in `tools/` from a short template and
  opens it, in Notepad++ when it is installed, else Notepad, and its step plays once you've written it. The page icon on
  a script step opens its file. **Use a macro** › asks for a macro's parameters. **In the app** › and **On this
  computer** › offer every other step, such as opening tree items, a drag, copying a file or setting a JSON value, each
  with a form: browse for a file, pick a control. **Databases and queues** › queries a database, or sends, receives
  or empties a queue, picked from those in suite.json.
- The chevron on a step folds out its details, as a form; the circle switches it off, so the case skips it and keeps
  it. **+** by the checkpoint adds any check, from a form whose **Try now** runs it against the app as it is. The `{x}`
  by a box, and over the steps, lists the kept values and suite variables with what each holds now, to put one in.
- Shift+click picks a run of steps, Ctrl+click one more. **Make a macro** writes them to `macros/` when you save, with
  the values you tick, such as what is typed, as its parameters, and puts a call to it in their place. **Repeat** runs
  them for each of a list of values or the rows of a data file; **Only if…** runs them when a check passes. The
  arrow on a repeat or an if takes its steps out again.
- **More** › **Watch it** has the builder wait for a control's new words whenever a step changes them, as it does for
  the status text; **After clicking it…** adds a macro after each click on it, after a delay if need be. Both go in
  `suite.json`'s `builder` section, for every case built after: see [docs/format.md](docs/format.md).
- **Save** writes `cases/NN-name.json`, as a person would write it, then checks the suite. The checkpoint's title,
  checks and the manual checks for the tester are at the bottom of the panel.

### Changing a case

The pencil on a case opens it in the builder. The app is taken to where the case starts, by playing the cases it
carries on from, and its steps are listed, greyed until they have been played. Over a step:

- **Run to here** plays the case up to that step.
- **Roll back to here**, on a step already played, starts the app again from where the case starts, after the
  suite's reset, and plays up to that step. The steps after it stay, greyed, to play again.
- **Edit from here** takes the app to just after that step, removes the steps after it, and records new ones. It
  first lists the steps that will be removed, and the cases that start where this one ends, ticked: those ticked get
  a warning in the launcher, saying why, until they are changed too or the warning is cleared. **Confirm** goes on;
  **Cancel** leaves the case as it was.

Record and the pick tools wait until the app is at the last step. Saving writes only what changed: the steps before the first
one changed, the checkpoint, and everything else in the file stay as they were written.

`autotest record <suite folder>` opens the builder straight away: `--name` names a new case, `--after <case>` starts it
where that case leaves off, and `--edit <case>` opens that case to change it. CI builds a case in Order Desk this way,
with clicks and keys sent to the screen, then changes case 1 from a step, and runs them:
[tools/Try-Builder.ps1](tools/Try-Builder.ps1).

## Writing a suite

A suite is a folder, one per app. See [docs/format.md](docs/format.md) for every step and check, and
[samples/demo](samples/demo) for a small one. [samples/README.md](samples/README.md) has demos to watch: a feature
tour, tests that fail on purpose, and a suite full of mistakes, all against a small app made for them.

```
suite.json        how to start and reset the app, its variables
elements.json     the app's controls by name
macros/           reusable steps with parameters
cases/            01-open-package.json …, run in file order
```

A case reads like the manual test it automates:

```json
{
  "name": "Dry run",
  "needs": ["open-package"],
  "steps": [
    { "type": "newInstallName", "text": "main" },
    { "click": { "button": "Dry run" } },
    { "call": "waitForStatusBar", "with": { "startsWith": "Dry run finished." }, "as": "status" },
    { "checkpoint": "Nothing changed",
      "checks": [ { "fileMissing": "${ordersDir}" }, { "service": "Contoso.Orders-main" } ],
      "look": [ "The summary lists the service and task it would add." ],
      "tools": [ { "openFile": "${logFile(status)}", "label": "Open the log" } ] }
  ]
}
```

A case can read and change the app's database, and send and take messages from its queues: SQL Server, PostgreSQL,
Oracle, MySQL and SQLite, and RabbitMQ, IBM MQ and MSMQ, each named once in suite.json with its connection. A
`query` step keeps a value, `send`, `receive` and `purge` work a queue, and the `dbValue` and `queue` checks say what
is in them at a checkpoint: see [Databases and queues](docs/format.md#databases-and-queues).

Setup that is easier written as code can be a C# file instead of PowerShell: `{ "script": "tools/SeedOrders.cs",
"args": ["${dataDir}"] }` runs it with `dotnet run`, which builds it the first time and after it changes. It gets the
step's args as `args`, a line such as `#:package Microsoft.Data.SqlClient@7.1.1` at the top brings in a NuGet package,
and an exit code other than 0, or an exception, fails the step. The suite's `reset.script` and `reset.leftovers` can
be C# too. It needs the .NET 10 SDK on the test machine, where PowerShell is always there, so `.ps1` stays the simple
choice for a few commands.

For apps that adopt it: give every control a test touches an `x:Name` or `AutomationProperties.AutomationId`, inside
templates too (bound, so each row's is different). Selectors can find controls by their text and tooltips, but ids
keep working when the wording changes.

## The parts

| Project | What it is |
| --- | --- |
| `AutoTestRunner.Core` | The script format, its loader and checks, the runner (pace, pausing, checkpoints, stuck, results) and the builder's engine (naming controls, turning what a person does into steps, writing the case). Plain .NET, tested on any OS against a fake app. |
| `AutoTestRunner.Data` | The databases and queues: each kind through its vendor's own .NET driver, and MSMQ through Windows PowerShell. Its tests run against real servers in containers in CI. |
| `AutoTestRunner.FlaUI` | The driver: finding controls, scrolling virtualised lists, buttons that only show on hover, Win32 message boxes and file pickers, screenshots and tree dumps; and the inspector, which reads the control under the mouse for the builder. |
| `AutoTestRunner.Cli` | `autotest`: the command line, the launcher, the panel and keys of a watched run, and the builder. |
| `schema/` | JSON schemas for suite.json, elements.json, macros and cases, written by `python3 tools/schema.py`. A test keeps them in step with the runner. |

Inside `AutoTestRunner.Cli`, `WatchedRun` runs the cases with the panel beside the app, and `GlobalKeyHook` catches
Space, Enter and Esc wherever the focus is. `BuildSession` is the builder: `RecordingHook` notes the person's clicks and
keys on the UI thread, and `BuildWorker` makes steps of them on a thread of its own, through `Recorder` and
`TargetSuggester` in `Core/Building`. Its windows are in `Views/`: `LauncherWindow` lists the suites and `SuitePane`
picks the cases, `RunPanel` is the panel, `ActivityFeed` is the panel's list of everything done so far, and
`BuilderPanel` and `OutlineWindow` are the builder's. In `AutoTestRunner.Core`, whose
folders are `Scripting/` (the format), `Running/` (the runner) and `Driving/` (what a driver does), `Runner` plays the steps, and waits for the person's in a manual run (`Runner.ByHand.cs`), `RunControl` holds the pace and pauses,
`CommandReportingMachine` shows each command and its output as it runs, and `IRunView` is what a run reports to: the
panel when watched, the console when unattended.

The first suite is [NetDeploy's](https://github.com/connorstevenson44/NetDeploy/tree/main/tests/ui-tests), ported from
an AutoHotkey runner whose behaviour this keeps.
