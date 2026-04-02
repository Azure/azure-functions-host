## General

* Make only high-confidence suggestions when reviewing code changes.
* Always use the latest version of C#, currently C# 13 features.
* Never change global.json unless explicitly asked to.
* Never change package.json or package-lock.json files unless explicitly asked to.
* Never change NuGet.config files unless explicitly asked to.
* For C# string comparisons, always use string.Equals with an appropriate, explicit, `StringComparison`.

## Formatting

* Apply code-formatting style defined in `.editorconfig`.
* Prefer file-scoped namespace declarations and single-line using directives.
* Insert a newline before the opening curly brace of any code block (e.g., after `if`, `for`, `while`, `foreach`, `using`, `try`, etc.).
* Ensure that the final return statement of a method is on its own line.
* Use pattern matching and switch expressions wherever possible.
* Use `nameof` instead of string literals when referring to member names.
* Ensure that XML doc comments are created for any public APIs. When applicable, include `<example>` and `<code>` documentation in the comments.

### Nullable Reference Types

* Declare variables non-nullable, and check for `null` at entry points.
* Always use `is null` or `is not null` instead of `== null` or `!= null`.
* Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

### Testing

* Do not emit "Act", "Arrange" or "Assert" comments.
* Use Moq for mocking in tests.
* Copy existing style in nearby files for test method names and capitalization.
* Do not use private reflection (e.g., `BindingFlags.NonPublic`, `GetField`, `GetProperty` with non-public flags) to access internal state in tests. If something needs to be tested, make it accessible through public or internal APIs, test-specific seams, or refactor the design to be testable without reflection.

## Dependencies & Patterns

* Prefer `System.Text.Json` over `Newtonsoft.Json` for new code. Only use Newtonsoft when interfacing with existing APIs that require it (e.g., `JObject`-based shared helpers).
* In this repository, `Microsoft.Azure.WebJobs.Script.IEnvironment` is our internal interface for accessing process environment data (for example, environment variables and related flags). Do not introduce new usage of `IEnvironment` for reading configuration; prefer the standard `Microsoft.Extensions.Configuration.IConfiguration` abstraction instead. Existing `IEnvironment` usage in legacy code is acceptable but should not be extended beyond its current scope.
* Do not use event-based communication (`IScriptEventManager` pub/sub) for new component coordination. Prefer direct method calls or `await`-based flows. The event manager's keyed state store (`TryAddWorkerState`/`TryGetWorkerState`) is acceptable when needed by existing infrastructure.
