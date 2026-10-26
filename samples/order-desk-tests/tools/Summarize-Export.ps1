# Reads Order Desk's export a line at a time, as a person checking it would, and ends with how many orders it has and
# what they come to: "3 orders, 58.00 in all".
$file = $args[0]
Write-Output "Reading $file"
$orders = @(Import-Csv -LiteralPath $file)
foreach ($order in $orders) {
    Start-Sleep -Milliseconds 400
    Write-Output ('Order {0}: {1}, {2} x {3}, {4}' -f $order.number, $order.customer, $order.quantity, $order.product, $order.total)
}
$sum = ($orders | Measure-Object -Property total -Sum).Sum
Write-Output ('{0} orders, {1} in all' -f $orders.Count, $sum.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture))
