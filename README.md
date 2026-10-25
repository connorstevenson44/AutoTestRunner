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

autotest launch <suite folder>      # opens the launcher: tick cases, pick how it runs, Start
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
has done: steps, checkpoints, and each command with its output, streaming while it runs, then its exit code.

`--mode auto` (the default) shows each checkpoint for a few seconds and carries on; `pause-at-checkpoints` waits at
each checkpoint; `step-by-step` waits for Enter before every step. The keys belong to the test unless you clicked into a text box
yourself. Results go in `<suite>\results\<date and time>`: `run.log`, `summary.txt`, `junit.xml`, and a screenshot and
UI Automation dump of each point it got stuck.

Exit codes: 0 passed, 1 a case failed, 2 the app or something in `requires` is missing, 3 a script error, 4 the suite
needs administrator and autotest isn't.

## Writing a suite

A suite is a folder, one per app. See [docs/format.md](docs/format.md) for every step and check, and
[samples/demo](samples/demo) for a small one.

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
| `AutoTestRunner.Core` | The script format, its loader and checks, and the runner: pace, pausing, checkpoints, stuck, results. Plain .NET, tested on any OS against a fake app. |
| `AutoTestRunner.FlaUI` | The driver: finding controls, scrolling virtualised lists, buttons that only show on hover, Win32 message boxes and file pickers, screenshots and tree dumps. |
| `AutoTestRunner.Cli` | `autotest`: the command line, and the panel and keys of a watched run. |
| `schema/` | JSON schemas for suite.json, elements.json, macros and cases, written by `python3 tools/schema.py`. A test keeps them in step with the runner. |

Inside `AutoTestRunner.Cli`, `LauncherWindow` picks the cases, `WatchedRun` runs them with the `RunPanel` beside the
app, `ActivityFeed` is the panel's list of everything done so far, and `GlobalKeyHook` catches Space, Enter and Esc
wherever the focus is. In `AutoTestRunner.Core`, `Runner` plays the steps, `RunControl` holds the pace and pauses,
`CommandReportingMachine` shows each command and its output as it runs, and `IRunView` is what a run reports to: the
panel when watched, the console when unattended.

The first suite is [NetDeploy's](https://github.com/connorstevenson44/NetDeploy/tree/main/tests/ui-tests), ported from
an AutoHotkey runner whose behaviour this keeps.
