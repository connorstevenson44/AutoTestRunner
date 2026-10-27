using System.Globalization;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
    // Steps that query a database, and send, receive and empty queues. False when the step is another kind.
    private bool DataStep(StepAt at)
    {
        var (step, scope) = (at.Step, at.Scope);
        switch (at.Kind)
        {
            case "query":
            {
                var database = Database(at.Required("query"), scope);
                var sql = at.Required("sql");
                ShowStep(at.Line(ActivityKind.Database, Part.Verb("Query"), Sources.Part(database), Part.Value(OneLine(sql))) with { Command = sql });
                QueryResult result;
                try
                {
                    result = Reported.Query(database, sql, Checks.Parameters(step["params"], scope), at.Within(TimeSpan.FromSeconds(30)));
                }
                catch (Exception ex) when (ex is not (StopRunException or ScriptException))
                {
                    FailStep(new StepFailure($"The query on {database.Name} failed: {ex.Message}", "rows", ex.Message));
                    break;
                }
                if (step["as"]?.GetValue<string>() is { } keep)
                {
                    scope.Set(keep, Value.Of(result.First, new Dictionary<string, string>
                    {
                        ["rows"] = result.Count.ToString(CultureInfo.InvariantCulture),
                        ["output"] = result.Table(int.MaxValue),
                    }));
                }
                break;
            }

            case "send":
            {
                var queue = Queue(at.Required("send"), scope);
                var body = step["file"] is { } file ? File.ReadAllText(SuitePath(scope.Expand(file.ToString()))) : at.Text("message")!;
                ShowStep(at.Line(ActivityKind.Queue, Part.Verb("Send"), Part.Value(OneLine(body)), Part.Word("to"), Sources.Part(queue)) with { Command = body });
                Queued(queue, "send", () => Reported.Send(queue, body));
                break;
            }

            case "receive":
            {
                var queue = Queue(at.Required("receive"), scope);
                var wanted = WantedText(step, scope, first: null);
                var line = at.Line(ActivityKind.Queue, Part.Verb("Receive"), Part.Word("from"), Sources.Part(queue));
                ShowStep(line with { Command = Sources.Line(queue) });
                _view.ShowWait(line);
                var within = Control.Scale(at.Within(TimeSpan.FromSeconds(10)));
                string? body = null;
                Queued(queue, "receive from", () => body = Reported.Receive(queue, within));
                if (body is null)
                    throw new StuckException($"receive a message from {queue.Name}", $"Nothing came in {within.TotalSeconds:0} s.");
                if (wanted.Expected.Length > 0 && !wanted.Test(body))
                    FailStep(new StepFailure($"The message from {queue.Name} isn't the one expected: wanted {wanted.Expected}, got {OneLine(body)}", wanted.Expected, OneLine(body)));
                if (step["as"]?.GetValue<string>() is { } keep)
                    scope.Set(keep, Value.Of(body));
                break;
            }

            case "purge":
            {
                var queue = Queue(at.Required("purge"), scope);
                ShowStep(at.Line(ActivityKind.Queue, Part.Verb("Empty"), Sources.Part(queue)));
                Queued(queue, "empty", () => Reported.Purge(queue));
                break;
            }

            default:
                return false;
        }
        return true;
    }

    // A message step: when the broker can't be reached or refuses, the step fails and the case carries on.
    private void Queued(QueueSettings queue, string what, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is not (StopRunException or ScriptException))
        {
            FailStep(new StepFailure($"Couldn't {what} {queue.Name}: {ex.Message}", "done", ex.Message));
        }
    }

    // SQL or a message on one line, short enough for a step's row: the whole of it shows with the command.
    private static string OneLine(string text)
    {
        var line = Regex.Replace(text.Trim(), @"\s+", " ");
        return line.Length <= 60 ? line : line[..59] + "…";
    }

    /// <summary>A database by its name in suite.json, with its variables filled in.</summary>
    private DatabaseSettings Database(string name, VariableScope scope)
    {
        if (!_suite.Databases.TryGetValue(name, out var database))
            throw new ScriptException($"There is no database called {name} in suite.json.");
        // A kind read from the app's own settings may be written SqlServer or SQLITE.
        var given = scope.Expand(database.Kind).Trim();
        var kind = DatabaseSettings.Kinds.Keys.FirstOrDefault(each => string.Equals(each, given, StringComparison.OrdinalIgnoreCase))
            ?? throw new ScriptException($"Database {name}'s kind, {database.Kind}, comes to \"{given}\", which isn't one of: {string.Join(", ", DatabaseSettings.Kinds.Keys)}.");
        return database with { Kind = kind, Connection = scope.Expand(database.Connection) };
    }

    /// <summary>A queue by its name in suite.json, with its variables filled in.</summary>
    private QueueSettings Queue(string name, VariableScope scope)
    {
        if (!_suite.Queues.TryGetValue(name, out var queue))
            throw new ScriptException($"There is no queue called {name} in suite.json.");
        string? Filled(string? text) => text is null ? null : scope.Expand(text);
        return queue with
        {
            Queue = scope.Expand(queue.Queue),
            Connection = Filled(queue.Connection),
            Channel = Filled(queue.Channel),
            QueueManager = Filled(queue.QueueManager),
            User = Filled(queue.User),
            Password = Filled(queue.Password),
        };
    }
}
