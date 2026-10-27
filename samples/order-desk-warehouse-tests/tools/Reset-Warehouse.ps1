# Puts the computer back as it was before the warehouse tests ran: closes Order Desk, points it back at the shop's
# broker in OrderDesk.dll.config, and deletes its data. The queues are emptied by the tests themselves.
Get-Process -Name OrderDesk -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
$shop = 'amqp://orderdesk:orderdesk@warehouse.contoso.local:5672/'
foreach ($folder in '..\..\OrderDesk', '..\..\OrderDesk\bin\Release\net10.0-windows', '..\..\OrderDesk\bin\Debug\net10.0-windows') {
    $file = Join-Path $PSScriptRoot "$folder\OrderDesk.dll.config"
    if (-not (Test-Path -LiteralPath $file)) { continue }
    $path = (Resolve-Path -LiteralPath $file).Path
    [xml] $xml = Get-Content -Raw -LiteralPath $path
    $broker = $xml.configuration.appSettings.add | Where-Object key -eq 'WarehouseBroker'
    if ($broker -and $broker.value -ne $shop) {
        $broker.value = $shop
        $xml.Save($path)
        "Pointed $path back at the shop's broker"
    }
}
$data = Join-Path $env:LOCALAPPDATA 'OrderDesk'
if (Test-Path -LiteralPath $data) {
    Remove-Item -LiteralPath $data -Recurse -Force
    "Deleted $data"
}
