# Puts the computer back as it was before the Order Desk demos ran: closes Order Desk and deletes its data folder.
Get-Process -Name OrderDesk -ErrorAction SilentlyContinue | Stop-Process -Force
$data = Join-Path $env:LOCALAPPDATA 'OrderDesk'
if (Test-Path -LiteralPath $data) {
    Remove-Item -LiteralPath $data -Recurse -Force
    "Deleted $data"
}
