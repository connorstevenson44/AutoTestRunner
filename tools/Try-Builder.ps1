# Builds a case in Order Desk as a person would, with clicks and keys sent to the screen, then checks that the case
# saved has the steps a person would write and that it passes when run. CI runs it against the demo bundle, after the
# demos; it builds in a copy of the feature tour beside it, so the bundle is left as it was.
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

$name = 'Take an order for Grace'
$builder = Start-Process $autotest -ArgumentList @('record', "`"$suite`"", '--name', "`"$name`"") -PassThru `
    -RedirectStandardOutput (Join-Path $Results 'builder-output.txt') -RedirectStandardError (Join-Path $Results 'builder-errors.txt')
$problem = $null
try {
    $panel = Wait-For { Window "New case: $name" } 'the builder panel'
    Wait-For { $panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $_.Current.Name -like 'Use Order Desk*' } } 'the builder to start Order Desk and record' 90 | Out-Null
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
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $picture = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    [System.Drawing.Graphics]::FromImage($picture).CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
    $picture.Save((Join-Path $Results 'builder-stuck.png'))
}
finally {
    # Back to the cases closes the builder and Order Desk, and opens the launcher, which is closed too.
    if (-not $problem) {
        Invoke (Find $panel $ui::NameProperty 'Back to the cases')
        $launcher = Wait-For { Window 'AutoTestRunner' } 'the launcher' 30
        $launcher.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
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
