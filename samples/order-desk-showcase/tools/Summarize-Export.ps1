# Reads Order Desk's export as a person checking it would, and ends with how many orders it has and what they come to:
# "3 orders, 73.00 in all".
$file = $args[0]
Write-Output "Reading $file"
$orders = @(Import-Csv -LiteralPath $file)
foreach ($order in $orders) {
    Write-Output ('Order {0}: {1}, {2} x {3}, {4}, {5}' -f $order.number, $order.customer, $order.quantity, $order.product, $order.total, $order.status)
}
$sum = ($orders | Measure-Object -Property total -Sum).Sum
Write-Output ('{0} orders, {1} in all' -f $orders.Count, $sum.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture))
