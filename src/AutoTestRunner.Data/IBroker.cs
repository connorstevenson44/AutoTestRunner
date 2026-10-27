using AutoTestRunner.Scripting;

namespace AutoTestRunner.Data;

/// <summary>A kind of message queue: what each queue step and check asks of it.</summary>
internal interface IBroker
{
    void Send(QueueSettings queue, string body);

    string? Receive(QueueSettings queue, TimeSpan within);

    int Purge(QueueSettings queue);

    IReadOnlyList<string> Peek(QueueSettings queue, int most);

    int Count(QueueSettings queue);
}
