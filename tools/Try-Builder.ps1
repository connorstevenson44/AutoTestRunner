# Builds a case in Order Desk as a person would, with clicks and keys sent to the screen, pausing and starting with F9
# and adding a wait with a pick tool, then checks that the case saved has the steps a person would write, file picker
# and all, and that it passes when run. Each new case starts where the sign-in case's reset leaves Order Desk, signed
# in, as the suite's cases do. Then it opens case 4, Take a standard order, to change it: runs it to a step, rolls back
# to an earlier one, and edits from a later one, which takes out the step after it and marks case 5, which carries on
# from it; and checks both still pass. CI runs it against the demo bundle, after the demos; it builds
# in a copy of the Order Desk tests beside it, so the bundle is left as it was.
#
#   tools/Try-Builder.ps1 -Bundle <folder with autotestrunner\, OrderDesk\ and the suites> -Results <folder>
#
# Then it adds a C# script step from the Script menu, runs to it, and clicks its words and its details, as a person does
# before changing it; and makes a new suite from the launcher, starting Order Desk to read its first window's title,
# signing in, and pointing at the main window's status bar. autotest must still be open after each.
#
#   tools/Try-Builder.ps1 ... -Parts script, new-suite    only some of them: record, edit, script and new-suite
param(
    [Parameter(Mandatory)] [string] $Bundle,
    [Parameter(Mandatory)] [string] $Results,
    [string[]] $Parts = @('record', 'edit', 'script', 'new-suite')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @'
using System.Runtime.InteropServices;
public static class Pointer
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hwnd);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, System.IntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int x, int y, uint data, System.IntPtr extra);
}
'@
$autotest = Join-Path $Bundle 'autotestrunner\autotest.exe'
$suite = Join-Path $Bundle 'builder-try'
New-Item -ItemType Directory -Force -Path $Results | Out-Null
if (Test-Path -LiteralPath $suite) { Remove-Item -LiteralPath $suite -Recurse -Force }
Copy-Item (Join-Path $Bundle 'order-desk-tests') $suite -Recurse
# Order numbers start again at 1001, as they do when the case runs.
& (Join-Path $suite 'tools\Reset-OrderDesk.ps1')
# A file with no orders in it, to import through Windows' file picker.
$noOrders = Join-Path (Resolve-Path $Results).Path 'no-orders.csv'
Set-Content -LiteralPath $noOrders 'customer,product,quantity,note'

$ui = [System.Windows.Automation.AutomationElement]
$root = $ui::RootElement
function Wait-For([scriptblock] $find, [string] $what, [int] $seconds = 60) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        # A window closing while its controls are listed, such as the file picker, takes them away mid-list: look again.
        try { $found = & $find }
        catch {
            if ($_.Exception.GetBaseException() -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw }
            $found = $null
        }
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
    # The list of steps, not a box that scrolls, such as a new case's name.
    $list = $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($ui::IsScrollPatternAvailableProperty, $true))) |
        Where-Object { $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Edit } | Select-Object -First 1
    if ($list -and -not $list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticallyScrollable) { $list = $null }
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

# autotest ended by itself: what it wrote about why, in errors.log and Windows' Application log.
function Assert-Open($process, [string] $while) {
    Start-Sleep -Milliseconds 800
    if (-not $process.HasExited) { return }
    $log = Join-Path $env:LOCALAPPDATA 'AutoTestRunner\logs\errors.log'
    if (Test-Path -LiteralPath $log) { Get-Content -Raw -LiteralPath $log | Write-Host }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime', 'Application Error'; StartTime = (Get-Date).AddMinutes(-10) } -ErrorAction SilentlyContinue |
        Select-Object -First 4 | ForEach-Object { Write-Host $_.Message }
    throw "autotest closed by itself (exit code $($process.ExitCode)) $while."
}

