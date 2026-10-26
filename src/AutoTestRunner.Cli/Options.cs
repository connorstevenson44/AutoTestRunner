using AutoTestRunner.Driving;

namespace AutoTestRunner.Cli;

/// <summary>What was asked for on the command line after the suite folder.</summary>
public sealed record Options
{
    public bool Unattended { get; init; }
    public string? Cases { get; init; }
    public Speed Speed { get; init; } = Speed.Modest;
    public string Mode { get; init; } = "auto";
    public string? Results { get; init; }
    public Dictionary<string, string> Variables { get; } = [];
    public string? Problem { get; init; }

    /// <summary>launch: start the cases given straight away, as after restarting as administrator to run them.</summary>
    public bool Start { get; init; }

    /// <summary>record: the case to open and change, by number or id.</summary>
    public string? Edit { get; init; }

    /// <summary>record: the case whose end the new case starts from, by number or id.</summary>
    public string? After { get; init; }

    /// <summary>record: the new case's name.</summary>
    public string? Name { get; init; }

    /// <summary>The options as arguments again, to pass on when autotest restarts itself.</summary>
    public IEnumerable<string> Arguments()
    {
        foreach (var (flag, value) in new[] { ("--cases", Cases), ("--mode", Mode), ("--speed", Speed.ToString()), ("--results", Results), ("--edit", Edit), ("--after", After), ("--name", Name) })
        {
            if (value is null)
                continue;
            yield return flag;
            yield return value;
        }
        foreach (var (name, value) in Variables)
        {
            yield return "--var";
            yield return $"{name}={value}";
        }
        if (Start)
            yield return "--start";
    }

    public static Options Read(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            string Next() => index + 1 < args.Length ? args[++index] : "";
            switch (args[index])
            {
                case "--unattended":
                    options = options with { Unattended = true };
                    break;
                case "--cases":
                    options = options with { Cases = Next() };
                    break;
                case "--speed":
                    var speed = Next();
                    options = Enum.TryParse<Speed>(speed, ignoreCase: true, out var parsed)
                        ? options with { Speed = parsed }
                        : options with { Problem = $"The speed is Slow, Modest or Fast, not {speed}." };
                    break;
                case "--mode":
                    var mode = Next();
                    options = mode is "auto" or "pause-at-checkpoints" or "step-by-step"
                        ? options with { Mode = mode }
                        : options with { Problem = $"The mode is auto, pause-at-checkpoints or step-by-step, not {mode}." };
                    break;
                case "--start":
                    options = options with { Start = true };
                    break;
                case "--edit" or "--add-to":
                    options = options with { Edit = Next() };
                    break;
                case "--after":
                    options = options with { After = Next() };
                    break;
                case "--name":
                    options = options with { Name = Next() };
                    break;
                case "--results":
                    options = options with { Results = Next() };
                    break;
                case "--var":
                    var pair = Next().Split('=', 2);
                    if (pair.Length == 2)
                        options.Variables[pair[0]] = pair[1];
                    else
                        options = options with { Problem = "--var is name=value." };
                    break;
                default:
                    options = options with { Problem = $"I don't know {args[index]}." };
                    break;
            }
        }
        return options;
    }
}
