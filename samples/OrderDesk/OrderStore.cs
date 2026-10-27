using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace OrderDesk;

/// <summary>
/// The orders, customers and staff, in orderdesk.db in the data folder, with every change written to activity.log there
/// too, so a test can check both. A connection is opened for each change and closed straight after, so the file can be
/// backed up, restored or read by someone else at any time.
/// </summary>
public sealed class OrderStore
{
    private const string Schema = """
        create table if not exists Staff (UserName text primary key, FullName text not null, PasswordHash text not null);
        create table if not exists Customers (Account text primary key, Name text not null unique, Email text not null default '');
        create table if not exists Orders (
            Number integer primary key autoincrement, Customer text not null, Sku text not null, Product text not null,
            Quantity integer not null, GiftWrap integer not null, Delivery text not null, DeliverBy text, Notes text not null default '',
            Total text not null, Status text not null default 'New', Created text not null);
        insert into sqlite_sequence (name, seq) select 'Orders', 1000 where not exists (select 1 from sqlite_sequence where name = 'Orders');
        """;

    private static readonly (string Account, string Name)[] FirstCustomers =
    [
        ("C-1001", "Ada Lovelace"), ("C-1002", "Grace Hopper"), ("C-1003", "Alan Turing"), ("C-1004", "Katherine Johnson"),
        ("C-1005", "Mary Jackson"), ("C-1006", "Margaret Hamilton"), ("C-1007", "Dorothy Vaughan"),
    ];

    private static readonly (string User, string Name, string Password)[] FirstStaff = [("clerk", "Jo Bloggs", "clerk-demo"), ("manager", "Sam Patel", "manager-demo")];

    private readonly string _connection;