if ($Parts -contains 'record') {
    $name = 'Take an order for Grace'
    $builder = Start-Process $autotest -ArgumentList @('record', "`"$suite`"", '--name', "`"$name`"", '--after-reset', 'sign-in') -PassThru `
        -RedirectStandardOutput (Join-Path $Results 'builder-output.txt') -RedirectStandardError (Join-Path $Results 'builder-errors.txt')
    $problem = $null
    try {
        $panel = Wait-For { Window "New case: $name" } 'the builder panel'
        Wait-For { $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.Name -like 'Recording. Use Order Desk*' } } 'the builder to start Order Desk, sign in and record' 120 | Out-Null
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

        # Windows' file picker. Clicks in it go straight to it, and only Open is held back while the builder reads what
        # the picker would open. The builder passes Open on without taking the mouse, which the person has moved on.
        Click (Find $app $ui::AutomationIdProperty 'ImportButton')
        $picker = Wait-For { $app.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, 'Import orders'))) } 'the Import orders picker' 20
        # The name box is control 1148. Tab after typing closes the list of matching files, so it doesn't cover Open.
        Click (Wait-For { Find $picker $ui::AutomationIdProperty '1148' } 'the picker''s name box' 10)
        Keys '^a'
        Keys ($noOrders -replace '[+^%~(){}\[\]]', '{$0}')
        Keys '{TAB}'
        $open = @($picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($ui::AutomationIdProperty, '1'))) |
            Sort-Object { $_.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button })[0].Current.BoundingRectangle
        [Pointer]::SetCursorPos([int]($open.X + $open.Width / 2), [int]($open.Y + $open.Height / 2)) | Out-Null
        Start-Sleep -Milliseconds 500
        [Pointer]::mouse_event(0x2, 0, 0, 0, [System.IntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [Pointer]::mouse_event(0x4, 0, 0, 0, [System.IntPtr]::Zero)
        $away = New-Object System.Drawing.Point([int]($open.X - 150), [int]($open.Y - 120))
        [Pointer]::SetCursorPos($away.X, $away.Y) | Out-Null
        Saying $app 'Import finished*' 'the picker to close, and Order Desk to import nothing' 20
        Start-Sleep -Milliseconds 500
        $mouse = [System.Windows.Forms.Cursor]::Position
        if ($mouse -ne $away) { throw "The mouse is at $mouse after the builder passed Open on, not at $away where it was put." }
        Start-Sleep -Seconds 2

        # The record key pauses recording from the app, and starts it again.
        Keys '{F9}'
        Saying $panel 'Paused.*' 'F9 to pause recording' 10
        Keys '{F9}'
        Saying $panel 'Recording.*' 'F9 to start recording again' 10

        # A pick tool first, then the control: Wait for it › Wait for its text, then the bar at the bottom. The click
        # picks the bar without reaching Order Desk, and recording carries on after the wait is added.
        $count = (Named $panel '* steps').Current.Name
        Click (Find $panel $ui::NameProperty 'Wait for it')
        $item = Wait-For { Find $root $ui::NameProperty 'Wait for its text' } 'the Wait for its text tool in its menu' 10
        Save-Screen 'builder-pick-menu.png'
        Click $item
        Saying $panel 'Click a control in Order Desk to wait for its text*' 'the Wait for its text tool to wait for a control' 10
        $bar = (Find $app $ui::AutomationIdProperty 'StatusText').Current.BoundingRectangle
        [Pointer]::SetCursorPos([int]($bar.X + 20), [int]($bar.Y + $bar.Height / 2)) | Out-Null
        Start-Sleep -Milliseconds 1200
        Save-Screen 'builder-pick-outline.png'
        [Pointer]::mouse_event(0x2, 0, 0, 0, [System.IntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [Pointer]::mouse_event(0x4, 0, 0, 0, [System.IntPtr]::Zero)
        Wait-For { (Named $panel '* steps').Current.Name -ne $count } 'the wait to be added to the steps' 20 | Out-Null
        Saying $panel 'Recording.*' 'recording to carry on after the wait' 20
        Save-Screen 'builder-picked.png'

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
    $case = Get-ChildItem (Join-Path $suite 'cases') -Filter '21-*.json' | Select-Object -First 1
    if (-not $case) { throw 'The builder saved no case 21.' }
    Copy-Item $case.FullName (Join-Path $Results $case.Name)
    $text = Get-Content -Raw -LiteralPath $case.FullName
    $text
    $expected = @(
        '"afterReset": "sign-in"', '"type": "customer", "text": "Grace Hopper"', '"choose": "product", "option": "Coffee beans"',
        '"type": "quantity", "text": "3"', '"tick": "giftWrap"', '"click": "addOrder"',
        '"call": "waitForStatusBar", "with": { "startsWith": "Added order 1001 for Grace Hopper" }',
        '"click": "importOrders"', '"pickFile": "Import orders"', '"waitText": "statusText"'
    )
    $missing = @($expected | Where-Object { -not $text.Contains($_) })
    if ($missing) { throw "The case the builder saved doesn't have:`n$($missing -join "`n")" }
    # Signed in already, so it doesn't start Order Desk itself.
    if ($text.Contains('"startApp"')) { throw "The case the builder saved starts Order Desk, though it starts after the sign-in case's reset:`n$text" }

    # And it passes, run as any other case.
    & $autotest validate $suite
    if ($LASTEXITCODE -ne 0) { throw "validate found mistakes after the builder saved (exit code $LASTEXITCODE)." }
    & $autotest run $suite --cases 21 --unattended --results (Join-Path $Results 'built-case')
    if ($LASTEXITCODE -ne 0) { throw "The built case ended with exit code $LASTEXITCODE, not 0." }
    'The builder recorded case 21, after the sign-in case''s reset, and it passes.'
}

# ===== Changing case 4 =====
if ($Parts -contains 'edit') {
    $first = Get-ChildItem (Join-Path $suite 'cases') -Filter '04-*.json' | Select-Object -First 1
    $second = Get-ChildItem (Join-Path $suite 'cases') -Filter '05-*.json' | Select-Object -First 1
    $before = Get-Content -Raw -LiteralPath $first.FullName
    $editor = Start-Process $autotest -ArgumentList @('record', "`"$suite`"", '--edit', 'take-an-order') -PassThru `
        -RedirectStandardOutput (Join-Path $Results 'edit-output.txt') -RedirectStandardError (Join-Path $Results 'edit-errors.txt')
    $problem = $null
    try {
        $panel = Wait-For { Window 'Edit 4. OD-ORD-01 Take a standard order' } 'the builder panel, on case 4'
        # Order Desk is started and signed in first, as the case starts after the sign-in case's reset.
        Saying $panel 'Order Desk is at the start of the case*' 'the case opened at its start' 120

        Step-Button $panel 'Pick Coffee beans from product' 'Run to here'
        Saying $panel 'Order Desk is at step 5.*' 'case 4 played to step 5' 90
        Step-Button $panel 'Type Ada Lovelace in customer*' 'Roll back to here'
        Saying $panel 'Order Desk is at step 3.*' 'Order Desk started again, signed in, and played to step 3' 150
        Save-Screen 'edit-rolled-back.png'

        # Edit from the wait for the bar at the bottom: the step after it, pointing at the new order, goes.
        Step-Button $panel 'Wait for the bar at the bottom to say*' 'Edit from here'
        # A window the panel owns is under it, as UI Automation sees it.
        $confirm = Wait-For { $panel.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, 'Edit from step 13'))) } 'the window that asks before steps are taken out' 20
        if (-not (Named $confirm '5. OD-ORD-03 Take the day''s orders from a sheet')) { throw 'Case 5, which carries on from case 4, isn''t offered to mark.' }
        Save-Screen 'edit-confirm.png'
        Invoke (Find $confirm $ui::NameProperty 'Confirm')
        Saying $panel 'Recording. Use Order Desk*' 'case 4 played to step 13, and recording' 120

        Invoke (Find $panel $ui::NameProperty 'Save')
        Saying $panel 'Saved in*' 'case 4 to be saved' 20
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
    if (-not $after.StartsWith($kept.TrimEnd())) { throw 'Case 4''s steps before the edit aren''t as they were written.' }
    if ($after.Contains('"hover"')) { throw 'The step after the one edited from is still in case 4.' }
    if (-not $after.Contains('"checkpoint": "Order 1001 is added and saved"')) { throw 'Case 4 lost its checkpoint.' }
    if (-not (Get-Content -Raw -LiteralPath $second.FullName).Contains('"review": "Case 4, OD-ORD-01 Take a standard order, was changed from step 13')) {
        throw 'Case 5 isn''t marked as needing a look.'
    }
    & $autotest validate $suite
    if ($LASTEXITCODE -ne 0) { throw "validate found mistakes after case 4 was changed (exit code $LASTEXITCODE)." }
    & $autotest run $suite --cases 4-5 --unattended --results (Join-Path $Results 'edited-cases')
    if ($LASTEXITCODE -ne 0) { throw "Cases 4 and 5 ended with exit code $LASTEXITCODE after case 4 was changed, not 0." }
    'The builder changed case 4 from step 13, marked case 5, and both pass.'
}
# The first window of autotest's, such as the launcher, titled for the suite it shows.
function Window-Of($process, [string] $what) {
    Wait-For { $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition($ui::ProcessIdProperty, $process.Id))) | Select-Object -First 1 } $what 60
}
function Set-Text($box, [string] $text) {
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    Start-Sleep -Milliseconds 300
}
function Text-Of($box) { $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }

# ===== A C# script step =====
# Made from the Script menu, as it comes, and played with Run to here; then its words are clicked to change them, and its
# details folded out, as a person does next.
if ($Parts -contains 'script') {
    $scripter = Start-Process $autotest -ArgumentList @('record', "`"$suite`"", '--name', '"Check the orders file"', '--after-reset', 'sign-in') -PassThru `
        -RedirectStandardOutput (Join-Path $Results 'script-output.txt') -RedirectStandardError (Join-Path $Results 'script-errors.txt')
    $problem = $null
    try {
        $panel = Wait-For { Window 'New case: Check the orders file' } 'the builder panel'
        Saying $panel 'Recording. Use Order Desk*' 'the builder to start Order Desk, sign in and record' 120

        Invoke (Find $panel $ui::NameProperty 'Script')
        # The Script menu is a window of its own, as a WPF menu is.
        $csharp = Wait-For { $root.FindAll([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($ui::ProcessIdProperty, $scripter.Id))) |
            ForEach-Object { Named $_ 'New C# script*' } | Select-Object -First 1 } 'New C# script in the Script menu' 10
        Invoke $csharp
        $prompt = Wait-For { $panel.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, 'New C# script'))) } 'the window that asks for the script''s name' 20
        Set-Text (Find $prompt $ui::ControlTypeProperty ([System.Windows.Automation.ControlType]::Edit)) 'Read the orders'
        Invoke (Find $prompt $ui::NameProperty 'Create')
        # It opens in Notepad to be written; it is left as it comes.
        Start-Sleep -Seconds 2
        Get-Process notepad, notepad++ -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Assert-Open $scripter 'after the C# script was made'

        Step-Button $panel 'Read the orders' 'Run to here'
        # dotnet run builds it the first time, which takes a while.
        Wait-For { (Named $panel 'Paused.*'), (Named $panel 'Step 1,*') | Where-Object { $_ } | Select-Object -First 1 } 'the script step to run' 240 | Out-Null
        Save-Screen 'script-ran.png'
        Assert-Open $scripter 'after Run to here played the script step'

        # Its words, clicked, become a box to change them in.
        Click (Wait-For { Named $panel 'Read the orders' } 'the script step''s words' 10)
        Assert-Open $scripter 'after the script step''s words were clicked'
        Keys '^a'
        Keys 'Read the orders file{ENTER}'
        Saying $panel 'Read the orders file' 'the script step''s new words' 10
        Assert-Open $scripter 'after the script step''s words were changed'

        Step-Button $panel 'Read the orders file' 'Change this step'
        Saying $panel 'Arguments, one to a line' 'the script step''s details' 10
        Save-Screen 'script-details.png'
        Assert-Open $scripter 'after the script step''s details were folded out'
        Invoke (Wait-For { Find $panel $ui::NameProperty 'Apply' } 'Apply in the details' 10)
        Assert-Open $scripter 'after the script step''s details were applied'

        # Edit from the script step, the last one: nothing is taken out, and recording starts after it.
        Step-Button $panel 'Read the orders file' 'Edit from here'
        Saying $panel 'Recording. Use Order Desk*' 'recording to start after the script step' 60
        Assert-Open $scripter 'after Edit from here on the script step'

        Invoke (Find $panel $ui::NameProperty 'Save')
        Saying $panel 'Saved in*' 'the case with the script step to be saved' 20
    }
    catch {
        $problem = "$_"
        Save-Screen 'script-stuck.png'
    }
    finally {
        if (-not $problem) {
            Invoke (Find $panel $ui::NameProperty 'Back to the cases')
            Close-Launcher $scripter
        }
        if (-not $scripter.WaitForExit(30000)) { $scripter.Kill() }
    }
    if ($problem) { throw $problem }
    $case = Get-ChildItem (Join-Path $suite 'cases') -Filter '*check-the-orders-file*.json' | Select-Object -First 1
    if (-not $case) { throw 'The builder saved no case with the script step.' }
    Copy-Item $case.FullName (Join-Path $Results $case.Name)
    $text = Get-Content -Raw -LiteralPath $case.FullName
    if (-not ($text.Contains('"script": "tools/ReadTheOrders.cs"') -and $text.Contains('"say": "Read the orders file"'))) {
        throw "The case doesn't run tools/ReadTheOrders.cs as the step Read the orders file:`n$text"
    }
    & $autotest validate $suite
    if ($LASTEXITCODE -ne 0) { throw "validate found mistakes after the case with the script step was saved (exit code $LASTEXITCODE)." }
    'The builder made a C# script step, ran to it, and changed its words and details.'
}

