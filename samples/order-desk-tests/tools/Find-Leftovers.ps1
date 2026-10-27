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
if (Get-ScheduledTask -TaskName 'Order Desk nightly export' -ErrorAction SilentlyContinue) {
    'The scheduled task Order Desk nightly export'
}
exit 0
