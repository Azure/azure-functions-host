# Web Function Express sample

An ordinary Express 5 todo app (static page plus JSON API) with no `host.json`, no Functions SDK,
and no Azure-specific code. The only hosting convention it follows is listening on `process.env.PORT`.

## Run with this repository's host

From the repository root in PowerShell, with .NET 10 and Node.js installed:

```powershell
npm install --prefix .\sample\WebFunctionExpress

$env:AzureFunctionsJobHost__configurationProfile = "web-function"
$env:FUNCTIONS_WORKER_RUNTIME = "node"
$env:AzureWebJobsScriptRoot = (Resolve-Path .\sample\WebFunctionExpress).Path
$env:AzureWebJobsSecretStorageType = "files"
$env:ASPNETCORE_URLS = "http://127.0.0.1:7071"
dotnet run --project .\src\WebJobs.Script.WebHost --no-launch-profile
```

The host finds `scripts.start` in `package.json`, runs it with npm, and forwards every
non-management request to the Express app on a loopback port it assigns.

Open `http://127.0.0.1:7071/` in a browser to use the todo page, or call the API directly:

```powershell
Invoke-RestMethod http://127.0.0.1:7071/api/info
Invoke-RestMethod -Method Post http://127.0.0.1:7071/api/todos -ContentType 'application/json' -Body '{"title":"try it"}'
Invoke-RestMethod http://127.0.0.1:7071/api/todos
Invoke-RestMethod -Method Patch http://127.0.0.1:7071/api/todos/1 -ContentType 'application/json' -Body '{"done":true}'
Invoke-RestMethod -Method Delete http://127.0.0.1:7071/api/todos/1
curl.exe -i http://127.0.0.1:7071/old-home     # 302 redirect passed through
curl.exe -i http://127.0.0.1:7071/api/error    # Express error handler returns 500
Invoke-RestMethod http://127.0.0.1:7071/api/headers   # shows X-Forwarded-* headers added by the host
```

To run the app without the Functions host, use `npm start --prefix .\sample\WebFunctionExpress`;
it listens on `127.0.0.1:3000`.

## Notes

- The todo list is kept in memory, so it is reset when the app restarts and is not shared
  across scaled-out instances. Real apps should store state externally.
- The app sees the internal loopback address in the `Host` header. The public host and scheme
  arrive in `X-Forwarded-Host` and `X-Forwarded-Proto`; set `app.set('trust proxy', ...)` if the
  app builds absolute URLs from `req.hostname` or `req.protocol`.
- See [the Node.js web function sample](../WebFunction/README.md) for startup conventions and
  current limitations of the `web-function` profile.
