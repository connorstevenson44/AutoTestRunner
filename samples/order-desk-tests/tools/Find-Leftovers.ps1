# Prints one line for each thing an earlier run of the Order Desk tests left behind. AutoTestRunner runs this before a
# run, and cleans up with Reset-OrderDesk.ps1 when it prints anything.
foreach ($folder in (Join-Path $env:LOCALAPPDATA 'OrderDesk'), (Join-Path $env:LOCALAPPDATA 'OrderDesk exports')) {
    if (Test-Path -LiteralPath $folder) {
        "The folder $folder"
    }
}
if (Test-Path -LiteralPath 'HKCU:\Software\OrderDesk') {
    'What Order Desk remembers, in HKCU\Software\OrderDesk'
}
schtasks.exe /Query /TN 'Order Desk nightly export' 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    'The scheduled task Order Desk nightly export'
}
exit 0
