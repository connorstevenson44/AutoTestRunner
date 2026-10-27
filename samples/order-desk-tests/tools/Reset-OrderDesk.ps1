# Puts the computer back as it was before the Order Desk tests ran: closes Order Desk, deletes its data and the tests'
# exports and backups, what it remembers in the registry, and the nightly export's scheduled task.
Get-Process -Name OrderDesk -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
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
schtasks.exe /Query /TN 'Order Desk nightly export' 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    schtasks.exe /Delete /F /TN 'Order Desk nightly export' | Out-Null
    'Deleted the scheduled task Order Desk nightly export'
}
exit 0
