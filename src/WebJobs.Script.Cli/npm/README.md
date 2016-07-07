Install:

```bash
> npm i -g azure-functions-cli
```

Local -> Azure:

```bash
> mkdir functions
> cd functions
> func init
> func new # launches yoeman generator (assume functionName=HttpCSharp)
>  func run HttpCSharp -f file.json
> git commit -am "First function"
> func new functionapp MyNewAwesomeFunctionApp -l WestUS
> func list functionapps # displays the git url for the function app
> func user # lets me reset publishing user.
>  git push https://MyNewAwesomeFunctionApp.scm.azurewebsites.net/ master
> func open MyNewAwesomeFunctionApp # launches default web browser with deep link to portal
```

Azure -> Local:

```bash
> func login
> func list functionapps # displays a list of function apps
> func switch-tenants # displays a list of tenants
> func switch-tenants <tenantId> 
> func list functionapps
> git clone https://MyNewAwesomeFunctionApp.scm.azurewebsites.net
> cd MyNewAwesomeFunctionApp
> func run FunctionName # then you go in the portal and switch to local
```

```bash
> func help
Azure Functions CLI 0.1
Usage: func [verb] [Options]

   config          get and set global cli config options
   fetch           fetches function app secrets
   login           Clears login cache and prompts for a new login
   logout          Clears login cache
   open            Launch default browser with link to the function app in https://portal.azure.com
   switch-tenants  List and switch current tenant for the Cli
   init            Creates .gitignore, and host.json. Runs git init .
   set             Not yet Implemented
   new             Handle creating a new function or function app
   run             Run the specified function locally
   user            Helps manage publishing username
   web             Launches a Functions server endpoint locally
   list            Lists function apps in current tenant. See switch-tenant command


Tip: run func init to get started.
```

```bash
>  azf help new
Azure Functions CLI 0.1
Usage: func new [function/functionApp] <functionAppName> [Options]

   <newOption>          [Function/FunctionApp/StorageAccount/Secret]
   <functionAppName>    (Required)
   -s/--subscription    Subscription to create function app in
   -l/--location        Geographical location for your function app

Tip: run func init to get started.
```

```bash
>  func help run
Azure Functions CLI 0.1
Usage: func run <functionName> [Options]

   <functionName>    (Required)
   -t/--timeout      Time to wait until Functions Server is ready in Seconds
   -c/--content      In line content to use
   -f/--file         File name to use as content
   -d/--debug        Attach a debugger to the host process before running the function.

Tip: run func init to get started.
```


```bash
>  func help list
Azure Functions CLI 0.1
Usage: func list FunctionApps [Options]

Usage: func list Secrets [Options]

   -a/--show    Display the secret value

Usage: func list StorageAccounts [Options]

Usage: func list Tenants [Options]

Tip: run func init to get started.
```


```bash
>  func help user
Azure Functions CLI 0.1
Usage: func user <userName> [Options]

   <userName>    (Required)

Tip: run func init to get started.
```

```bash
>  func help config
Azure Functions CLI 0.1
Usage: func config <name> <value> [Options]

   <name>     (Required)
   <value>    (Required)
```
