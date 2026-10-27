using AutoTestRunner.Running;
using RabbitMQ.Client;

namespace AutoTestRunner.Data.Tests;

/// <summary>Makes the queues the tests use, which a suite would find already there.</summary>
internal static class Rabbit
{
    public static string Declare(string address, string queue)
    {
        Task.Run(async () =>
        {
            await using var connection = await new ConnectionFactory { Uri = new Uri(address) }.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        }).GetAwaiter().GetResult();
        return queue;
    }
}

internal static class Msmq
{
    public static string Make(string path)
    {
        var output = new WindowsMachine().PowerShell("""
            Add-Type -AssemblyName System.Messaging
            if (-not [System.Messaging.MessageQueue]::Exists($args[0])) { [void][System.Messaging.MessageQueue]::Create($args[0]) }
            """, [path]);
        Assert.True(output.ExitCode == 0, output.Output);
        return path;
    }
}

/// <summary>The machine, for the drivers that don't use it.</summary>
internal sealed class UnusedMachine : IMachine
{
    public string? ServiceState(string name) => throw new NotSupportedException();
    public bool TaskExists(string path) => throw new NotSupportedException();
    public void SetServiceRunning(string name, bool running) => throw new NotSupportedException();
    public string? RegistryValue(string key, string name) => throw new NotSupportedException();
    public bool ProcessRunning(string name) => throw new NotSupportedException();
    public bool EventLogged(string log, string? source, string text, DateTime since) => throw new NotSupportedException();
    public string Clipboard() => throw new NotSupportedException();
    public (int Status, string Body) Get(string url) => throw new NotSupportedException();
    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null, string? folder = null) => throw new NotSupportedException();
    public ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null, Action<string, bool>? output = null) => throw new NotSupportedException();
    public void Open(string what, string path, IReadOnlyList<string>? arguments = null) => throw new NotSupportedException();
}
