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
autotest run <suite folder> --unattended    # no panel and no pauses, as CI runs it
autotest validate <suite folder>    # reads every script and reports what's wrong, without running anything
autotest list <suite folder>        # lists the cases in order
autotest preview-ui                 # plays a made-up run, to see the launcher and panel without an app
```

| Key | What it does |
| --- | --- |
| **Space** | Pause, or carry on by itself |
| **Enter** | Do the next step, then wait |
| **Esc** | Stop the test (it asks first) |

The panel shows the case and a bar of every case, coloured as each ends; the controls as icons, with their keys under
them; what it is doing now, the command a step is about to run and what it is waiting for; and below, everything it
has done: steps, checkpoints, and each command with its output, streaming while it runs, then its exit code. A step
that fails while the case carries on, such as a message box that says something else, turns red, and pointing at it
says why; the next checkpoint is red too, and lists it.

`--mode auto` (the default) shows each checkpoint for a few seconds and carries on; `pause-at-checkpoints` waits at
each checkpoint; `step-by-step` waits for Enter before every step. The keys belong to the test unless you clicked into a text box
yourself. Results go in `<suite>\results\<date and time>`: `run.log`, `summary.txt`, `junit.xml`, and a screenshot and
UI Automation dump of each point it got stuck.

Exit codes: 0 passed, 1 a case failed, 2 the app or something in `requires` is missing, 3 a script error, 4 the suite
needs administrator and autotest isn't.

The launcher lists every suite it finds near autotest: beside it in a ready-to-run kit, and up to three folders down
from the folder it is in or was started from, such as `samples/` in a clone. Pick one on the left to see its cases,
whether its app is there, and how its last run went; a suite with mistakes in its scripts shows them instead. A shield
on Start means the suite needs administrator, which autotest asks for when it starts.

## Building a case

**New case** in the launcher opens the builder: a panel beside the app, which gets the app to where the case starts
(a fresh start, or where another case leaves off), then records.

- **Record** (on at the start): use the app. Each click, pick from a drop-down, tick and tab becomes a step, what you
  type becomes one when you leave the box, and keys such as Enter or Ctrl+S become key steps. A click that changes the
  status text gets a wait for its new words, so the case waits for the app, not a time. Message boxes and file pickers
  become `answerDialog`, `pickFile` and `pickFolder`.
- **Pick**, or Ctrl+click while recording: point at a control and it is outlined with how a step would find it; click
  it and the panel offers Click, Point at, Wait for it, Wait for it to go, Wait for its text, Keep its text, Check it
  shows, Check its text, and Name it. The click doesn't reach the app.
- Each step shows how it finds its control, with a dot: green for a name in `elements.json` or an AutomationId, amber
  for what it says, red when more than one control matches. **Name it** adds a red or amber one to `elements.json`
  and has the steps use the name. Over a step, ▶ tries it in the app, the arrows move it, and the bin takes it out;
  click its words to change what the panel says.
- **Step** adds a step that isn't a click: **Run a script…** picks a `.ps1` in the suite and runs it; **New script…**
  writes one in `tools/` from a short template and opens it, in Notepad++ when it is installed, else Notepad, and
  its step plays once you've written it; **Say something…** and **Wait…**. The page icon on a script step opens its file.
- **Watch it** under a picked control has the builder wait for its new words whenever a step changes them, as it
  does for the status text; **After clicking it…** adds a macro after each click on it, after a delay if need be.
  Both go in `suite.json`'s `builder` section, for every case built after: see [docs/format.md](docs/format.md).
- **Save** writes `cases/NN-name.json`, as a person would write it, then checks the suite. The checkpoint's title,
  checks and what to look at are at the bottom of the panel.

### Changing a case

The pencil on a case opens it in the builder. The app is taken to where the case starts, by playing the cases it
carries on from, and its steps are listed, greyed until they have been played. Over a step:

- **Run to here** plays the case up to that step.
- **Roll back to here**, on a step already played, starts the app again from where the case starts, after the
  suite's reset, and plays up to that step. The steps after it stay, greyed, to play again.
- **Edit from here** takes the app to just after that step, takes out the steps after it, and records new ones. It
  first shows which steps go, and the cases that carry on from this one, ticked: those ticked get a warning in the
  launcher, saying why, until they are changed too or the warning is clicked away.

Record and Pick wait until the app is at the last step. Saving writes only what changed: the steps before the first
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
  "name": "Try a deploy without changing anything",
  "needs": ["open-package"],
  "steps": [
    { "type": "newInstallName", "text": "main", "say": "Name the new install main" },
    { "click": { "button": "Dry run" }, "say": "Click Dry run, to see what a deploy would do without doing it" },
    { "call": "waitForStatusBar", "with": { "startsWith": "Dry run finished." }, "as": "status" },
    { "checkpoint": "The dry run changed nothing",
      "checks": [ { "fileMissing": "${ordersDir}" }, { "service": "Contoso.Orders-main" } ],
      "look": [ "The bar at the bottom says Dry run finished. Nothing was changed." ],
      "tools": [ { "openFile": "${logFile(status)}", "label": "Open the log" } ] }
  ]
}
```

For apps that adopt it: give every control a test touches an `x:Name` or `AutomationProperties.AutomationId`, inside
templates too (bound, so each row's is different). Selectors can find controls by their text and tooltips, but ids
keep working when the wording changes.

## The parts

| Project | What it is |
| --- | --- |
| `AutoTestRunner.Core` | The script format, its loader and checks, the runner (pace, pausing, checkpoints, stuck, results) and the builder's engine (naming controls, turning what a person does into steps, writing the case). Plain .NET, tested on any OS against a fake app. |
| `AutoTestRunner.FlaUI` | The driver: finding controls, scrolling virtualised lists, buttons that only show on hover, Win32 message boxes and file pickers, screenshots and tree dumps; and the inspector, which reads the control under the mouse for the builder. |
| `AutoTestRunner.Cli` | `autotest`: the command line, the launcher, the panel and keys of a watched run, and the builder. |
| `schema/` | JSON schemas for suite.json, elements.json, macros and cases, written by `python3 tools/schema.py`. A test keeps them in step with the runner. |

Inside `AutoTestRunner.Cli`, `WatchedRun` runs the cases with the panel beside the app, and `GlobalKeyHook` catches
Space, Enter and Esc wherever the focus is. `BuildSession` is the builder: `RecordingHook` notes the person's clicks and
keys on the UI thread, and `BuildWorker` makes steps of them on a thread of its own, through `Recorder` and
`TargetSuggester` in `Core/Building`. Its windows are in `Views/`: `LauncherWindow` lists the suites and `SuitePane`
picks the cases, `RunPanel` is the panel, `ActivityFeed` is the panel's list of everything done so far, and
`BuilderPanel` and `OutlineWindow` are the builder's. In `AutoTestRunner.Core`, whose
folders are `Scripting/` (the format), `Running/` (the runner) and `Driving/` (what a driver does), `Runner` plays the steps, `RunControl` holds the pace and pauses,
`CommandReportingMachine` shows each command and its output as it runs, and `IRunView` is what a run reports to: the
panel when watched, the console when unattended.

The first suite is [NetDeploy's](https://github.com/connorstevenson44/NetDeploy/tree/main/tests/ui-tests), ported from
an AutoHotkey runner whose behaviour this keeps.
