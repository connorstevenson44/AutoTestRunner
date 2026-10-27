using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// How a database or queue is shown: on a badge by its name in suite.json, with its kind and where it is to point at,
/// and passwords hidden wherever it shows.
/// </summary>
public static partial class Sources
{
    public static Part Part(DatabaseSettings database) => new(PartKind.Database, database.Name, Tip(database));

    public static Part Part(QueueSettings queue) => new(PartKind.Queue, queue.Name, Tip(queue));

    public static Subject Subject(DatabaseSettings database) => new(SubjectKind.Database, database.Name, Tip: Tip(database));

    public static Subject Subject(QueueSettings queue, string detail) => new(SubjectKind.Queue, queue.Name, detail, Tip(queue));

    public static string Tip(DatabaseSettings database) => $"{DatabaseSettings.Kinds[database.Kind]}\n{Hidden(database.Connection)}";

    public static string Tip(QueueSettings queue) =>
        string.Join("\n", new[] { QueueSettings.Kinds[queue.Kind], queue.Connection is null ? null : Hidden(queue.Connection), queue.QueueManager, queue.Channel, queue.Queue }
            .Where(line => !string.IsNullOrEmpty(line)));

    /// <summary>The line a query or a message shows as its command: orders (SQL Server), newOrders (RabbitMQ orders.new).</summary>
    public static string Line(DatabaseSettings database) => $"{database.Name} ({DatabaseSettings.Kinds[database.Kind]})";

    public static string Line(QueueSettings queue) => $"{queue.Name} ({QueueSettings.Kinds[queue.Kind]} {queue.Queue})";

    /// <summary>A connection string or address with its password as dots: Password=•••, amqp://guest:•••@host.</summary>
    public static string Hidden(string connection) =>
        UserInfo().Replace(Password().Replace(connection, match => $"{match.Groups[1].Value}=•••"), match => $"{match.Groups[1].Value}:•••@");

    [GeneratedRegex(@"\b(password|pwd)\s*=\s*(""[^""]*""|'[^']*'|[^;]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Password();

    [GeneratedRegex(@"(?<=://)([^:/@\s]+):[^@/\s]*@")]
    private static partial Regex UserInfo();
}
