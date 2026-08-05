# Environment migration source baselines

These baselines freeze the Phase 0 migration surface without depending on line
numbers:

- `EnvironmentMigrationInventory.json` records the 77 public
  `EnvironmentExtensions` helpers, production environment reads and writes,
  static access, hosting-environment predicates, compiled public signatures,
  and test seams.
- `EnvironmentMigrationAllowlist.json` records existing production and test
  debt for `IEnvironment`, `SystemEnvironment.Instance`,
  `ScriptSettingsManager.Instance`, and hosting-environment predicates.

Each source usage is keyed by repository-relative path, a trivia-free normalized
token sequence, and multiplicity. The scanner considers every preprocessor
branch, normalizes line endings, and preserves code inside raw-string
interpolations while ignoring literal text. It does not depend on the older
Roslyn parser's recovered declaration tree. Formatting, comments, directives,
and line movement do not change a key.

The test compares both directions: a new key fails the ratchet, and a stale
allowlist key also fails. Owning migration slices must therefore remove the
source usage and its allowlist entry together; deleted debt cannot silently
return.

Predicate growth is permitted only for exact bootstrap expressions or
parity-characterization files listed in `EnvironmentMigrationSourceScanner`.
Resolver and profile boundaries must not be added until their mandatory design
gates are approved.

After reviewing an intentional migration-surface change, refresh both files:

```powershell
eng\script\update-environment-migration-baselines.ps1
```

Then review the JSON diff and rerun the baseline test without update mode:

```powershell
dotnet test test\WebJobs.Script.Tests\WebJobs.Script.Tests.csproj `
  --filter "FullyQualifiedName~EnvironmentMigrationSourceUsageTests"
```
