# Repro: Worker Startup Timeout → Host Deadlock (Permanent 503s)

This reproduces a bug where a worker startup timeout during the first host startup causes the
Functions Host to enter a permanent deadlock, returning 503 for all requests indefinitely.

**Related ICM:** 51000000885979  
**Host version:** v4.1046.100

## Quick Start

```powershell
cd <azure-functions-host repo root>

# Set environment variables
$env:AzureWebJobsScriptRoot = "$PWD\test-apps\worker-startup-timeout-repro\sample-app"
$env:FUNCTIONS_WORKER_RUNTIME = "node"
$env:languageWorkers__workersDirectory = "$PWD\test-apps\worker-startup-timeout-repro\workers"
$env:AzureWebJobsStorage = ""
$env:FUNCTIONS_WORKER_RUNTIME_VERSION = "~20"
$env:AzureWebJobsSecretStorageType = "files"

# Run the host
dotnet run --project src\WebJobs.Script.WebHost\WebJobs.Script.WebHost.csproj --no-launch-profile
```

Wait ~10 seconds, then in another terminal:

```powershell
curl http://localhost:5000/api/hello
# → HTTP 503: Function host is not running.
# (This will never recover — the host is deadlocked)
```

## How It Works

- `workers/node/worker.config.json` sets `processStartupTimeout` to **5 seconds**
- `workers/node/dist/src/nodejsWorker.js` is a wrapper that sleeps **8 seconds** before
  loading the real node worker — so the `StartStream` gRPC message arrives 3 seconds
  after the timeout fires
- `sample-app/` contains a minimal Node.js HTTP function (never actually loads — the timeout
  fires before the worker finishes starting)

## What Happens

1. Host starts, calls `BuildHost()` → `GetFunctionMetadataAsync()` → starts the node worker
2. Worker process launches but the delay wrapper sleeps 8 seconds
3. `ProcessStartupTimeout` (5s) fires → `TimeoutException` → `ExternalStartupException`
4. `BuildHost()` throws **before** `ActiveHost = localHost` (line 383) is reached
5. `_currentJobHost` was never set (stays null from construction)
6. Host enters `Error` state, retries after 1-second backoff
7. Retry calls `BuildHost()` → `GetFunctionMetadataAsync()` → `IsJobHostStarting()`:
   - `State == Error` → first check fails
   - `_currentJobHost is null` → second check fails
   - Returns **false** → calls `RestartHostAsync()` instead of `InitializeChannelAsync()`
8. `RestartHostAsync()` cancels the retry op, then hits `await _hostStartSemaphore.WaitAsync()`
9. **Deadlock**: `RestartHostAsync` is waiting to acquire `_hostStartSemaphore`, but the semaphore
   is held by the retry's own `StartHostAsync` (line 307) — which is blocked on the current thread.
   The full call chain on the blocked thread is:

   ```
   StartHostAsync                         ← HOLDS _hostStartSemaphore (line 307)
     → UnsynchronizedStartHostCoreAsync
       → BuildHost()
         → FunctionMetadataManager
           → .GetFunctionMetadataAsync()
             .GetAwaiter().GetResult()     ← SYNC BLOCK — thread cannot be released
               → WorkerFunctionMetadataProvider.GetFunctionMetadataAsync()
                 → await RestartHostAsync()
                   → await _hostStartSemaphore.WaitAsync()   ← NEEDS the same semaphore
   ```

   The `.GetAwaiter().GetResult()` at `FunctionMetadataManager.cs:150` blocks the thread
   synchronously. When `RestartHostAsync` hits `await _hostStartSemaphore.WaitAsync()`, the
   await returns an incomplete Task. That Task propagates back up to `.GetResult()`, which
   blocks the thread waiting for it to complete. But the Task can only complete when the
   semaphore is released, and the semaphore can only be released when `StartHostAsync`'s
   `finally` block runs, which requires `BuildHost()` to return, which requires this thread
   to unblock — circular dependency, permanent deadlock.
10. Host stuck permanently — 503 on every request

## Bug Location

| File | Lines | Issue |
|------|-------|-------|
| `WorkerFunctionMetadataProvider.cs` | 162-186 | `IsJobHostStarting()` returns `false` when `_currentJobHost` is null and state is Error |
| `WorkerFunctionMetadataProvider.cs` | 97-98 | Calls `RestartHostAsync()` from within `BuildHost()` call chain |
| `FunctionMetadataManager.cs` | 150 | `.GetAwaiter().GetResult()` sync-over-async enables the deadlock |
| `WebJobsScriptHostService.cs` | 627 | `_hostStartSemaphore.WaitAsync()` without cancellation token |

## Prerequisites

- .NET 8 SDK
- Node.js installed
- The node worker files must be copied from Azure Functions Core Tools (see Setup below)

## Setup (One-Time)

The `workers/node/dist/src/nodejsWorker.js` is the delay wrapper. You need to copy the **real**
node worker from Azure Functions Core Tools as `nodejsWorker.real.js`:

```powershell
$coreTools = "C:\Program Files\Microsoft\Azure Functions Core Tools"
$repro = "test-apps\worker-startup-timeout-repro\workers\node"

# Copy the real worker as .real.js
Copy-Item "$coreTools\workers\node\dist\src\nodejsWorker.js" "$repro\dist\src\nodejsWorker.real.js"

# Copy supporting files
Copy-Item "$coreTools\workers\node\package.json" "$repro\package.json"
Get-ChildItem "$coreTools\workers\node\dist" -Recurse | Where-Object { !$_.PSIsContainer -and $_.Name -ne "nodejsWorker.js" } | ForEach-Object {
    $rel = $_.FullName.Substring("$coreTools\workers\node\dist".Length)
    $dest = "$repro\dist$rel"
    New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
    Copy-Item $_.FullName $dest -Force
}
```
