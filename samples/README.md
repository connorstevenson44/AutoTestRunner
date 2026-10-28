# Demos

Order Desk is a small WPF app made for these demos, as a coffee and tea shop's order-taking app would be: staff sign
in, orders and customers are in a SQLite database, and shipped orders go to the warehouse over RabbitMQ. Four suites
test it: the Order Desk tests, which pass; the warehouse tests, which pass against a RabbitMQ on the computer; tests
that fail or get stuck; and a suite whose scripts have mistakes in them. They are written as a test team would write them,
so what you watch is a test run, not a tour. The showcase is the tour: one case, a day at the shop in eight parts, that
shows most of what AutoTestRunner does, to show people who don't write tests.

## Watching them

Take the **Demos-ReadyToRun** artifact from any run of the Demos workflow (the run's Summary page, under Artifacts),
unzip it and double-click one of the `.cmd` files. Nothing needs installing but for the warehouse tests, and nothing
needs administrator: Order Desk keeps its database, settings and activity log in `%LOCALAPPDATA%\OrderDesk`, and the
suites put back everything they change (that folder, the exports folder, Order Desk's key in the registry and its
scheduled task) when they are done.

| File | What it shows |
| --- | --- |
| `0 Open the test launcher.cmd` | Opens the launcher on all the suites, listed on the left. Pick one and Start, or use **New case** to build a case by using Order Desk, and the pencil on a case to change it. |
| `1 Run the Order Desk tests.cmd` | Opens the launcher on the Order Desk tests. Press Start, and watch each step in the panel beside Order Desk. |
| `2 Step through the Order Desk tests.cmd` | The same, a step at a time: press Enter for each step. |
| `3 Watch tests fail.cmd` | The failure examples, holding at each checkpoint so you can read what failed. |
| `4 Find the mistakes in a suite.cmd` | Runs `autotest validate` on a suite full of mistakes, and lists every one. |
| `5 Do the Order Desk tests yourself.cmd` | The Order Desk tests at Manual speed: the panel outlines what to click and says what to type, and each step waits until you have done it. Starting the app, the database queries and the checks are still done for you. |
| `6 Run the warehouse tests.cmd` | The warehouse tests. They need RabbitMQ running on the computer, with its `guest` user; the launcher offers to install it with winget, as `autotest setup` does. |
| `7 Watch the showcase.cmd` | Opens the launcher on the showcase. Press Start, and the panel says what each part is about before it starts. Space holds a checkpoint on screen for as long as you need to talk it through. |
| `8 Do the showcase yourself.cmd` | The showcase at Manual speed: you do the clicks and typing the panel asks for, and it does the rest. |

The Demos workflow runs on every push to main and every pull request, and keeps the bundle for 90 days. To make a
fresh one, open **Actions > Demos > Run workflow** on GitHub and pick the branch. Untick the check to only build the
bundle, which takes a few minutes; ticked, it also runs every demo and checks each ends as it should.

While a test runs, **Space** pauses and carries on, **Enter** goes a step at a time, and **Esc** stops. The suites
sign in as `clerk`, password `clerk-demo`; `manager`, password `manager-demo`, is there too.

From a clone, build Order Desk with `dotnet build samples/OrderDesk`, and AutoTestRunner as the main README says,
then run `autotest launch samples/order-desk-tests`. For the warehouse tests, `autotest setup
samples/order-desk-warehouse-tests` installs RabbitMQ. `tools/Run-Demos.ps1` runs them all unattended and checks
each ends as it should, as CI does.

## The suites

| Folder | What it is |
| --- | --- |
| [`OrderDesk`](OrderDesk) | Order Desk: a sign-in window; an order form with a drop-down, tick box, radio buttons and a date; a list of orders whose Ship and Delete buttons only show on hover and whose rows have a menu of their own; a window per order; a customers grid; a catalogue with a category tree and 437 products, of which only the rows in view are built; paged activity history; Windows message boxes and file and folder pickers; a slow export with progress, backups and a nightly export scheduled in Task Scheduler; and the warehouse link over RabbitMQ. Its [release notes](OrderDesk/CHANGES.md) are what Help > About links to. |
| [`order-desk-tests`](order-desk-tests) | Twenty cases that pass, numbered OD-*area*-*n* as a test team's would be (below). |
| [`order-desk-warehouse-tests`](order-desk-warehouse-tests) | Five cases that pass against a RabbitMQ on the computer: the test plays the warehouse, reading what Order Desk sends and sending it picks. |
| [`order-desk-failure-examples`](order-desk-failure-examples) | Ten cases that fail on purpose, each in a different way (below). |
| [`order-desk-showcase`](order-desk-showcase) | One case to show people: a day at the shop, told in eight parts (below). |
| [`script-mistakes`](script-mistakes) | A suite with a mistake in nearly every step, for `autotest validate` to find before anything runs. |
| [`demo`](demo) | The smallest suite, used by the runner's own tests. It has no app. |

## The Order Desk tests

Most cases carry on from the one before, as the orders build up; a few start after the sign-in case's reset, an empty
order form, so they don't need the orders before them.

| Cases | What they test | What they use |
| --- | --- | --- |
| 1–2. Sign in | A wrong password is refused; the right one opens the main window and is remembered | A password typed as a secret, the registry, the activity log, a query on the Staff table |
| 3–9. Orders | Bad quantities refused, a standard order, the day's orders from a data sheet, express and next-day prices, changing an order in its own window, duplicating one from its menu, shipping, keeping and deleting | Radio buttons, a date, hover-only buttons, a row's menu, the clipboard, a message box, a checkpoint switched off for a known bug, and the database checked for each order |
| 10. Customers | A customer added straight to the database shows in the grid after F5 | A `query` that inserts a row, and a grid cell |
| 11. Catalogue | Find products by category, drag one onto the order form, and scroll to a spare part far down the list | A tree, a drag, a list that only builds the rows in view |
| 12–14. Export and backup | Export to a chosen folder, check it adds up with a script, back up, lose the orders and restore them | A folder picker, a file picker, a slow step with progress, a PowerShell script, a zip |
| 15–16. Import and activity | Import orders, skipping drafts, then page back through the history | A window per imported order, a data file written by the test, Older and Newer buttons |
| 17. Nightly export | Schedule and stop the nightly export | A scheduled task |
| 18. About | The version shown is the exe's | The exe's file version |
| 19. Settings | The fees follow `settings.json` | A JSON file changed by the test and put back |
| 20. Leave nothing behind | Everything the suite changed is put back | `nothingLeft` |

## The warehouse tests

Order Desk sends each shipped order to the `warehouse.orders` queue and takes picks from `orderdesk.picks`. The suite
points Order Desk at the computer's own RabbitMQ with `setAppSetting`, empties both queues, then checks what Order
Desk sends with `queue` checks and `receive`, sends it picks with `send`, and checks that something that isn't a pick,
or a pick for an order it doesn't have, is ignored and logged. The last case points Order Desk back at the shop's
broker.

## The showcase

One case, made to be watched by people who don't write tests: before each part, the panel says in a sentence what is
about to happen, and each part ends at a checkpoint whose checks are labelled in plain words. It starts Order Desk
itself and puts the computer back as it was at the end, so it can be run again straight away.

| Part | What happens | What it shows off |
| --- | --- | --- |
| 1. Sign in | A wrong password is turned away, then the right one gets in | A secret typed and kept out of the log, a message waited for, the registry, the database, and the shop's name read out of Order Desk's own `.config` with a `${shopName(readFile(appConfig))}` helper |
| 2. A phone order | Every kind of box on the order form is filled in | A text box, a drop-down, a tick box, radio buttons, and the order checked in the database and the activity log |
| 3. The lunchtime rush | Three orders from a sheet, one after another | `repeat` over a data file, and a macro with an `if` in it |
| 4. Changes of mind | An order changed in its own window; another deleted, after a Yes to "are you sure?" | A double-click, a second window, part of a note replaced, a button that only shows on hover, a message box |
| 5. The catalogue | A tea dragged onto the order form; a spare part found among 420 | A tree, a drag, a list that only builds the rows on screen |
| 6. Behind the scenes | A customer added straight into the database shows up after F5 | A `query`, a grid's cell, and a sum worked out by the database |
| 7. The warehouse | Shipping an order sends a message to RabbitMQ; the test reads it, and answers as the warehouse | `queue` checks, `receive`, `send`, and the queues' names read from Order Desk's `.config` |
| 8. End of the day | Export the orders to a folder, check the file with a script, and clean up | A folder picker, a slow step, a PowerShell script, `nothingLeft` |

Part 7 needs RabbitMQ running on the computer, as the warehouse tests do; the launcher offers to install it. Without
it, the showcase says so and just ships the order, so it still runs anywhere.

## The failure examples

Order Desk has one bug on purpose: it leaves express delivery out of the total it saves. The first case catches it.
The others fail because the test and the app disagree, as they do when an app changes and its tests don't.

| Case | What goes wrong | What the run shows |
| --- | --- | --- |
| 1. Express order is saved with its full total | The saved total in the database is wrong | Saved total: expected `29.00`, actual `24.00` |
| 2. Keep an order when Delete is cancelled | The message box asks something else | Expected `contains "Delete 1 order?"`, actual the message's words. The case carries on. |
| 3. Shipped orders are in the export | The script ends with exit code 2, and `findstr` finds nothing | Expected `exit 0`, actual `exit 2`, and what each command printed |
| 4. Change the delivery note | `replaceText` finds nothing to replace | Expected `matches back door`, actual `no match` |
| 5. Export orders | Export orders is clicked before the box that enables it is ticked | Stuck. Watched, tick the box yourself and choose **Try again**. |
| 6. Add an order | The test clicks Submit order, which is now Add order | Stuck, with a screenshot and a UI Automation dump that lists Add order |
| 7. Connect to the warehouse | The warehouse never answers | Stuck, with what the bar at the bottom said when the wait ran out |
| 8. Import orders from a file | The test waits for an import that now asks about each order | Stuck at once, quoting the window, rather than after a minute |
| 9. Next-day delivery is saved with the order | The test expects the code `NextDay`, and Order Desk saves `Next day` | Delivery: expected `NextDay`, actual `Next day` |
| 10. Read what an order was charged | The query asks for a `Price` column, and the table calls it `Total` | The query on orders failed, with SQLite's `no such column: Price` |

Cases 1 to 4, 9 and 10 record the failure and carry on to their checkpoint. Cases 5 to 8 get stuck: watched, the run asks
whether to try again, skip the case or stop; unattended, as in CI, the run stops there with a screenshot and a UI
Automation dump in the results folder. So CI runs each failure example on its own.
