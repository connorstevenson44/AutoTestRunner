# Walkthrough

A manual test runner for WPF apps. It runs a test on your computer while you watch: it moves the mouse to each button
or box, outlines it in orange, and says what it is doing in a panel beside the app. Each case ends at a checkpoint:
what it checked by itself (files, settings, services, scheduled tasks, what a program prints), then what to look at
yourself, with buttons that open it. When it can't find something you can put the app right by hand and try again,
skip the case, or stop.

The same scripts run unattended in CI, so a renamed button breaks the build before it breaks a tester. Unattended, the
first thing it can't find ends the run with a screenshot and a dump of the UI Automation tree.

It drives the app through UI Automation with [FlaUI](https://github.com/FlaUI/FlaUI), so it works with any WPF app,
and the cases are JSON files checked against a schema, so an editor completes and checks them.

## Running a suite

```powershell
dotnet publish src/Walkthrough.Cli -c Release -o artifacts/walk

walk run <suite folder>                         # watch it from the first case
walk run <suite folder> --cases 1-5,8 --mode steps --speed Slow
walk run <suite folder> --unattended            # as CI runs it
walk check <suite folder>                       # read every script and report what's wrong
walk list <suite folder>
```

| Key | What it does |
| --- | --- |
| **Space** | Pause, or carry on by itself |
| **Enter** | Do the next step, then wait |
| **Esc** | Stop the test (it asks first) |

`--mode itself` (the default) shows each checkpoint for a few seconds and carries on; `checks` waits at each
checkpoint; `steps` waits for Enter before every step. The keys belong to the test unless you clicked into a text box
yourself. Results go in `<suite>\results\<date and time>`: `run.log`, `summary.txt`, `junit.xml`, and a screenshot and
UI Automation dump of each point it got stuck.

Exit codes: 0 passed, 1 a case failed, 2 the app or something in `requires` is missing, 3 a script error, 4 the suite
needs administrator and walk isn't.

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
    { "call": "waitStatus", "with": { "startsWith": "Dry run finished." }, "as": "status" },
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
| `Walkthrough.Core` | The script format, its loader and checks, and the runner: pace, pausing, checkpoints, stuck, results. Plain .NET, tested on any OS against a fake app. |
| `Walkthrough.FlaUI` | The driver: finding controls, scrolling virtualised lists, buttons that only show on hover, Win32 message boxes and file pickers, screenshots and tree dumps. |
| `Walkthrough.Cli` | `walk`: the command line, and the panel and keys of a watched run. |
| `schema/` | JSON schemas for suite.json, elements.json, macros and cases, written by `python3 tools/schema.py`. A test keeps them in step with the runner. |

The first suite is [NetDeploy's](https://github.com/connorstevenson44/NetDeploy/tree/main/tests/walkthrough), ported from
an AutoHotkey runner whose behaviour this keeps.
