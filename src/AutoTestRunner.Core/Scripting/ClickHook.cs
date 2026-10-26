using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// Steps the builder adds after a control is clicked while recording, from suite.json's builder.afterClick: a wait of
/// delay seconds when there is one, then a call of the macro, with the parameters given. They stand for the wait
/// for the status text the click would otherwise get.
/// </summary>
/// <param name="Element">The control's name in elements.json.</param>
/// <param name="Call">The macro to run after it is clicked.</param>
/// <param name="With">The macro's parameters, or null.</param>
/// <param name="Delay">Seconds to wait before the macro runs, as a click can take a moment to start something.</param>
public sealed record ClickHook(string Element, string Call, JsonObject? With = null, double Delay = 0);
