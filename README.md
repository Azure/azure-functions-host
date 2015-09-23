Azure WebJobs SDK Script
===
This repo contains libraries that enable a **light-weight scripting model** for the [Azure WebJobs SDK](http://github.com/Azure/azure-webjobs-sdk). You simply provide job function scripts written in various languages (e.g. Javascript/[Node.js](http://nodejs.org), CSharp, etc.) along with a simple **manifest.json** file that indicates how those functions should be invoked, and the scripting library does the work necessary to plug those scripts into the WebJobs SDK JobHost runtime (i.e. "Make It Happen!").

This opens the door for interesting **UI driven scenarios**, where the user simply chooses trigger options from drop-downs, provides a job script, and the corresponding manifest.json is generated behind the scenes. The scripting runtime is able to take these two simple inputs and set everything else up. The engine behind the scenes is the [Azure WebJobs SDK](https://github.com/Azure/azure-webjobs-sdk) - this library just layers on top to allow you to "**script the WebJobs SDK**". So you get the full benefits of the power of the WebJobs SDK, including the [WebJobs Dashboard](http://azure.microsoft.com/en-us/documentation/videos/azure-webjobs-dashboard-site-extension/). 

As an example, here's a simple Node.js job function in a processWorkItem.js file:

```javascript
module.exports = function (context) {
    var workItem = context.input;
    console.log('Node.js job function processed work item ' + workItem.ID);
    context.done();
}
```

And here's the corresponding manifest.json file that instructs the runtime to invoke this function whenever a new queue message is added to the 'samples-workitems' Azure Storage Queue:

```javascript
{
  "functions": [
    {
      "source": "processWorkItem.js",
      "trigger": {
          "type": "queue",
          "queueName": "samples-workitems"
        }
    }
  ]
}
```
That's all that is required from the user! The runtime will be initialized automatically with these inputs, live monitoring of the Azure Queue will begin, and the function will be invoked when queue messages are added.

When hosted in an [Azure Web App](http://azure.microsoft.com/en-us/services/app-service/web/) this means there is **no compilation + publish step required**. Simply by modifying a script file, the Web App will automatically restart, load the new script content + metadata, and the changes are **live**. Scripts and their metadata can be modified quickly on the fly in the browser (e.g. in a first class UI or in the [Kudu Console](http://github.com/projectkudu/kudu/wiki/Kudu-console)) and the changes take effect immediately.
