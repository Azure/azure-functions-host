# Web Function sample

This is a Node.js HTTP server with no `host.json`, Functions SDK, or npm dependencies.
The host starts `index.js`, assigns a loopback endpoint, and forwards requests to the
server. `/events` demonstrates server-sent events.

## Run with this repository's host

From the repository root in PowerShell, with .NET 10 and Node.js installed:

```powershell
$env:AzureFunctionsJobHost__configurationProfile = "web-function"
$env:FUNCTIONS_WORKER_RUNTIME = "node"
$env:AzureWebJobsScriptRoot = (Resolve-Path .\sample\WebFunction).Path
$env:AzureWebJobsSecretStorageType = "files"
$env:ASPNETCORE_URLS = "http://127.0.0.1:7071"
dotnet run --project .\src\WebJobs.Script.WebHost --no-launch-profile
```

In another terminal:

```powershell
Invoke-RestMethod http://127.0.0.1:7071/
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:7071/echo?name=Azure' -Body 'hello'
curl.exe -N http://127.0.0.1:7071/events
```

Stop the host with Ctrl+C. To run the server without the Functions host, use
`node .\sample\WebFunction\index.js`; it listens on `127.0.0.1:3000`.

The profile is currently an explicit host setting; Azure app creation, deployment,
and Core Tools have not yet been updated to select it automatically. HTTP routes
in this profile are anonymous by default, so the application or a front-end
authentication service must protect routes that are not public. Normal Functions
host and hosting-platform request limits and timeouts still apply; the HTTP proxy
also has a 240-second inactivity timeout. WebSockets have not yet been validated.
The host does not restart the app when it creates application directories or
writes caches under `node_modules`; restart or redeploy the app after changing its code.
Functions management endpoints such as `/admin/host/status` remain reserved
and are not forwarded to the application.
On shutdown the host force-stops the web app and its child processes; graceful
application shutdown and in-flight request draining are not implemented yet.

## Startup conventions

The platform or local environment selects `node`, `python`, or `java` with
`FUNCTIONS_WORKER_RUNTIME`. If it is unset or `custom`, the host detects a single
runtime from the application files; ambiguous projects must select one explicitly.
`FUNCTIONS_WEB_ENTRYPOINT` can override the entry point without a `host.json`.

| Runtime | Default startup | Entry-point override |
| --- | --- | --- |
| Node.js | `package.json` `scripts.start` (using npm), then `main`, or `index.js`, `server.js`, `app.js`, and their `.mjs`/`.cjs` variants | Path to a JavaScript file |
| Python | An `app` object in `wsgi.py`, `app.py`, or `main.py` | `module:application` |
| Java | One runnable JAR in the app root, `target`, or `build/libs` | Path to a JAR |

Python WSGI apps use Gunicorn on Unix and Waitress on Windows. The selected server
and application dependencies must be installed for the Python interpreter. To run
an ASGI application with Uvicorn instead, set `FUNCTIONS_WEB_PYTHON_PROTOCOL=asgi`
and install Uvicorn. Azure-style `.python_packages/lib/site-packages` is added to
the child process's `PYTHONPATH` when present. Java apps need a compatible Java
runtime; the host uses `JAVA_HOME` when set, or `java` on `PATH` otherwise.
Spring Boot receives `SERVER_PORT` and `SERVER_ADDRESS`.

Every web app receives `FUNCTIONS_HTTP_ENDPOINT` as `127.0.0.1:<port>` and `PORT`
as the port number. Apps must listen on that endpoint. The host's existing
custom-handler readiness check, process restart, and HTTP streaming proxy are used.
