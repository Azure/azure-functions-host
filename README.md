![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-cli/master/src/Azure.Functions.Cli/npm/assets/azure-functions-logo-color-raster.png)

|Branch|Windows|Linux|
|---|---|---|
|master|[![Build status](https://ci.appveyor.com/api/projects/status/a6j46j1tawdfs3js?svg=true&branch=master)](https://ci.appveyor.com/project/appsvc/azure-webjobs-sdk-script-y8o14?branch=master)|[![Build status](https://ci.appveyor.com/api/projects/status/03j5j88yk9489y33/branch/master?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-host-l70a7/branch/master)|
|dev|[![Build status](https://ci.appveyor.com/api/projects/status/a6j46j1tawdfs3js?svg=true&branch=dev)](https://ci.appveyor.com/project/appsvc/azure-webjobs-sdk-script-y8o14?branch=dev)|[![Build status](https://ci.appveyor.com/api/projects/status/03j5j88yk9489y33/branch/dev?svg=true)](https://ci.appveyor.com/project/appsvc/azure-functions-host-l70a7/branch/dev)|
|v1.x|[![Build status](https://ci.appveyor.com/api/projects/status/a6j46j1tawdfs3js?svg=true&branch=v1.x)](https://ci.appveyor.com/project/appsvc/azure-webjobs-sdk-script-y8o14?branch=v1.x)| `NA` |

WebJobs.Script
===

This repo contains libraries that enable a **light-weight scripting model** for the [Azure WebJobs SDK](http://github.com/Azure/azure-webjobs-sdk). You simply provide job function **scripts** written in various languages (e.g. Javascript/[Node.js](http://nodejs.org), C#, Python, F#, PowerShell, PHP, CMD, BAT, BASH scripts, etc.) along with a simple **function.json** metadata file that indicates how those functions should be invoked, and the scripting library does the work necessary to plug those scripts into the [Azure WebJobs SDK](https://github.com/Azure/azure-webjobs-sdk) runtime.

These libraries are the runtime used by [Azure Functions](https://azure.microsoft.com/en-us/services/functions/). The runtime builds upon the tried and true [Azure WebJobs SDK](https://github.com/Azure/azure-webjobs-sdk) - this library just layers on top to allow you to "**script the WebJobs SDK**". So you get the full benefits and the power of the WebJobs SDK, including the [WebJobs Dashboard](http://azure.microsoft.com/en-us/documentation/videos/azure-webjobs-dashboard-site-extension/). 

**An important note on language support levels** - while many languages are supported, their level of support differs in important ways, making some languages more suitable than others for certain workloads. The **first class** languages are C#, F# and Javascript/Node.js. Functions written in these languages are run **in process** and are suitable for any workload. The remaining languages are considered **experimental**. Functions written in these languages are scripts that are run **out of process**. While there are many scenarios where this is acceptable, it won't be acceptable for high load scenarios where the overhead of a new process for each invocation won't scale.

As an example, here's a simple Node.js function that receives a queue message and writes that message to Azure Blob storage:

```javascript
module.exports = function (context, workItem) {
    context.log('Node.js queue trigger function processed work item ', workItem.id);
    context.bindings.receipt = workItem;
    context.done();
}
```

And here's the corresponding **function.json** file which includes a trigger **input binding** that instructs the runtime to invoke this function whenever a new queue message is added to the `samples-workitems` queue:

```javascript
{
  "bindings": [
    {
      "type": "queueTrigger",
      "direction": "in",
      "queueName": "samples-workitems"
    },
    {
      "type": "blob",
      "name": "receipt",
      "direction": "out",
      "path": "samples-workitems/{id}"
    }
  ]
}
```

The `receipt` blob **output binding** that was referenced in the code above is also shown. Note that the blob binding path `samples-workitems/{id}` includes a parameter `{id}`. The runtime will bind this to the `id` property of the incoming JSON message. Functions can be just a single script file, or can include additional files/content. For example, a Node.js function might include a node_modules folder, multiple .js files, etc. A PowerShell function might include and load additional companion scripts.

Here's a Windows Batch script that uses the **same function definition**, writing the incoming messages to blobs (it could process/modify the message in any way):

```batch
SET /p workItem=<%input%
echo Windows Batch script processed work item '%workItem%'
echo %workItem% > %receipt%
```

And here's a Python function for the same function definition doing the same thing:

```python
import os

# read the queue message and write to stdout
workItem = open(os.environ['input']).read()
message = "Python script processed work item '{0}'".format(workItem)
print(message)

# write to the output binding
f = open(os.environ['receipt'], 'w')
f.write(workItem)
```

Note that for all script types other than Node.js, binding inputs are made available to the script via environment variables, and output logs is written via STDOUT. You can see more script language [examples here](http://github.com/Azure/azure-webjobs-sdk-script/tree/master/sample).

The samples also includes a canonical [image resize sample](http://github.com/Azure/azure-webjobs-sdk-script/tree/master/sample/ResizeImage). This sample demonstrates both input and output bindings. Here's the **function.json**:

```javascript
{
  "bindings": [
    {
      "type": "blobTrigger",
      "name": "original",
      "direction": "in",
      "path": "images-original/{name}"
    },
    {
      "type": "blob",
      "name": "resized",
      "direction": "out",
      "path": "images-resized/{name}"
    }
  ]
}
```

When the script is triggered by a new image in `images-original`, the input binding reads the original image from blob storage (binding to the `name` property from the blob path), sets up the output binding, and invokes the script. Here's the batch script (resize.bat):

```batch
.\Resizer\Resizer.exe %original% %resized% 200
```

Using Resizer.exe which is part of the function content, the operation is a simple one-liner. The bound paths set up by the runtime are passed into the resizer, the resizer processes the image, and writes the result to `%resized%`. The ouput binding uploads the image written to `%resized%` to blob storage.

On startup, the script runtime loads all scripts and metadata files and begins listening for events (e.g. new Queue messages, Blobs, etc.). Functions are invoked automatically when their trigger events are received. Virtually all of the WebJobs SDK triggers (including [Extensions](http://github.com/Azure/azure-webjobs-sdk-extensions)) are available for scripting. Most of the configuration options found in the WebJobs SDK can also be specified via json metadata.

When hosted in an [Azure Web App](http://azure.microsoft.com/en-us/services/app-service/web/) this means there is **no compilation + publish step required**. Simply by modifying a script file, the runtime will load the new script content + metadata, and the changes are **live**. Scripts and their metadata can be modified quickly on the fly in a browser editor (e.g. in a first class UI or in the [Kudu Console](http://github.com/projectkudu/kudu/wiki/Kudu-console)) and the changes take effect immediately.

The Script library is available as a Nuget package (**Microsoft.Azure.WebJobs.Script**). Currently this package is available on the [App Service Myget feed](http://github.com/Azure/azure-webjobs-sdk/wiki/%22Nightly%22-Builds).

Please see the [Wiki](https://github.com/Azure/azure-webjobs-sdk-script/wiki) for more information on how to use and deploy the library, and also please log any issues/feedback on our [issues list](https://github.com/Azure/azure-webjobs-sdk-script/issues) and we'll investigate.

### License

This project is under the benevolent umbrella of the [.NET Foundation](http://www.dotnetfoundation.org/) and is licensed under [the MIT License](LICENSE.txt)

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

### Questions

See the [getting help](https://github.com/Azure/azure-webjobs-sdk-script/wiki#getting-help) section in the wiki.
