using System.Diagnostics;
using System.Globalization;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// The databases and queues as a run's steps use them: each query, send, receive and purge is shown as a command is,
/// with what it ran and what came back, and goes in the log. Peeking and counting aren't shown, as checks do that often.
/// </summary>
public sealed class ReportingConnections(IConnections connections, IRunView view, Results results) : IConnections
{
    public QueryResult Query(DatabaseSettings database, string sql, IReadOnlyDictionary<string, string> parameters, TimeSpan timeout)
    {
        var script = string.Join("\n", [sql.Trim(), .. parameters.Select(pair => $"-- @{pair.Key} = {pair.Value}")]);
        return Shown(Sources.Line(database), script, () => connections.Query(database, sql, parameters, timeout), result => result.Table());
    }

    public void Send(QueueSettings queue, string body) =>
        Shown(Sources.Line(queue), body, () =>
        {
            connections.Send(queue, body);
            return 0;
        }, _ => "Sent.");

    public string? Receive(QueueSettings queue, TimeSpan within) =>
        Shown(Sources.Line(queue), null, () => connections.Receive(queue, within), body => body ?? $"Nothing came in {within.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s.");

    public int Purge(QueueSettings queue) =>
        Shown(Sources.Line(queue), null, () => connections.Purge(queue), count => $"Removed {count} message{(count == 1 ? "" : "s")}.");

    public IReadOnlyList<string> Peek(QueueSettings queue, int most) => connections.Peek(queue, most);

    public int Count(QueueSettings queue) => connections.Count(queue);

    private T Shown<T>(string line, string? script, Func<T> work, Func<T, string> said)
    {
        results.Log("RUN", line);
        view.CommandStarted(line, script);
        var clock = Stopwatch.StartNew();
        var code = -1;
        try
        {
            var result = work();
            code = 0;
            foreach (var text in said(result).Split('\n'))
            {
                results.Log("OUTPUT", text.TrimEnd());
                view.CommandOutput(text, false);
            }
            return result;
        }
        catch (Exception ex) when (ex is not Driving.StopRunException)
        {
            code = 1;
            results.Log("OUTPUT", ex.Message);
            view.CommandOutput(ex.Message, true);
            throw;
        }
        finally
        {
            results.Log("EXIT", $"{code}, after {clock.Elapsed.TotalSeconds:0.0} s");
            view.CommandFinished(code, clock.Elapsed);
        }
    }
}
