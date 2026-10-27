# Order Desk release notes

## 2.4.1

- The activity history shows ten lines a page, newest first.
- Restoring a backup asks first, and says how many orders each side has.
- Known issue, OD-118: an express order's saved total leaves out the express fee. The form shows the right total.

## 2.4.0

- Shipped orders are sent to the warehouse, and the warehouse's picks mark the order as picked. Point
  `WarehouseBroker` in `OrderDesk.dll.config` at the shop's RabbitMQ.
- **Tools > Schedule nightly export** exports the orders at 23:00 each night.
- An order's menu can duplicate it or copy its number.

## 2.3.0

- Staff sign in, and Order Desk remembers who signed in last.
- Orders and customers are kept in a SQLite database, `orderdesk.db`, in the data folder, instead of `orders.json`.
- **File > Back up** and **File > Restore** keep and bring back the orders.
- Fees are in `settings.json`, beside the database.

## 2.2.0

- The catalogue lists the spare parts the service desk orders, by category.
- Next-day delivery, and a deliver-by date on each order.
