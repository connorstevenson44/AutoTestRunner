# Demos

Order Desk is a small WPF app made for these demos, and three suites drive it. Together they show what AutoTestRunner
does when a test passes, when it fails, when it gets stuck, and when the script itself has a mistake.

## Watching them

Take the **Demos-ReadyToRun** artifact from any run of the Demos workflow (the run's Summary page, under Artifacts),
unzip it and double-click one of the `.cmd` files. Nothing needs installing, and nothing needs administrator: Order
Desk keeps its orders in `%LOCALAPPDATA%\OrderDesk`, and the suites delete that folder when they are done.

| File | What it shows |
| --- | --- |
| `1 Watch the feature tour.cmd` | Opens the launcher on the feature tour. Press Start, and watch each step in the panel beside Order Desk. |
| `2 Step through the feature tour.cmd` | The same, a step at a time: press Enter for each step. |
| `3 Watch tests fail.cmd` | The failure examples, holding at each checkpoint so you can read what failed. |
| `4 Find the mistakes in a suite.cmd` | Runs `autotest validate` on a suite full of mistakes, and lists every one. |

The Demos workflow runs on every push to main and every pull request, and keeps the bundle for 90 days. To make a
fresh one, open **Actions > Demos > Run workflow** on GitHub and pick the branch. Untick the check to only build the
bundle, which takes a few minutes; ticked, it also runs every demo and checks each ends as it should.

While a test runs, **Space** pauses and carries on, **Enter** goes a step at a time, and **Esc** stops.

From a clone, build Order Desk with `dotnet build samples/order-desk-app`, and AutoTestRunner as the main README says,
then run `autotest launch samples/order-desk-feature-tour`. `tools/Run-Demos.ps1` runs them all unattended and checks
each ends as it should, as CI does.

## The suites

| Folder | What it is |
| --- | --- |
| [`order-desk-app`](order-desk-app) | Order Desk: an order form with a drop-down and tick boxes, a list of orders whose Ship and Delete buttons only show on hover, Windows message boxes and file pickers, an import that asks about each order in a window of its own, a slow export with progress, and a catalogue of 500 products that only builds the rows in view. |
| [`order-desk-feature-tour`](order-desk-feature-tour) | Eight cases that pass, each showing something tests can do: type, pick and tick; wait for the bar at the bottom; macros, `repeat` and `if`; buttons that show on hover; message boxes; slow work; commands with their output streaming into the panel; a window per item with `whileShows`; scrolling a long list; and checkpoints that check files, logs and what the app shows. |
| [`order-desk-failure-examples`](order-desk-failure-examples) | Eight cases that fail on purpose, each in a different way (below). |
| [`script-mistakes`](script-mistakes) | A suite with a mistake in nearly every step, for `autotest validate` to find before anything runs. |
| [`demo`](demo) | The smallest suite, used by the runner's own tests. It has no app. |

## The failure examples

Order Desk has one bug on purpose: it leaves express delivery out of the total it saves. The first case catches it.
The others fail because the test and the app disagree, as they do when an app changes and its tests don't.

| Case | What goes wrong | What the run says |
| --- | --- | --- |
| 1. Catch a bug | The saved total is wrong | The check that failed, and what it found instead: `it is 24.00` |
| 2. A message box says something else | `answerDialog` expects other words | What the message really said. The case carries on. |
| 3. A command fails | A PowerShell step ends with exit code 2, and `findstr` finds nothing | The exit codes, and what each command printed |
| 4. The text to change isn't there | `replaceText` finds nothing to replace | What it looked for |
| 5. Stuck: a button is greyed out | Export orders is clicked before the box that enables it is ticked | Watched, you can tick the box yourself and choose **Try again**. |
| 6. Stuck: a button was renamed | The test clicks Submit order, which is now Add order | A screenshot and a UI Automation dump, which lists Add order |
| 7. Stuck: waiting for something that never happens | The warehouse never answers | What the bar at the bottom said when the wait ran out |
| 8. Stuck: another window shows up | The test waits for an import that asks about each order | It stops at once, quoting the window, rather than waiting a minute |

Cases 1 to 4 record the failure and carry on to their checkpoint. Cases 5 to 8 get stuck: watched, the run asks
whether to try again, skip the case or stop; unattended, as in CI, the run stops there with a screenshot and a UI
Automation dump in the results folder. So CI runs each failure example on its own.
