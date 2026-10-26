# Looks for Order Desk's export, and ends with exit code 2 when there isn't one.
$file = $args[0]
Write-Output "Looking for $file"
if (-not (Test-Path -LiteralPath $file)) {
    Write-Output 'There is no export: nobody has clicked Export orders yet.'
    exit 2
}
