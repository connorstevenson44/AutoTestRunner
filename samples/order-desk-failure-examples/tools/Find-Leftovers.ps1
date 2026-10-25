# Prints one line for each thing an earlier run of the Order Desk demos left behind: its data folder, with the orders
# in it. AutoTestRunner runs this before a run, and cleans up with Reset-OrderDesk.ps1 when it prints anything.
$data = Join-Path $env:LOCALAPPDATA 'OrderDesk'
if (Test-Path -LiteralPath $data) {
    "Order Desk's data folder, $data"
}
