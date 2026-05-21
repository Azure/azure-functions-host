---
mode: agent
description: Handle a failing Verify_DepsJsonChanges test by reviewing dependency changes and updating the checked-in deps.json baseline.
---

# Update WebHost deps.json baseline

Use this workflow whenever the `Verify_DepsJsonChanges` test in
`test/WebJobs.Script.Tests/DependencyTests.cs` fails, or when the user says
something like "the deps test failed", "update the deps.json", "refresh the
deps baseline", etc.

**This is a one-shot, non-interactive execution workflow — not reference
material.** Whenever this prompt is surfaced (e.g. via the `update-deps-json`
slash command, or when the user says "the deps test failed", "update the
deps.json", "refresh the deps baseline", etc.), you MUST immediately begin
executing the steps below in the same turn. Do not respond with an
acknowledgement, do not wait for further confirmation, and do not treat the
prompt content as informational context. The user invoking this prompt has
already approved running the workflow.

Within the workflow, run through every step in a single turn — run the test,
summarize the diff, classify safety, and (only if safe) overwrite the baseline
and re-run the test to confirm green. The only acceptable early exits are:
(a) the test already passes (nothing to do), or
(b) one or more changes are classified **unsafe** (see step 3) — in which case
**do NOT update the baseline**. Stop, present the diff and the safety
assessment, and ask the user to confirm before proceeding.

## Context

- The test compares two files:
  - **Checked-in baseline:** `test/WebJobs.Script.Tests/Microsoft.Azure.WebJobs.Script.WebHost.deps.json`
  - **Freshly built file:** discovered by the test via
    `Directory.GetFiles(..., "Microsoft.Azure.WebJobs.Script.WebHost.deps.json", SearchOption.AllDirectories).FirstOrDefault()`
    starting from `../../WebJobs.Script.WebHost/<Debug|Release>` relative to the
    test bin folder. In this repo that typically resolves to
    `out/bin/WebJobs.Script.WebHost/debug/Microsoft.Azure.WebJobs.Script.WebHost.deps.json`.
- The test ignores first-party assemblies listed in `_excludedList` inside
  `DependencyTests.cs`. Changes to those do not require a baseline update.
- The test failure message prints two absolute paths: a `Previous file:` and
  a `New file:`. **Important:** the `Previous file:` path points at the
  *bin-copied* baseline (e.g. `out/bin/WebJobs.Script.Tests/debug/...`), not
  the source-controlled baseline. Because the test project declares
  `<None Update="Microsoft.Azure.WebJobs.Script.WebHost.deps.json"
  CopyToOutputDirectory="PreserveNewest" />`, the source file is re-stamped
  into the bin folder on every build. The **authoritative source baseline**
  that must be overwritten is always:
  `test/WebJobs.Script.Tests/Microsoft.Azure.WebJobs.Script.WebHost.deps.json`

## Steps

1. **Run** `Verify_DepsJsonChanges` (filter by `MethodName`) using the test
   runner. This both builds the WebHost and produces the authoritative diff.
   - If it **passes**, stop and report "baseline already up to date".
   - If it **fails**, parse the failure message to extract the
     `Previous file:` and `New file:` paths and the Changed / Removed / Added
     lists.
2. **Summarize** the changes for the user in a single concise block, grouped:
   - **Added** (new assemblies)
   - **Removed** (dropped assemblies)
   - **Changed** (assembly version / file version transitions)
3. **Classify safety.** Mark the diff as **unsafe** if any of the following
   apply; otherwise mark it **safe**:
   - A major-version bump on any assembly (e.g. `12.x → 13.x`).
   - Any change (added / removed / version bump at any level) involving a
     security-sensitive package family: `System.Text.Json`,
     `Microsoft.IdentityModel.*`, `Azure.*`, `Microsoft.Azure.*` SDKs,
     `Grpc.*`, `OpenTelemetry.*`, `Microsoft.AspNetCore.*` cryptography /
     authentication assemblies.
   - An assembly was **removed** that does not have a clear replacement in
     the **Added** list.
   - The number of changed assemblies is large (> ~25) or appears
     unrelated to a recent intended package change.

   Present the safety classification explicitly in the summary, with a
   one-line rationale per flagged item.
4. **Gate on safety.**
   - If **safe**: proceed to step 5.
   - If **unsafe**: STOP. Do not overwrite the baseline. Report the diff and
     the safety findings, and ask the user to confirm before continuing.
5. **Overwrite the baseline.** Copy the `New file` path from the failure
   message over the source-controlled baseline at
   `test/WebJobs.Script.Tests/Microsoft.Azure.WebJobs.Script.WebHost.deps.json`
   (NOT the `Previous file:` path from the failure message — see Context).
   Also overwrite the bin copy at the `Previous file:` path so the immediate
   re-run does not race with MSBuild's `PreserveNewest` copy. Use a straight
   file copy (`Copy-Item -Force` in pwsh) — do not hand-edit JSON.
6. **Re-run** `Verify_DepsJsonChanges` to confirm it now passes. If it still
   fails, stop and report the new diff — do not loop.
7. **Report** the final summary, the safety classification, and the relative
   path of the updated file so the user can include it in their commit
   message.

## Notes

- Never modify `DependencyTests.cs` or `_excludedList` to "fix" the failure
  unless the user explicitly asks for it.
- Do not change `global.json`, `NuGet.config`, or package versions as part of
  this workflow — this prompt only refreshes the baseline to match an already
  intended dependency change.
