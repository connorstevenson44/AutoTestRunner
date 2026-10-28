# Prints one line for each thing an earlier run of the showcase left behind: Order Desk's data and exports, what it
# remembers in the registry, and Order Desk still pointed at this computer's RabbitMQ. AutoTestRunner cleans up with
# Reset-Showcase.ps1 when it prints anything.
foreach ($folder in (Join-Path $env:LOCALAPPDATA 'OrderDesk'), (Join-Path $env:LOCALAPPDATA 'OrderDesk exports')) {
    if (Test-Path -LiteralPath $folder) {
        "The folder $folder"
    }
}
if (Test-Path -LiteralPath 'HKCU:\Software\OrderDesk') {
    'What Order Desk remembers, in HKCU\Software\OrderDesk'
}
foreach ($folder in '..\..\OrderDesk', '..\..\OrderDesk\bin\Release\net10.0-windows', '..\..\OrderDesk\bin\Debug\net10.0-windows') {
    $file = Join-Path $PSScriptRoot "$folder\OrderDesk.dll.config"
    if ((Test-Path -LiteralPath $file) -and (Select-String -LiteralPath $file -SimpleMatch 'amqp://guest:guest@localhost' -Quiet)) {
        "Order Desk pointed at this computer's broker, in $file"
    }
}
exit 0
