# Builds a case in Order Desk as a person would, with clicks and keys sent to the screen, then checks that the case
# saved has the steps a person would write and that it passes when run. Then it opens case 1 to change it: runs it to
# a step, rolls back to an earlier one, and edits from a later one, which takes out the step after it and marks case 2,
# which carries on from it; and checks both still pass. CI runs it against the demo bundle, after the demos; it builds
# in a copy of the feature tour beside it, so the bundle is left as it was.
#
#   tools/Try-Builder.ps1 -Bundle <folder with autotestrunner\, OrderDesk\ and the suites> -Results <folder>
param(
    [Parameter(Mandatory)] [string] $Bundle,
    [Parameter(Mandatory)] [string] $Results
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class Pointer
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int x, int y, uint data, System.IntPtr extra);
}
'@
$autotest = Join-Path $Bundle 'autotestrunner\autotest.exe'
$suite = Join-Path $Bundle 'builder-try'
New-Item -ItemType Directory -Force -Path $Results | Out-Null
if (Test-Path -LiteralPath $suite) { Remove-Item -LiteralPath $suite -Recurse -Force }
Copy-Item (Join-Path $Bundle 'order-desk-feature-tour') $suite -Recurse
# Order numbers start again at 1001, as they do when the case runs.
& (Join-Path $suite 'tools\Reset-OrderDesk.ps1')

$ui = [System.Windows.Automation.AutomationElement]
$root = $ui::RootElement
function Wait-For([scriptblock] $find, [string] $what, [int] $seconds = 60) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        $found = & $find
        if ($found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    throw "Waited $seconds seconds for $what."
}
function Find($parent, $property, $value) {
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($property, $value)))
}
function Window([string] $name) {
    $root.FindFirst([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, $name)))
}
# Moves the mouse onto a control, rests there as a person does, and clicks it.
function Click($element) {
    $box = $element.Current.BoundingRectangle
    [Pointer]::SetCursorPos([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 500
    [Pointer]::mouse_event(0x2, 0, 0, 0, [System.IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [Pointer]::mouse_event(0x4, 0, 0, 0, [System.IntPtr]::Zero)
    Start-Sleep -Milliseconds 800
}
function Keys([string] $keys) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 300
}
function Invoke($button) {
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Named($parent, [string] $like) {
    $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like $like } | Select-Object -First 1
}
function Saying($parent, [string] $like, [string] $what, [int] $seconds = 60) {
    Wait-For { Named $parent $like } $what $seconds | Out-Null
}
# The launcher is titled for the suite it shows: it is the window of autotest's that isn't the panel.
function Close-Launcher($process) {
    $launcher = Wait-For { $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition($ui::ProcessIdProperty, $process.Id))) |
        Where-Object { $_.Current.Name -notlike 'New case:*' -and $_.Current.Name -notlike 'Edit *' } | Select-Object -First 1 } 'the launcher' 30
    $launcher.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}
function Save-Screen([string] $name) {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $picture = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    [System.Drawing.Graphics]::FromImage($picture).CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
    $picture.Save((Join-Path $Results $name))
}
# A step's buttons show while the mouse is over its row: scrolls the row into the list, rests the mouse on it, and
# clicks the button whose tip starts so.
function Step-Button($panel, [string] $step, [string] $button) {
    $row = Wait-For { Named $panel $step } "the step $step" 10
    $list = $panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($ui::IsScrollPatternAvailableProperty, $true)))
    for ($turn = 0; $list -and $turn -lt 40; $turn++) {
        $box = $row.Current.BoundingRectangle; $view = $list.Current.BoundingRectangle
        $scroll = $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($box.Bottom -gt $view.Bottom) { $scroll.ScrollVertical([System.Windows.Automation.ScrollAmount]::SmallIncrement) }
        elseif ($box.Top -lt $view.Top) { $scroll.ScrollVertical([System.Windows.Automation.ScrollAmount]::SmallDecrement) }
        else { break }
        Start-Sleep -Milliseconds 100
    }
    $box = $row.Current.BoundingRectangle
    [Pointer]::SetCursorPos([int]($box.X + 20), [int]($box.Y + $box.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 600
    # Each row has the buttons; those of the row under the mouse are the ones showing, level with its words.
    Click (Wait-For { $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like "$button*" -and -not $_.Current.IsOffscreen -and
            $_.Current.BoundingRectangle.Top -ge $box.Top - 12 -and $_.Current.BoundingRectangle.Top -le $box.Bottom } |
        Select-Object -First 1 } "the $button button of $step" 10)
}

