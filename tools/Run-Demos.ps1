# Runs the demo suites unattended and checks each one ends the way it should: the Order Desk tests pass, each failure
# example fails or gets stuck with the message it is there to show, and validate finds the mistakes in the script
# mistakes suite. CI runs it against the demo bundle; it runs the same from a folder laid out like the bundle.
#
#   tools/Run-Demos.ps1 -Bundle <folder with autotestrunner\, OrderDesk\ and the suites> -Results <folder>
param(
    [Parameter(Mandatory)] [string] $Bundle,
    [Parameter(Mandatory)] [string] $Results
)

$ErrorActionPreference = 'Stop'
$autotest = Join-Path $Bundle 'autotestrunner\autotest.exe'
$tour = Join-Path $Bundle 'order-desk-tests'
$failures = Join-Path $Bundle 'order-desk-failure-examples'
$mistakes = Join-Path $Bundle 'script-mistakes'
New-Item -ItemType Directory -Force -Path $Results | Out-Null
$problems = [System.Collections.Generic.List[string]]::new()
$report = [System.Collections.Generic.List[string]]::new()

function Summary($folder) {
    $file = Get-ChildItem -Path $folder -Recurse -Filter summary.txt -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($file) { Get-Content -Raw -LiteralPath $file.FullName } else { '' }
}

# validate: the two Order Desk suites have no mistakes, and every mistake in the script mistakes suite is found.
foreach ($suite in @($tour, $failures)) {
    & $autotest validate $suite
    if ($LASTEXITCODE -ne 0) { $problems.Add("validate found mistakes in $suite (exit code $LASTEXITCODE).") }
}
# validate prints the mistakes to stderr, which mustn't stop this script.
$ErrorActionPreference = 'Continue'
$found = & $autotest validate $mistakes 2>&1 | ForEach-Object { "$_" } | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$found | Set-Content -LiteralPath (Join-Path $Results 'script-mistakes.txt') -Encoding utf8
$found
$count = @($found -split "`n" | Where-Object { $_ -match '^\s+\S' }).Count
if ($code -ne 3) { $problems.Add("validate script-mistakes ended with exit code $code, not 3.") }
if ($count -lt 12) { $problems.Add("validate found $count mistakes in script-mistakes, expected at least 12.") }
$report.Add("validate: found $count mistakes in script-mistakes, exit code 3 as expected")

# The Order Desk tests pass.
& $autotest run $tour --unattended --results (Join-Path $Results 'order-desk-tests')
$code = $LASTEXITCODE
if ($code -ne 0) { $problems.Add("The Order Desk tests ended with exit code $code, not 0:`n$(Summary (Join-Path $Results 'order-desk-tests'))") }
else { $report.Add('Order Desk tests: every case passed') }

# Each failure example fails with the message it shows. They run one at a time, as an unattended run stops at the
# first case that gets stuck.
$expected = [ordered]@{
    1 = 'Saved total: found 24.00'
    2 = 'expected "Delete 1 order?"'
    3 = 'Find-Export.ps1 failed with exit code 2'
    4 = 'The text has nothing matching back door'
    5 = "It's greyed out, so it can't be used yet."
    6 = "I couldn't find the Submit order button."
    7 = 'It says: Connecting to the warehouse'
    8 = 'is showing another window instead: "Import order"'
}
foreach ($entry in $expected.GetEnumerator()) {
    # Indexing an ordered dictionary with a number picks by position, so take each key and its text together.
    $case = $entry.Key
    $text = $entry.Value
    $folder = Join-Path $Results "failure-example-$case"
    & $autotest run $failures --unattended --cases $case --results $folder
    $code = $LASTEXITCODE
    $summary = Summary $folder
    if ($code -ne 1) { $problems.Add("Failure example $case ended with exit code $code, not 1.") }
    elseif ($summary -notmatch "(?m)^FAILED\s+$case\.") { $problems.Add("Failure example $case didn't fail:`n$summary") }
    elseif (-not $summary.Contains($text)) { $problems.Add("Failure example $case failed, but not with ``$text``:`n$summary") }
    else { $report.Add("failure example ${case}: failed as it should, with ``$text``") }
}
& (Join-Path $failures 'tools\Reset-OrderDesk.ps1') | Out-Null

# demos.txt has what ended as it should, then what didn't, for CI's summary.
$lines = [System.Collections.Generic.List[string]]::new($report)
if ($problems.Count -gt 0) { $lines.Add(''); $lines.Add("Didn't end as they should:"); $lines.AddRange($problems) }
$lines | Set-Content -LiteralPath (Join-Path $Results 'demos.txt') -Encoding utf8
''
$report
if ($problems.Count -gt 0) {
    ''
    $problems
    throw "$($problems.Count) of the demos didn't end the way they should."
}
# The failure examples leave the last command's exit code at 1, which CI would read as this script failing.
exit 0
