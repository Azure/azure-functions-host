![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-webjobs-sdk-script/rename-cli/src/WebJobs.Script.Cli/npm/assets/azure-functions-logo-color-raster.png)

# Azure Functions CLI

The Azure Functions CLI provides a local development experience for creating, developing, testing, running, and debugging Azure Functions. 

## Installing

**NOTE**: This package only currently works on Windows and must be installed globally, since the underlying Functions Host is not yet cross-platform. You can upvote this GitHub issue if you're interested in running on other platforms: [make the Azure Functions CLI cross platform](https://github.com/Azure/azure-webjobs-sdk-script/issues/509).

Make sure you're using a Node version 6.x LTS or later, as the Yeoman dependency requires this.

To install:

```
npm i -g azure-functions-cli
```

### Aliases

The package sets up the following global aliases:

```
func
azfun
azure-functions
```

## Commands

The CLI commands have the following basic structure:

```
func [context] [context] <action> [-/--options]
```

### Contexts

```
azure        For Azure login and working with Function Apps on Azure
function     For local function settings and actions
functionapp  For local function app settings and actions
host         For local Functions host settings and actions
settings     For local settings for your Functions host
```

### Top-level actions

```
func init    Create a new Function App in the current folder. Initializes git repo.
func run     Run a function directly
```

### Azure actions

Actions in the "azure" context require logging in to Azure.

```
func azure

Usage: func azure [context] <action> [-/--options]

Contexts:
account        For Azure account and subscriptions settings and actions
functionapp    For Azure Function App settings and actions
storage        For Azure Storage settings and actions
subscriptions  For Azure account and subscriptions settings and actions

Actions:
get-publish-username  Get the source control publishing username for a Function App in Azure
set-publish-password  Set the source control publishing password for a Function App in Azure
login                 Log in to an Azure account. Can also do "func azure login"
logout                Log out of Azure account. Can also do "func azure logout"
portal                Launch default browser with link to the current app in https://portal.azure.com
```

```
func azure account
Usage: func azure account <action> [-/--options]

Actions:
set <subscriptionId> Set the active subscription 
list  List subscriptions for the logged in user
```

```
func azure functionapp
Usage: func azure functionapp <action> [-/--options]

Actions:
create              Create a new Function App in Azure with default settings
enable-git-repo     Enable git repository on your Azure-hosted Function App
fetch-app-settings  Retrieve App Settings from your Azure-hosted Function App and store locally. Alias: fetch
list                List all Function Apps in the selected Azure subscription
```

The `func azure storage list` command will show storage accounts in the selected subscription. You can then set up a connection string locally with this storage account name using `func settings add-storage-account`.  

```
func azure storage
Usage: func Azure Storage <action> [-/--options]

Actions:
list  List all Storage Accounts in the selected Azure subscription
```

### Local actions

Actions that are not in the "azure" context operate on the local environment. For instance, `func settings list` will show the app settings for the current function app.

```
func settings
Usage: func settings [context] <action> [-/--options]

Actions:
add                  Add new local app setting to appsettings.json
add-storage-account  Add a local app setting using the value from an Azure Storage account. Requires Azure login.
decrypt              Decrypt the local settings file
delete               Remove a local setting
encrypt              Encrypt the local settings file
list                 List local settings
```

```
func function
Usage: func function [context] <action> [-/--options]

Actions:
create  Create a new Function from a template, using the Yeoman generator
run     Run a function directly 
```

For consistency, the `func init` command can also be invoked via `func function app init`. 

```
func functionapp init              
```

## License

This project is under the benevolent umbrella of the [.NET Foundation](http://www.dotnetfoundation.org/) and is licensed under [the MIT License](LICENSE.txt)

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Contact Us

For questions on Azure Functions or the CLI, you can ask questions here:

- [Azure Functions MSDN Forum](https://social.msdn.microsoft.com/Forums/azure/en-US/home?forum=AzureFunctions)
- [Azure-Functions tag on StackOverflow](http://stackoverflow.com/questions/tagged/azure-functions)

To file bugs, post an issue with the prefix "CLI:" in the [Azure Functions Script repo on GitHub](https://github.com/Azure/azure-webjobs-sdk-script/issues).