# ===== A new suite =====
# From the launcher the demo bundle's first shortcut opens: the app's exe typed in, started to read its window's title,
# which is the sign-in window's; then signed in, as a person would, and the main window's status bar pointed at.
if ($Parts -contains 'new-suite') {
    $made = Join-Path $Bundle 'order-desk-again'
    if (Test-Path -LiteralPath $made) { Remove-Item -LiteralPath $made -Recurse -Force }
    $launcher = Start-Process $autotest -ArgumentList 'launch' -WorkingDirectory $Bundle -PassThru `
        -RedirectStandardOutput (Join-Path $Results 'new-suite-output.txt') -RedirectStandardError (Join-Path $Results 'new-suite-errors.txt')
    $problem = $null
    try {
        $window = Window-Of $launcher 'the launcher'
        # The small button that opens the logs folder, made if nothing has been written to it yet.
        Invoke (Wait-For { Find $window $ui::NameProperty 'Open the logs folder' } 'the logs folder button' 20)
        Wait-For { Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'AutoTestRunner\logs') } 'the logs folder to be made' 10 | Out-Null
        Assert-Open $launcher 'after the logs folder button was clicked'
        # Explorer, on the logs folder, is closed again so it covers nothing; how it titles itself varies.
        Start-Sleep -Seconds 2
        $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.Name -like 'logs*' } |
            ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        Invoke (Wait-For { Named $window 'New suite:*' } 'the New suite button' 20)
        $form = Wait-For { $window.FindFirst([System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition($ui::NameProperty, 'New suite'))) } 'the New suite window' 20
        $boxes = @($form.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($ui::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit))))
        if ($boxes.Count -ne 4) { throw "The New suite window has $($boxes.Count) boxes, not 4." }
        Set-Text $boxes[0] 'Order Desk again'
        Set-Text $boxes[1] (Join-Path $Bundle 'OrderDesk\OrderDesk.exe')
        Invoke (Find $form $ui::NameProperty 'Start the app and read its window''s title')
        Saying $form 'Read from the window*' 'Order Desk to start and its title to be read' 60
        Assert-Open $launcher 'after Order Desk was started to read its title'
        if ((Text-Of $boxes[2]) -ne 'Sign in to Order Desk') { throw "The window's title was read as '$(Text-Of $boxes[2])', not 'Sign in to Order Desk'." }

        $signIn = Wait-For { Window 'Sign in to Order Desk' } 'the sign-in window' 20
        Set-Text (Find $signIn $ui::AutomationIdProperty 'UserBox') 'clerk'
        Click (Find $signIn $ui::AutomationIdProperty 'PasswordBox')
        Keys 'clerk-demo'
        Invoke (Wait-For { $button = Find $signIn $ui::AutomationIdProperty 'SignInButton'; if ($button.Current.IsEnabled) { $button } } 'Sign in to be enabled' 10)
        $app = Wait-For { Window 'Order Desk' } 'the Order Desk window, signed in' 30
        # The main window opens where Windows puts it, and here behind the launcher, as signing in wasn't a click:
        # brought to the front, as a person would before pointing at it. Windows lets a script do that just after a
        # key press, so Alt is pressed and let go first.
        [Pointer]::keybd_event(0x12, 0, 0, [System.IntPtr]::Zero)
        [Pointer]::keybd_event(0x12, 0, 2, [System.IntPtr]::Zero)
        [Pointer]::SetForegroundWindow([System.IntPtr]$app.Current.NativeWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 500
        Invoke (Find $form $ui::NameProperty 'Point at it')
        Start-Sleep -Seconds 1
        Click (Find $app $ui::AutomationIdProperty 'StatusText')
        try { Saying $form '*found by*' 'the status bar to be picked' 20 }
        catch {
            # What the window says instead, such as why the pick didn't take, as the screenshot can't be read here.
            $said = @($form.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition($ui::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) |
                ForEach-Object { $_.Current.Name } | Where-Object { $_ })
            $under = [System.Windows.Forms.Cursor]::Position
            $bar = (Find $app $ui::AutomationIdProperty 'StatusText').Current.BoundingRectangle
            $hit = $ui::FromPoint((New-Object System.Windows.Point($under.X, $under.Y)))
            $owner = (Get-Process -Id $hit.Current.ProcessId -ErrorAction SilentlyContinue).ProcessName
            throw "$_ The New suite window says: $($said -join ' | '). The status bar is at $bar, and the mouse at $under, over '$($hit.Current.Name)' of $owner."
        }
        Assert-Open $launcher 'after the status bar was pointed at'
        Save-Screen 'new-suite.png'

        Invoke (Find $form $ui::NameProperty 'Make the suite')
        # The launcher opens again, on the new suite.
        Wait-For { Window 'Order Desk again' } 'the launcher on the new suite' 30 | Out-Null
    }
    catch {
        $problem = "$_"
        Save-Screen 'new-suite-stuck.png'
    }
    finally {
        if (-not $problem) { Close-Launcher $launcher }
        if (-not $launcher.WaitForExit(30000)) { $launcher.Kill() }
    }
    if ($problem) { throw $problem }
    $text = Get-Content -Raw -LiteralPath (Join-Path $made 'suite.json')
    Copy-Item (Join-Path $made 'suite.json') (Join-Path $Results 'new-suite.json')
    if (-not $text.Contains('"window": "Sign in to Order Desk"')) { throw "The new suite's suite.json doesn't wait for the sign-in window:`n$text" }
    & $autotest validate $made
    if ($LASTEXITCODE -ne 0) { throw "validate found mistakes in the new suite (exit code $LASTEXITCODE)." }
    Remove-Item -LiteralPath $made -Recurse -Force
    'The launcher made a new suite for Order Desk, reading its first window''s title and its status bar from the app.'
}
