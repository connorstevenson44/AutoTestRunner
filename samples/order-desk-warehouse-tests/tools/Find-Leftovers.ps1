# Prints one line for each thing an earlier run of the warehouse tests left behind: Order Desk's data folder, and
# Order Desk still pointed at this computer's broker. AutoTestRunner cleans up with Reset-Warehouse.ps1 when it prints
# anything.
$data = Join-Path $env:LOCALAPPDATA 'OrderDesk'
if (Test-Path -LiteralPath $data) {
    "Order Desk's data folder, $data"
}
foreach ($folder in '..\..\OrderDesk', '..\..\OrderDesk\bin\Release\net10.0-windows', '..\..\OrderDesk\bin\Debug\net10.0-windows') {
    $file = Join-Path $PSScriptRoot "$folder\OrderDesk.dll.config"
    if ((Test-Path -LiteralPath $file) -and (Select-String -LiteralPath $file -SimpleMatch 'amqp://guest:guest@localhost' -Quiet)) {
        "Order Desk pointed at this computer's broker, in $file"
    }
}
