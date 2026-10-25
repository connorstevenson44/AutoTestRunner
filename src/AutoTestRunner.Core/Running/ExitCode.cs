namespace AutoTestRunner.Running;

/// <summary>How a run ended, as the process's exit code. CI relies on these, as it did on the AutoHotkey runner's.</summary>
public enum ExitCode { Passed = 0, Failed = 1, Missing = 2, ScriptError = 3, NotAdministrator = 4 }
