# AutoTestRunner

Keep this file under 20 lines: CodebaseNotesTests fails the build when it grows past that.

- Build: `dotnet build -c Release`. Warnings are errors, and an unused `using` is one.
- Test: `dotnet test -c Release --no-build`. Format: `dotnet format whitespace --verify-no-changes`.
- On Linux, install the SDK with `apt-get install -y dotnet-sdk-10.0`. Everything builds there; tests that drive
  Windows skip themselves.
- Core holds the script format (Scripting), running (Running), building cases (Building) and the driver interfaces
  (Driving). Data has the database and queue drivers, FlaUI drives the app and wraps Win32 (Native), Cli is the
  `autotest` program and its WPF views.
- The step and check vocabulary lives in `ScriptVocabulary`; `schema/` and `docs/format.md` follow it, and a test
  checks the schemas against it.
- Big classes are split into partial files by concern, such as `Runner.DataSteps.cs`: search for the file first.
- Read hand-written JSON with `ScriptJson`, and edit it in place with `JsonLine`.
- Names and comments are plain English about what the app does. Comments say why, not what the code says.
