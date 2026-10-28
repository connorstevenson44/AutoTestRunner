# Puts the computer back as it was before the showcase ran: closes Order Desk, points it back at the shop's broker in
# OrderDesk.dll.config, and deletes its data, the exports and what it remembers in the registry.
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
foreach ($folder in (Join-Path $env:LOCALAPPDATA 'OrderDesk'), (Join-Path $env:LOCALAPPDATA 'OrderDesk exports')) {
    if (Test-Path -LiteralPath $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
        "Deleted $folder"
    }
}
if (Test-Path -LiteralPath 'HKCU:\Software\OrderDesk') {
    Remove-Item -LiteralPath 'HKCU:\Software\OrderDesk' -Recurse -Force
    'Deleted HKCU\Software\OrderDesk'
}
exit 0
