using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Data;

/// <summary>
/// MSMQ, which .NET has no client for, through Windows PowerShell and the .NET Framework's System.Messaging. A message's
/// text is written as XmlMessageFormatter writes a string, which .NET apps read by default, or as plain text; one is
/// read either way. A transactional queue gets a transaction of its own for each message.
/// </summary>
internal sealed class MsmqBroker(IMachine machine) : IBroker
{
    // Opens the queue in $args[0], with what reads a message's text whichever way it was written.
    private const string Open = """
        Add-Type -AssemblyName System.Messaging
        $queue = New-Object System.Messaging.MessageQueue $args[0]
        if (-not [System.Messaging.MessageQueue]::Exists($args[0])) { throw "There is no queue $($args[0])." }
        $queue.MessageReadPropertyFilter.Body = $true
        $single = if ($queue.Transactional) { [System.Messaging.MessageQueueTransactionType]::Single } else { [System.Messaging.MessageQueueTransactionType]::None }
        function Read-Body($message) {
            $bytes = New-Object byte[] $message.BodyStream.Length
            [void]$message.BodyStream.Read($bytes, 0, $bytes.Length)
            $text = if ($bytes.Length -ge 2 -and $bytes[1] -eq 0) { [System.Text.Encoding]::Unicode.GetString($bytes) } else { [System.Text.Encoding]::UTF8.GetString($bytes) }
            $text = $text.TrimStart([char]0xFEFF)
            if ($text -match '^<\?xml[^>]*\?>\s*<string>([\s\S]*)</string>\s*$') { $text = [System.Net.WebUtility]::HtmlDecode($matches[1]) }
            [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($text))
        }

        """;

    public void Send(QueueSettings queue, string body) => Run(queue, """
        $text = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($args[1]))
        $message = New-Object System.Messaging.Message
        if ($args[2] -eq 'text') { $message.BodyStream = New-Object System.IO.MemoryStream (,[System.Text.Encoding]::UTF8.GetBytes($text)) }
        else { $message.Formatter = New-Object System.Messaging.XmlMessageFormatter (,[type[]]@([string])); $message.Body = $text }
        $queue.Send($message, $single)
        """, Encoded(body), queue.Format ?? "xml");

    public string? Receive(QueueSettings queue, TimeSpan within) => Run(queue, """
        try { Read-Body ($queue.Receive([TimeSpan]::FromMilliseconds([double]$args[1]), $single)) }
        catch [System.Messaging.MessageQueueException] { if ($_.Exception.MessageQueueErrorCode -ne 'IOTimeout') { throw } }
        """, ((long)within.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)) is [var body] ? body : null;

    public int Purge(QueueSettings queue) => int.Parse(Run(queue, """
        $count = $queue.GetAllMessages().Length
        $queue.Purge()
        [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("$count"))
        """)[0], System.Globalization.CultureInfo.InvariantCulture);

    public IReadOnlyList<string> Peek(QueueSettings queue, int most) => Run(queue, """
        $queue.GetAllMessages() | Select-Object -First ([int]$args[1]) | ForEach-Object { Read-Body $_ }
        """, most.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public int Count(QueueSettings queue) => int.Parse(Run(queue, """
        [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("$($queue.GetAllMessages().Length)"))
        """)[0], System.Globalization.CultureInfo.InvariantCulture);

    // Each line the script prints is a message's text, in base64 so its own lines and characters come back as they are.
    private List<string> Run(QueueSettings queue, string script, params string[] arguments)
    {
        var output = machine.PowerShell(Open + script, [queue.Queue, .. arguments], TimeSpan.FromMinutes(2));
        if (output.ExitCode != 0)
            throw new InvalidOperationException(output.Output.Trim());
        return [.. output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(line)))];
    }

    private static string Encoded(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
}
