using System.Collections;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;
using IBM.WMQ;

namespace AutoTestRunner.Data;

/// <summary>
/// IBM MQ, through IBM's managed .NET client as a client connection: the queue manager at host(port) over a server
/// connection channel, with a user and password when the channel asks for them. Messages are text, in UTF-8.
/// </summary>
internal sealed partial class IbmBroker : IBroker
{
    public static readonly IbmBroker Instance = new();

    // IBM's client asks for Windows code pages, which .NET has only once they are registered.
    static IbmBroker() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    private const int Utf8 = 1208;

    public void Send(QueueSettings queue, string body) => With(queue, MQC.MQOO_OUTPUT, open =>
    {
        var message = new MQMessage { Format = MQC.MQFMT_STRING, CharacterSet = Utf8 };
        message.WriteString(body);
        open.Put(message, new MQPutMessageOptions());
        return 0;
    });

    public string? Receive(QueueSettings queue, TimeSpan within) => With(queue, MQC.MQOO_INPUT_AS_Q_DEF, open =>
        Get(open, MQC.MQGMO_WAIT, (int)within.TotalMilliseconds));

    public int Purge(QueueSettings queue) => With(queue, MQC.MQOO_INPUT_AS_Q_DEF, open =>
    {
        var count = 0;
        while (Get(open, MQC.MQGMO_NO_WAIT, 0) is not null)
            count++;
        return count;
    });

    public IReadOnlyList<string> Peek(QueueSettings queue, int most) => With(queue, MQC.MQOO_BROWSE, open =>
    {
        var bodies = new List<string>();
        while (bodies.Count < most && Get(open, (bodies.Count == 0 ? MQC.MQGMO_BROWSE_FIRST : MQC.MQGMO_BROWSE_NEXT) | MQC.MQGMO_NO_WAIT, 0) is { } body)
            bodies.Add(body);
        return (IReadOnlyList<string>)bodies;
    });

    public int Count(QueueSettings queue) => With(queue, MQC.MQOO_INQUIRE, open => open.CurrentDepth);

    // The next message's text, or null when there is none.
    private static string? Get(MQQueue open, int options, int wait)
    {
        var message = new MQMessage();
        var get = new MQGetMessageOptions { Options = options | MQC.MQGMO_FAIL_IF_QUIESCING | MQC.MQGMO_CONVERT, WaitInterval = wait };
        try
        {
            open.Get(message, get);
            return message.ReadString(message.MessageLength);
        }
        catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
        {
            return null;
        }
    }

    private static T With<T>(QueueSettings queue, int options, Func<MQQueue, T> work)
    {
        if (Address().Match(queue.Connection ?? "") is not { Success: true } address)
            throw new ScriptException($"An IBM MQ connection is host(port), such as localhost(1414), not {queue.Connection}.");
        var properties = new Hashtable
        {
            [MQC.TRANSPORT_PROPERTY] = MQC.TRANSPORT_MQSERIES_MANAGED,
            [MQC.HOST_NAME_PROPERTY] = address.Groups["host"].Value,
            [MQC.PORT_PROPERTY] = address.Groups["port"].Success ? int.Parse(address.Groups["port"].Value, System.Globalization.CultureInfo.InvariantCulture) : 1414,
            [MQC.CHANNEL_PROPERTY] = queue.Channel,
        };
        if (queue.User is { } user)
        {
            properties[MQC.USER_ID_PROPERTY] = user;
            properties[MQC.PASSWORD_PROPERTY] = queue.Password ?? "";
            properties[MQC.USE_MQCSP_AUTHENTICATION_PROPERTY] = true;
        }
        var manager = new MQQueueManager(queue.QueueManager, properties);
        try
        {
            var open = manager.AccessQueue(queue.Queue, options | MQC.MQOO_FAIL_IF_QUIESCING);
            try
            {
                return work(open);
            }
            finally
            {
                open.Close();
            }
        }
        finally
        {
            manager.Disconnect();
        }
    }

    [GeneratedRegex(@"^\s*(?<host>[^()\s]+)\s*(\(\s*(?<port>\d+)\s*\))?\s*$")]
    private static partial Regex Address();
}