    public OrderStore(string folder, string connection)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        _connection = connection.Replace("{DataFolder}", folder, StringComparison.Ordinal);
        using var db = Open();
        Execute(db, Schema);
        if (Scalar(db, "select count(*) from Staff") == 0)
            foreach (var (user, name, password) in FirstStaff)
                Execute(db, "insert into Staff values (@user, @name, @hash)", ("@user", user), ("@name", name), ("@hash", Hash(password)));
        if (Scalar(db, "select count(*) from Customers") == 0)
            foreach (var (account, name) in FirstCustomers)
                Execute(db, "insert into Customers values (@account, @name, @email)", ("@account", account), ("@name", name), ("@email", Email(name)));
    }

    public string Folder { get; }

    public string DatabaseFile => Path.Combine(Folder, "orderdesk.db");

    public string LogFile => Path.Combine(Folder, "activity.log");

    public string SettingsFile => Path.Combine(Folder, "settings.json");

    // ===== Staff =====

    /// <summary>The person's full name when the password is theirs, else null.</summary>
    public string? SignIn(string user, string password)
    {
        using var db = Open();
        using var command = Command(db, "select FullName from Staff where UserName = @user and PasswordHash = @hash", ("@user", user), ("@hash", Hash(password)));
        return command.ExecuteScalar() as string;
    }

    // ===== Orders =====

    public List<Order> Orders()
    {
        using var db = Open();
        using var command = Command(db, "select Number, Customer, Sku, Product, Quantity, GiftWrap, Delivery, DeliverBy, Notes, Total, Status from Orders order by Number");
        using var reader = command.ExecuteReader();
        var orders = new List<Order>();
        while (reader.Read())
        {
            orders.Add(new Order
            {
                Number = reader.GetInt32(0),
                Customer = reader.GetString(1),
                Sku = reader.GetString(2),
                Product = reader.GetString(3),
                Quantity = reader.GetInt32(4),
                GiftWrap = reader.GetInt32(5) == 1,
                Delivery = reader.GetString(6),
                DeliverBy = reader.IsDBNull(7) ? "" : reader.GetString(7),
                Notes = reader.GetString(8),
                Total = reader.GetString(9),
                Status = reader.GetString(10),
            });
        }
        return orders;
    }

    public Order Add(string customer, Product product, int quantity, bool giftWrap, string delivery, string deliverBy, string notes)
    {
        var order = new Order
        {
            Customer = customer,
            Sku = product.Sku,
            Product = product.Name,
            Quantity = quantity,
            GiftWrap = giftWrap,
            Delivery = delivery,
            DeliverBy = deliverBy,
            Notes = notes,
            Total = Pricing.Plain(Pricing.SavedTotal(product, quantity, giftWrap, delivery)),
        };
        using (var db = Open())
        {
            AddCustomer(db, customer);
            Execute(db, """
                insert into Orders (Customer, Sku, Product, Quantity, GiftWrap, Delivery, DeliverBy, Notes, Total, Created)
                values (@customer, @sku, @product, @quantity, @giftWrap, @delivery, @deliverBy, @notes, @total, @created)
                """,
                ("@customer", customer), ("@sku", product.Sku), ("@product", product.Name), ("@quantity", quantity), ("@giftWrap", giftWrap ? 1 : 0),
                ("@delivery", delivery), ("@deliverBy", deliverBy.Length == 0 ? null : deliverBy), ("@notes", notes), ("@total", order.Total),
                ("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            order.Number = (int)Scalar(db, "select last_insert_rowid()");
        }
        Log($"Added order {order.Number}: {customer}, {quantity} x {product.Name}, {order.Total}");
        return order;
    }

    /// <summary>Changes an order's quantity and note, and works its total out again.</summary>
    public void Change(Order order, int quantity, string notes)
    {
        order.Quantity = quantity;
        order.Notes = notes;
        if (Product.Named(order.Product) is { } product)
            order.Total = Pricing.Plain(Pricing.SavedTotal(product, quantity, order.GiftWrap, order.Delivery));
        using var db = Open();
        Execute(db, "update Orders set Quantity = @quantity, Notes = @notes, Total = @total where Number = @number",
            ("@quantity", quantity), ("@notes", notes), ("@total", order.Total), ("@number", order.Number));
        Log($"Changed order {order.Number}: {quantity} x {order.Product}, {order.Total}");
    }

    public void SetStatus(Order order, string status)
    {
        order.Status = status;
        using var db = Open();
        Execute(db, "update Orders set Status = @status where Number = @number", ("@status", status), ("@number", order.Number));
        Log($"{status} order {order.Number}");
    }

    public void Delete(Order order)
    {
        using var db = Open();
        Execute(db, "delete from Orders where Number = @number", ("@number", order.Number));
        Log($"Deleted order {order.Number}");
    }

    // ===== Customers =====

    public List<Customer> Customers()
    {
        using var db = Open();
        using var command = Command(db, """
            select c.Account, c.Name, c.Email, (select count(*) from Orders o where o.Customer = c.Name)
            from Customers c order by c.Account
            """);
        using var reader = command.ExecuteReader();
        var customers = new List<Customer>();
        while (reader.Read())
            customers.Add(new Customer(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return customers;
    }

    // A name taken on the order form that isn't a customer yet becomes one, with the next account number.
    private static void AddCustomer(SqliteConnection db, string name)
    {
        if (Scalar(db, "select count(*) from Customers where Name = @name", ("@name", name)) > 0)
            return;
        var next = Scalar(db, "select coalesce(max(cast(substr(Account, 3) as integer)), 1000) + 1 from Customers");
        Execute(db, "insert into Customers values (@account, @name, @email)", ("@account", $"C-{next}"), ("@name", name), ("@email", Email(name)));
    }

    private static string Email(string name) => name.ToLowerInvariant().Replace(' ', '.') + "@example.com";

    // ===== Files =====

    public static string CsvHeader => "number,customer,product,quantity,giftWrap,delivery,deliverBy,total,status";

    public static string CsvLine(Order order) =>
        string.Join(",", order.Number, Quote(order.Customer), Quote(order.Product), order.Quantity, order.GiftWrap ? "yes" : "no", order.Delivery,
            order.DeliverBy, order.Total, order.Status);

    private static string Quote(string text) => text.Contains(',') || text.Contains('"') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    /// <summary>Writes the database, settings and activity log to a .zip in the folder, and gives its path.</summary>
    public string Backup(string folder)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"orderdesk-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var part in new[] { DatabaseFile, SettingsFile, LogFile }.Where(File.Exists))
                zip.CreateEntryFromFile(part, Path.GetFileName(part));
        }
        Log($"Backed up to {file}");
        return file;
    }

    /// <summary>How many orders the backup has, or null when it isn't an Order Desk backup.</summary>
    public static int? OrdersIn(string backup)
    {
        var copy = Unpack(backup);
        if (copy is null)
            return null;
        try
        {
            using var db = new SqliteConnection($"Data Source={copy};Pooling=False");
            db.Open();
            return (int)Scalar(db, "select count(*) from Orders");
        }
        finally
        {
            File.Delete(copy);
        }
    }

    /// <summary>Puts the backup's database in place of this one.</summary>
    public void Restore(string backup)
    {
        var copy = Unpack(backup) ?? throw new InvalidDataException($"{backup} isn't an Order Desk backup.");
        File.Copy(copy, DatabaseFile, overwrite: true);
        File.Delete(copy);
        Log($"Restored from {backup}");
    }

    private static string? Unpack(string backup)
    {
        using var zip = ZipFile.OpenRead(backup);
        if (zip.GetEntry("orderdesk.db") is not { } entry)
            return null;
        var copy = Path.Combine(Path.GetTempPath(), $"orderdesk-restore-{Guid.NewGuid():N}.db");
        entry.ExtractToFile(copy);
        return copy;
    }

    public void Log(string line) =>
        File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}", Encoding.UTF8);

    // ===== SQL =====

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        return db;
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object? Value)[] values)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void Execute(SqliteConnection db, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(db, sql, values);
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection db, string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(db, sql, values);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string Hash(string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
}
