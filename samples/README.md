# Demos

Order Desk is a small WPF app made for these demos, and three suites test it: a suite of tests that pass, tests that
fail or get stuck, and a suite whose scripts have mistakes in them. They are written as a test team would write them,
so what you watch is a test run, not a tour.

## Watching them

Take the **Demos-ReadyToRun** artifact from any run of the Demos workflow (the run's Summary page, under Artifacts),
unzip it and double-click one of the `.cmd` files. Nothing needs installing, and nothing needs administrator: Order
Desk keeps its orders in `%LOCALAPPDATA%\OrderDesk`, and the suites delete that folder when they are done.

| File | What it shows |
| --- | --- |
| `0 Open the test launcher.cmd` | Opens the launcher on all three suites, listed on the left. Pick one and Start, or use **New case** to build a case by using Order Desk, and the pencil on a case to change it. |
| `1 Run the Order Desk tests.cmd` | Opens the launcher on the Order Desk tests. Press Start, and watch each step in the panel beside Order Desk. |
| `2 Step through the Order Desk tests.cmd` | The same, a step at a time: press Enter for each step. |
| `3 Watch tests fail.cmd` | The failure examples, holding at each checkpoint so you can read what failed. |
| `4 Find the mistakes in a suite.cmd` | Runs `autotest validate` on a suite full of mistakes, and lists every one. |

The Demos workflow runs on every push to main and every pull request, and keeps the bundle for 90 days. To make a
fresh one, open **Actions > Demos > Run workflow** on GitHub and pick the branch. Untick the check to only build the
bundle, which takes a few minutes; ticked, it also runs every demo and checks each ends as it should.

While a test runs, **Space** pauses and carries on, **Enter** goes a step at a time, and **Esc** stops.

From a clone, build Order Desk with `dotnet build samples/OrderDesk`, and AutoTestRunner as the main README says,
then run `autotest launch samples/order-desk-tests`. `tools/Run-Demos.ps1` runs them all unattended and checks
each ends as it should, as CI does.

## The suites

| Folder | What it is |
| --- | --- |
| [`OrderDesk`](OrderDesk) | Order Desk: an order form with a drop-down and tick boxes, a list of orders whose Ship and Delete buttons only show on hover, Windows message boxes and file pickers, an import that asks about each order in a window of its own, a slow export with progress, and a catalogue of 500 products that only builds the rows in view. |
| [`order-desk-tests`](order-desk-tests) | Eight cases that pass: take an order, take orders from a data file, ship, keep and delete orders, export, check the export with a script, import with a window per order, find a product in a long list, and leave nothing behind. |
| [`order-desk-failure-examples`](order-desk-failure-examples) | Eight cases that fail on purpose, each in a different way (below). |
| [`script-mistakes`](script-mistakes) | A suite with a mistake in nearly every step, for `autotest validate` to find before anything runs. |
| [`demo`](demo) | The smallest suite, used by the runner's own tests. It has no app. |

## The failure examples

Order Desk has one bug on purpose: it leaves express delivery out of the total it saves. The first case catches it.
The others fail because the test and the app disagree, as they do when an app changes and its tests don't.

| Case | What goes wrong | What the run shows |
| --- | --- | --- |
| 1. Express order is saved with its full total | The saved total is wrong | Saved total: expected `29.00`, actual `24.00` |
| 2. Keep an order when Delete is cancelled | The message box asks something else | Expected `contains "Delete 1 order?"`, actual the message's words. The case carries on. |
| 3. Shipped orders are in the export | The script ends with exit code 2, and `findstr` finds nothing | Expected `exit 0`, actual `exit 2`, and what each command printed |
| 4. Change the delivery note | `replaceText` finds nothing to replace | Expected `matches back door`, actual `no match` |
| 5. Export orders | Export orders is clicked before the box that enables it is ticked | Stuck. Watched, tick the box yourself and choose **Try again**. |
| 6. Add an order | The test clicks Submit order, which is now Add order | Stuck, with a screenshot and a UI Automation dump that lists Add order |
| 7. Connect to the warehouse | The warehouse never answers | Stuck, with what the bar at the bottom said when the wait ran out |
| 8. Import orders from a file | The test waits for an import that now asks about each order | Stuck at once, quoting the window, rather than after a minute |

Cases 1 to 4 record the failure and carry on to their checkpoint. Cases 5 to 8 get stuck: watched, the run asks
whether to try again, skip the case or stop; unattended, as in CI, the run stops there with a screenshot and a UI
Automation dump in the results folder. So CI runs each failure example on its own.
