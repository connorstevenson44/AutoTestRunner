using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>A helper a suite registers for expressions, such as logFile(status): the first group of a regex.</summary>
public sealed record Helper(string Name, Regex Pattern);