$name = 'Take an order for Grace'
$builder = Start-Process $autotest -ArgumentList @('record', "`"$suite`"", '--name', "`"$name`"") -PassThru `
    -RedirectStandardOutput (Join-Path $Results 'builder-output.txt') -RedirectStandardError (Join-Path $Results 'builder-errors.txt')
$problem = $null
try {
    $panel = Wait-For { Window "New case: $name" } 'the builder panel'
    Wait-For { $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like 'Recording. Use Order Desk*' } } 'the builder to start Order Desk and record' 90 | Out-Null
    $app = Wait-For { Window 'Order Desk' } 'Order Desk'

    Click (Find $app $ui::AutomationIdProperty 'CustomerBox')
    Keys 'Grace Hopper'
    $product = Find $app $ui::AutomationIdProperty 'ProductBox'
    Click $product
    Click (Wait-For { Find $product $ui::NameProperty 'Coffee beans' } 'Coffee beans in the drop-down' 10)
    Click (Find $app $ui::AutomationIdProperty 'QuantityBox')
    Keys '^a'
    Keys '3'
    Click (Find $app $ui::AutomationIdProperty 'GiftWrapBox')
    Click (Find $app $ui::AutomationIdProperty 'AddButton')
    # The bar at the bottom says the order was added; the builder waits for it to keep still before a wait for it.
    Start-Sleep -Seconds 3

    Invoke (Find $panel $ui::NameProperty 'Save')
    Wait-For { $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like 'Saved in*' } } 'the case to be saved' 20 | Out-Null
}
catch {
    $problem = "$_"
    Save-Screen 'builder-stuck.png'
}
finally {
    # Back to the cases closes the builder and Order Desk, and opens the launcher, which is closed too.
    if (-not $problem) {
        Invoke (Find $panel $ui::NameProperty 'Back to the cases')
        Close-Launcher $builder
    }
    if (-not $builder.WaitForExit(30000)) { $builder.Kill() }
    Get-ChildItem (Join-Path $suite 'results') -Recurse -Filter run.log -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName (Join-Path $Results 'builder-run.log') }
}
if ($problem) { throw $problem }

