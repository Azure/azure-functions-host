Azure WebJobs SDK Script
===
This repo contains libraries that enable a **light-weight scripting model** for the [Azure WebJobs SDK](http://github.com/Azure/azure-webjobs-sdk). You simply provide job function **scripts** written in various languages (e.g. Javascript/[Node.js](http://nodejs.org), Python, F#, PowerShell, PHP, CMD, BAT, BASH scripts, etc.) along with a simple **function.json** metadata file that indicates when those functions should be invoked, and the scripting library does the work necessary to plug those scripts into the [Azure WebJobs SDK](https://github.com/Azure/azure-webjobs-sdk) runtime (i.e. "Make It Happen!").

This opens the door for interesting **UI driven scenarios**, where the user simply chooses trigger options from drop-downs, provides a job script, and the corresponding function.json is generated behind the scenes. The scripting runtime is able to take these two simple inputs and set everything else up. The engine behind the scenes is the tried and true [Azure WebJobs SDK](https://github.com/Azure/azure-webjobs-sdk) - this library just layers on top to allow you to "**script the WebJobs SDK**". So you get the full benefits and the power of the WebJobs SDK, including the [WebJobs Dashboard](http://azure.microsoft.com/en-us/documentation/videos/azure-webjobs-dashboard-site-extension/). 

As an example, here's a simple Node.js job function that receives a queue message and writes that message to Azure Blob storage:

```javascript
var util = require('util');

module.exports = function (context) {
    context.log('Node.js queue trigger function processed work item ' 
        + util.inspect(context.workItem.id));

    context.output({
        receipt: JSON.stringify(context.workItem)
    })

    context.done();
}
```

And here's the corresponding function.json file which includes  the trigger instructs the runtime to invoke this function whenever a new queue message is added to the 'samples-workitems' Azure Storage Queue, as well as the output blob binding:

```javascript
{
  "trigger": {
    "type": "queue",
    "name": "workItem",
    "queueName": "samples-workitems"
  },
  "outputs": [
    {
      "type": "blob",
      "name": "receipt",
      "path": "samples-workitems/{id}"
    }
  ]
}
```

A Python script might look like this:

```python
input = input();
message = "Python script processed queue message '{0}'".format(input)
print(message)
```

Note that for all script types other than Node.js, trigger input is passed via STDIN, and output is written via STDOUT.

The runtime will be initialized automatically with these inputs, live monitoring of the Azure Queue will begin, and the function will be invoked when queue messages are added. In addition to Queue processing, all the other WebJobs SDK triggers are supported - triggering on new Blobs, cron scheduled functions, ServiceBus queues, etc. Virtually all of the WebJobs SDK triggers (including [Extensions](http://github.com/Azure/azure-webjobs-sdk-extensions)) are available for scripting. Most of the configuration options found in the WebJobs SDK can also be specified via json metadata.

When hosted in an [Azure Web App](http://azure.microsoft.com/en-us/services/app-service/web/) this means there is **no compilation + publish step required**. Simply by modifying a script file, the Web App will automatically restart, load the new script content + metadata, and the changes are **live**. Scripts and their metadata can be modified quickly on the fly in the browser (e.g. in a first class UI or in the [Kudu Console](http://github.com/projectkudu/kudu/wiki/Kudu-console)) and the changes take effect immediately.

The Script library is available as a Nuget package (**Microsoft.Azure.WebJobs.Script**). Currently this package is available on the [App Service Myget feed](http://github.com/Azure/azure-webjobs-sdk/wiki/%22Nightly%22-Builds).

Please see the [Wiki](https://github.com/Azure/azure-webjobs-sdk-script/wiki) for more information, and also please log any issues/feedback on our [issues list](https://github.com/Azure/azure-webjobs-sdk-script/issues) and we'll investigate. 