# The case reads as a person would write it, finding each control by its name in elements.json.
$case = Get-ChildItem (Join-Path $suite 'cases') -Filter '09-*.json' | Select-Object -First 1
if (-not $case) { throw 'The builder saved no case 9.' }
Copy-Item $case.FullName (Join-Path $Results $case.Name)
$text = Get-Content -Raw -LiteralPath $case.FullName
$text
$expected = @(
    '"startApp": {}', '"type": "customer", "text": "Grace Hopper"', '"choose": "product", "option": "Coffee beans"',
    '"type": "quantity", "text": "3"', '"tick": "giftWrap"', '"click": "addOrder"',
    '"call": "waitForStatusBar", "with": { "startsWith": "Added order 1001 for Grace Hopper" }'
)
$missing = @($expected | Where-Object { -not $text.Contains($_) })
if ($missing) { throw "The case the builder saved doesn't have:`n$($missing -join "`n")" }

# And it passes, run as any other case.
& $autotest validate $suite
if ($LASTEXITCODE -ne 0) { throw "validate found mistakes after the builder saved (exit code $LASTEXITCODE)." }
& $autotest run $suite --cases 9 --unattended --results (Join-Path $Results 'built-case')
if ($LASTEXITCODE -ne 0) { throw "The built case ended with exit code $LASTEXITCODE, not 0." }
'The builder recorded case 9, and it passes.'

# ===== Changing case 1 =====
$first = Get-ChildItem (Join-Path $suite 'cases') -Filter '01-*.json' | Select-Object -First 1
$second = Get-ChildItem (Join-Path $suite 'cases') -Filter '02-*.json' | Select-Object -First 1
$before = Get-Content -Raw -LiteralPath $first.FullName
$editor = Start-Process $autotest -ArgumentList @('record', "`"$suite`"", '--edit', 'take-an-order') -PassThru `
    -RedirectStandardOutput (Join-Path $Results 'edit-output.txt') -RedirectStandardError (Join-Path $Results 'edit-errors.txt')
$problem = $null
try {
    $panel = Wait-For { Window 'Edit 1. Take an order' } 'the builder panel, on case 1'
    Saying $panel 'Order Desk is at the start of the case*' 'the case opened at its start' 90

    Step-Button $panel 'Pick Coffee beans from the drop-down' 'Run to here'
    Saying $panel 'Order Desk is at step 5.*' 'case 1 played to step 5' 90
    Step-Button $panel 'Type Ada Lovelace as the customer*' 'Roll back to here'
    Saying $panel 'Order Desk is at step 3.*' 'Order Desk started again, and played to step 3' 120
    Save-Screen 'edit-rolled-back.png'

    # Edit from the wait for the bar at the bottom: the step after it, pointing at the new order, goes.
    Step-Button $panel 'Wait for the bar at the bottom to say*' 'Edit from here'
    # A window the panel owns is under it, as UI Automation sees it.
    $confirm = Wait-For { $panel.FindFirst([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, 'Edit from step 11'))) } 'the window that asks before steps are taken out' 20
    if (-not (Named $confirm '2. Take three more orders with a macro')) { throw 'Case 2, which carries on from case 1, isn''t offered to mark.' }
    Save-Screen 'edit-confirm.png'
    Invoke (Find $confirm $ui::NameProperty 'Confirm')
    Saying $panel 'Recording. Use Order Desk*' 'case 1 played to step 11, and recording' 120

    Invoke (Find $panel $ui::NameProperty 'Save')
    Saying $panel 'Saved in*' 'case 1 to be saved' 20
}
catch {
    $problem = "$_"
    Save-Screen 'edit-stuck.png'
}
finally {
    if (-not $problem) {
        Invoke (Find $panel $ui::NameProperty 'Back to the cases')
        Close-Launcher $editor
    }
    if (-not $editor.WaitForExit(30000)) { $editor.Kill() }
}
if ($problem) { throw $problem }

$after = Get-Content -Raw -LiteralPath $first.FullName
Copy-Item $first.FullName (Join-Path $Results "edited-$($first.Name)")
$after
# The steps up to the one edited from are as they were written, and the one after it has gone.
$kept = $before.Substring(0, $before.IndexOf('{ "hover"'))
if (-not $after.StartsWith($kept.TrimEnd())) { throw 'Case 1''s steps before the edit aren''t as they were written.' }
if ($after.Contains('"hover"')) { throw 'The step after the one edited from is still in case 1.' }
if (-not $after.Contains('"checkpoint": "Ada''s order was added and saved"')) { throw 'Case 1 lost its checkpoint.' }
if (-not (Get-Content -Raw -LiteralPath $second.FullName).Contains('"review": "Case 1, Take an order, was changed from step 11')) {
    throw 'Case 2 isn''t marked as needing a look.'
}
& $autotest validate $suite
if ($LASTEXITCODE -ne 0) { throw "validate found mistakes after case 1 was changed (exit code $LASTEXITCODE)." }
& $autotest run $suite --cases 1-2 --unattended --results (Join-Path $Results 'edited-cases')
if ($LASTEXITCODE -ne 0) { throw "Cases 1 and 2 ended with exit code $LASTEXITCODE after case 1 was changed, not 0." }
'The builder changed case 1 from step 11, marked case 2, and both pass.'
